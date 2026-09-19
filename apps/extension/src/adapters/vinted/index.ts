import type { IngestListing, IngestOrder, RunIssue } from "../../agent/types";
import {
  AdapterError,
  type ListingsSnapshot,
  type MarketplaceAdapter,
  type MarketplaceContext,
  type PageKind,
  type SalesScan,
  type SalesWindow,
  type Session,
} from "../types";
import { toIngestListing, type VintedWardrobe } from "./listings";
import { saleStamp, saleState, toIngestOrder, type VintedOrderList, type VintedOrderRow, type VintedTransaction } from "./orders";
import { assertReadable, signedOutPage, siteChanged } from "./pages";

const origin = "https://www.vinted.com";

/** The seller's sold orders — an ordinary Vinted page, and loading one renews a lapsed sign-in. */
const ordersPage = `${origin}/my_orders?order_type=sold`;

/** As Vinted's own orders page asks for them. */
const ordersPerPage = 20;
const itemsPerPage = 96;

/** A scan never reads more pages than this; past it, it stops and says so. */
export const maxPages = 250;

/** A sale as remembered between scans: read in full once, and again only when the list shows it changed. */
interface RememberedSale {
  stamp: string;
  order: IngestOrder | null;
}

/**
 * Asks Vinted's API for JSON, from Vinted's own page. A lapsed sign-in answers 401 until something
 * renews it — Vinted's pages do, as they load — so on a 401 one is loaded and the question asked
 * again; still refused, or showing the signed-out page, is signed out.
 */
async function readJson<T>(context: MarketplaceContext, path: string, what: string): Promise<T> {
  let response = await context.request(`${origin}${path}`);
  if (response.status === 401) {
    const page = await context.page(ordersPage);
    assertReadable(page);
    if (signedOutPage(page)) {
      throw new AdapterError("SignedOut", "The browser is signed out of Vinted.");
    }

    response = await context.request(`${origin}${path}`);
  }

  assertReadable(response);
  try {
    return JSON.parse(response.body) as T;
  } catch {
    throw siteChanged(what);
  }
}

/**
 * One sale from the order list: from memory when the list shows it unchanged since it was last read,
 * otherwise read in full. A cancelled sale needs no reading — only telling. A sale whose details
 * cannot be read is reported, and left unremembered so the next scan tries it again.
 */
async function readSale(context: MarketplaceContext, row: VintedOrderRow): Promise<{ order: IngestOrder | null; issues: RunIssue[]; changed: boolean }> {
  const key = `sale:${row.transaction_id}`;
  const stamp = saleStamp(row);
  const known = await context.memory.get<RememberedSale>(key);
  if (known?.stamp === stamp) {
    return { order: known.order, issues: [], changed: false };
  }

  if (saleState(row) === "cancelled") {
    await context.memory.set<RememberedSale>(key, { stamp, order: null });
    return { order: null, issues: [], changed: true };
  }

  let detail: VintedTransaction;
  try {
    detail = await readJson<VintedTransaction>(context, `/api/v2/transactions/${encodeURIComponent(String(row.transaction_id))}`, "sale details");
  } catch (error) {
    // Trouble with the whole site stops the scan; trouble with this one sale only skips it.
    if (error instanceof AdapterError && error.kind === "Error") {
      return { order: null, issues: [{ message: `This sale could not be read. ${error.message}`, externalReference: String(row.transaction_id), rejected: true }], changed: true };
    }

    throw error;
  }

  const { order, issues } = toIngestOrder(row, detail);
  if (order) {
    await context.memory.set<RememberedSale>(key, { stamp, order });
  }

  return { order, issues, changed: true };
}

/**
 * Vinted (vinted.com, USD), read the way Vinted's own pages read it: its JSON API, asked from a Vinted
 * page. Vinted has no seller report, so each sale is read in full — price, items, buyer — from the
 * sale itself, and the first scan reads the seller's whole order history.
 */
