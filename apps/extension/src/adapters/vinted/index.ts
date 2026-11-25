import type { IngestListing, IngestOrder, RunIssue } from "../../agent/types";
import {
  AdapterError,
  type ListingsSnapshot,
  type MarketplaceAdapter,
  type MarketplaceContext,
  type PageKind,
  type PageResponse,
  type SalesScan,
  type SalesWindow,
  type Session,
} from "../types";
import { toIngestListing, type VintedWardrobe } from "./listings";
import { saleStamp, saleState, toIngestOrder, type VintedOrderList, type VintedOrderRow, type VintedEscrowReceipt } from "./orders";
import { assertReadable, lookOn, sessionOnPage, signedInPage, signInPath, signedOut, signedOutPage, siteChanged, type MissingResource } from "./pages";

const origin = "https://www.vinted.com";
const apiOrigin = "https://api.www.vinted.com";
const escrowOrders = `${apiOrigin}/escrow-orders/api/v2`;

/** The seller's sold orders — an ordinary Vinted page, and loading one renews a lapsed sign-in. */
const ordersPage = `${origin}/my_orders?order_type=sold`;
const ordersPageName = "the sold-orders page";

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

/** The cookie Vinted's pages keep the sign-in's short-lived access token in. */
export const tokenCookie = "access_token_web";

/** Memory of having seen that cookie in this browser: only then does its absence mean anything. */
const tokenSeenKey = "token-cookie-seen";

/** A token with less than this left is renewed before it is used, so no request outlives it. */
const tokenMarginMs = 60_000;

/** How long a page load is given to renew the token, looked at every half second, before the sign-in counts as lapsed. */
const renewWaitMs = 10_000;

/** The least time between two page loads made only to renew the token. */
const renewEveryMs = 10 * 60_000;

type TokenState = "valid" | "expired" | "missing" | "unknown";

/** One check or scan's reading of Vinted: what it has already done to keep the sign-in fresh. */
interface Reading {
  renewedAt: number | null;
}

const reading = (): Reading => ({ renewedAt: null });

/** When a token (a JWT) stops working, from the token itself; null when it is not one this can read. */
export function tokenExpiry(token: string): number | null {
  try {
    const payload = token.split(".")[1];
    if (!payload) {
      return null;
    }

    const json = JSON.parse(atob(payload.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(payload.length / 4) * 4, "="))) as { exp?: unknown };
    return typeof json.exp === "number" ? json.exp * 1000 : null;
  } catch {
    return null;
  }
}

/**
 * The sign-in's access token, from the cookie jar — no request. "unknown" when this browser cannot tell: cookies
 * cannot be read, the cookie has never been seen here, or it is not a token this can read.
 */
async function accessToken(context: MarketplaceContext): Promise<TokenState> {
  const value = await context.cookie?.(tokenCookie);
  if (value === undefined) {
    return "unknown";
  }

  const seen = await context.memory.get<number>(tokenSeenKey);
  if (value === null) {
    return seen ? "missing" : "unknown";
  }

  const now = context.now().getTime();
  if (!seen || now - seen > 24 * 60 * 60_000) {
    await context.memory.set<number>(tokenSeenKey, now);
  }

  const expiry = tokenExpiry(value);
  return expiry === null ? "unknown" : expiry - tokenMarginMs > now ? "valid" : "expired";
}

/** The token as it stands once it is live, or once `ms` have gone by looking every half second — the cookie jar only. */
async function tokenWithin(context: MarketplaceContext, ms: number): Promise<TokenState> {
  let token = await accessToken(context);
  for (let waited = 0; waited < ms && (token === "expired" || token === "missing") && context.sleep; waited += 500) {
    await context.sleep(500);
    token = await accessToken(context);
  }

  return token;
}

/** Gives Vinted's page time to refresh its session before issuing a read with an expired or missing token. */
async function ensureToken(context: MarketplaceContext, read: Reading): Promise<void> {
  // A page that has only just loaded may be renewing it this moment.
  const token = await tokenWithin(context, 2000);
  if (token === "valid" || token === "unknown") {
    return;
  }

  const now = context.now().getTime();
  if (read.renewedAt !== null && now - read.renewedAt < renewEveryMs) {
    throw signedOut();
  }

  read.renewedAt = now;
  const page = await context.page(ordersPage);
  assertReadable(page, ordersPageName);
  if (signedOutPage(page)) {
    throw signedOut();
  }

  const renewed = await tokenWithin(context, renewWaitMs);
  if (renewed === "valid") {
    return;
  }

  if (renewed === "missing" && signedInPage(page)) {
    // Signed in, by the page, with no token where one was before: Vinted keeps it elsewhere now. Stop relying on it.
    await context.memory.set<number>(tokenSeenKey, 0);
    return;
  }

  throw new AdapterError("SignedOut", "Vinted's sign-in has lapsed. Sign in to Vinted again in this browser.");
}

async function readJson<T>(context: MarketplaceContext, url: string, what: string, read: Reading, missing?: MissingResource): Promise<T> {
  await ensureToken(context, read);
  const response = await context.request(url);
  assertReadable(response, what, missing);
  if (signInPath(new URL(response.url).pathname)) {
    throw signedOut();
  }

  let data: unknown;
  try {
    data = JSON.parse(response.body);
  } catch {
    throw siteChanged(what);
  }

  if (!data || typeof data !== "object" || Array.isArray(data)) {
    throw siteChanged(what);
  }

  return data as T;
}

/**
 * One sale from the order list: from memory when the list shows it unchanged since it was last read,
 * otherwise read in full. A cancelled sale needs no reading — only telling. A sale whose details
 * cannot be read is reported, and left unremembered so the next scan tries it again.
 */
async function readSale(context: MarketplaceContext, row: VintedOrderRow, read: Reading, session: Session): Promise<{ order: IngestOrder | null; issues: RunIssue[]; changed: boolean }> {
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

  let detail: VintedEscrowReceipt;
  try {
    const id = encodeURIComponent(String(row.escrow_order_id ?? row.transaction_id));
    detail = await readJson<VintedEscrowReceipt>(context, `${escrowOrders}/escrow-orders/${id}`, "sale details", read, "Error");
  } catch (error) {
    // Trouble with the whole site stops the scan; trouble with this one sale only skips it.
    if (error instanceof AdapterError && error.kind === "Error") {
      return { order: null, issues: [{ message: `This sale could not be read. ${error.message}`, externalReference: String(row.transaction_id), rejected: true }], changed: true };
    }

    throw error;
  }

  const { order, issues } = toIngestOrder(row, detail, session.marketplaceUserId);
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
  requestOrigins: [apiOrigin],
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

    if (signInPath(path)) {
      return "sign-in";
    }

    return /^\/(my_orders|wallet|inbox)(\/|$)|^\/member\/\d+/i.test(path) ? "seller" : "other";
  },

  /**
   * A page first, the way a person arrives — or the one the operator already has open (`shown`): it says plainly when
   * no one is signed in. Only a page that shows a sign-in is followed by the one question of which account it is, and
   * only with a live token. Vinted's API questioned without a session is exactly the pattern its bot protection
   * blocks, so a signed-out browser is never asked anything at all.
   */
  async detectSession(context: MarketplaceContext, shown?: PageResponse): Promise<Session> {
    const page = shown ?? (await context.page(ordersPage));
    assertReadable(page, shown ? "the open page" : ordersPageName);
    if (signedOutPage(page)) {
      return { signedIn: false, username: null, marketplaceUserId: null };
    }

    if (!signedInPage(page)) {
      // The operator's page may be one that does not say; Vinted's own orders page always has.
      if (shown) {
        throw new AdapterError("Interrupted", "The open Vinted page does not show the sign-in.");
      }

      throw siteChanged("sign-in state");
    }

    const current = await readJson<{ user?: { id?: number | string | null; login?: string | null } }>(context, `${origin}/api/v2/users/current`, "account details", reading());
    const login = current.user?.login?.trim();
    if (!login) {
      throw siteChanged("account details");
    }

    const id = current.user?.id;
    return { signedIn: true, username: login, marketplaceUserId: id === null || id === undefined ? null : String(id) };
  },

  // The operator's own Vinted tab says when it is signed out, so that is read for free rather than asked of Vinted.
  sessionFrom: sessionOnPage,
  look: lookOn,

  async scanSales(context: MarketplaceContext, window: SalesWindow, session: Session): Promise<SalesScan> {
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
    const read = reading();

    for (let page = 1; ; page++) {
      const what = `the sold-orders list (page ${page})`;
      const list = await readJson<VintedOrderList>(context, `${escrowOrders}/current-user/escrow-orders?page=${page}&per_page=${ordersPerPage}&type=sold`, what, read);
      if (!Array.isArray(list.my_orders)) {
        throw siteChanged(what);
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

        const sale = await readSale(context, row, read, session);
        if (sale.changed) {
          changes++;
        }

        // The scan may have read this cancellation before HaulScout received its batch.
        if (saleState(row) === "cancelled") {
          cancelledOrderIds.push(String(row.transaction_id));
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
    const read = reading();
    for (let page = 1; page <= maxPages; page++) {
      const wardrobe = await readJson<VintedWardrobe>(
        context,
        `${origin}/api/v2/wardrobe/${encodeURIComponent(session.marketplaceUserId)}/items?page=${page}&per_page=${itemsPerPage}&order=newest_first`,
        "listings",
        read);
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