export const vintedAdapter: MarketplaceAdapter = {
  platform: "Vinted",
  origins: ["https://*.vinted.com/*"],
  home: origin,
  workerUrl: ordersPage,
  signInUrl: `${origin}/member/login`,
  checkUrl: ordersPage,

  readable: (url: string) => /^https:\/\/([a-z0-9-]+\.)*vinted\.com(\/|$)/i.test(url),

  pageKind(url: string): PageKind {
    let path: string;
    try {
      path = new URL(url).pathname;
    } catch {
      return "other";
    }

    if (/^\/member\/(login|signup)/i.test(path)) {
      return "sign-in";
    }

    return /^\/(my_orders|wallet|inbox)(\/|$)|^\/member\/\d+/i.test(path) ? "seller" : "other";
  },

  async detectSession(context: MarketplaceContext): Promise<Session> {
    try {
      const current = await readJson<{ user?: { id?: number | string | null; login?: string | null } }>(context, "/api/v2/users/current", "account details");
      const login = current.user?.login?.trim();
      if (!login) {
        throw siteChanged("account details");
      }

      const id = current.user?.id;
      return { signedIn: true, username: login, marketplaceUserId: id === null || id === undefined ? null : String(id) };
    } catch (error) {
      if (error instanceof AdapterError && error.kind === "SignedOut") {
        return { signedIn: false, username: null, marketplaceUserId: null };
      }

      throw error;
    }
  },

  async scanSales(context: MarketplaceContext, window: SalesWindow): Promise<SalesScan> {
    const now = context.now();
    const since = window.from?.getTime() ?? null;
    // The first scan, and a history scan, read the whole list. After that a routine scan stops at
    // the first page with nothing new or changed on it: Vinted lists the sales with the latest
    // activity first, so the pages past it hold only sales already read.
    const everything = since !== null || window.cursor === null;
    const orders: IngestOrder[] = [];
    const cancelledOrderIds: string[] = [];
    const issues: RunIssue[] = [];
    let newest = Date.parse(window.cursor ?? "");
    let oldest = now.getTime();

    for (let page = 1; ; page++) {
      const list = await readJson<VintedOrderList>(context, `/api/v2/my_orders?type=sold&status=all&per_page=${ordersPerPage}&page=${page}`, "orders");
      if (!Array.isArray(list.my_orders)) {
        throw siteChanged("orders");
      }

      let changes = 0;
      for (const row of list.my_orders) {
        const at = Date.parse(row.date ?? "");
        if (row.transaction_id === null || row.transaction_id === undefined) {
          issues.push({ message: "A sale in Vinted's order list has no id.", externalReference: null, rejected: true });
          continue;
        }

        if (since !== null && Number.isFinite(at) && at < since) {
          continue;
        }

        if (Number.isFinite(at)) {
          newest = Number.isFinite(newest) ? Math.max(newest, at) : at;
          oldest = Math.min(oldest, at);
        }

        const sale = await readSale(context, row);
        if (sale.changed) {
          changes++;
          if (saleState(row) === "cancelled") {
            cancelledOrderIds.push(String(row.transaction_id));
          }
        }

        if (sale.order) {
          orders.push(sale.order);
        }

        issues.push(...sale.issues);
      }

      const pages = list.pagination?.total_pages;
      const last = typeof pages === "number" ? page >= pages : list.my_orders.length < ordersPerPage;
      if (last || (!everything && changes === 0)) {
        break;
      }

      if (page >= maxPages) {
        issues.push({ message: `Vinted lists more sales than one scan reads (${maxPages * ordersPerPage}); the oldest were not read.`, externalReference: null, rejected: false });
        break;
      }
    }

    return {
      orders,
      cancelledOrderIds,
      issues,
      cursor: Number.isFinite(newest) ? new Date(newest).toISOString() : window.cursor,
      coversFrom: new Date(since ?? oldest).toISOString(),
      coversTo: now.toISOString(),
    };
  },

  async snapshotListings(context: MarketplaceContext, session: Session): Promise<ListingsSnapshot> {
    if (!session.marketplaceUserId) {
      throw siteChanged("account details");
    }

    // Keyed by item id, so an item that moves between pages while they are read is counted once.
    const listings = new Map<string, IngestListing>();
    const issues: RunIssue[] = [];
    let total: number | null = null;
    for (let page = 1; page <= maxPages; page++) {
      const wardrobe = await readJson<VintedWardrobe>(
        context,
        `/api/v2/wardrobe/${encodeURIComponent(session.marketplaceUserId)}/items?page=${page}&per_page=${itemsPerPage}&order=newest_first`,
        "listings");
      if (!Array.isArray(wardrobe.items)) {
        throw siteChanged("listings");
      }

      const before = listings.size;
      for (const item of wardrobe.items) {
        const listing = toIngestListing(item);
        if (listing) {
          listings.set(listing.externalListingId!, listing);
        } else {
          issues.push({ message: "A listing's id or price could not be read.", externalReference: item.id === undefined ? null : String(item.id), rejected: true });
        }
      }

      total = wardrobe.pagination?.total_entries ?? total;
      const pages = wardrobe.pagination?.total_pages;
      const last = typeof pages === "number" ? page >= pages : wardrobe.items.length < itemsPerPage;
      if (last || listings.size === before) {
        break;
      }
    }

    // Complete only when every item Vinted counts was read — only then is a listing no longer in it gone.
    const complete = total !== null && listings.size === total && issues.every((issue) => !issue.rejected);
    return { listings: [...listings.values()], issues, complete };
  },
};
