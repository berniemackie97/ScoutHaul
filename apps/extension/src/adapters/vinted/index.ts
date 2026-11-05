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
import { saleStamp, saleState, toIngestOrder, type VintedOrderList, type VintedOrderRow, type VintedTransaction } from "./orders";
import { assertReadable, lookOn, sessionOnPage, signedInPage, signedOutPage, siteChanged } from "./pages";

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
  recovered: boolean;
}

const reading = (): Reading => ({ renewedAt: null, recovered: false });

const signedOut = () => new AdapterError("SignedOut", "The browser is signed out of Vinted.");

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

/**
 * Makes sure the next request goes with a live sign-in, because a request without one is exactly what Vinted's bot
 * protection blocks. A token about to lapse is renewed the way Vinted renews it — a page loads — and watched for in the
 * cookie jar; one that does not come back means the sign-in has lapsed, and nothing is asked.
 */
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
  assertReadable(page);
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

/**
 * Asks Vinted's API for JSON, from Vinted's own page, only ever with a live sign-in. A 401 is the end of it: the
 * question is not asked again. The one exception is a browser that cannot see the token — there, Vinted's page may
 * still have been renewing it, so once per check or scan, a page loads, and the question is asked one more time.
 */
async function readJson<T>(context: MarketplaceContext, path: string, what: string, read: Reading): Promise<T> {
  await ensureToken(context, read);
  let response = await context.request(`${origin}${path}`);
  if (response.status === 401 && !read.recovered && (await accessToken(context)) === "unknown") {
    read.recovered = true;
    const page = await context.page(ordersPage);
    assertReadable(page);
    if (signedOutPage(page)) {
      throw signedOut();
    }

    await context.sleep?.(3000);
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
async function readSale(context: MarketplaceContext, row: VintedOrderRow, read: Reading): Promise<{ order: IngestOrder | null; issues: RunIssue[]; changed: boolean }> {
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
    detail = await readJson<VintedTransaction>(context, `/api/v2/transactions/${encodeURIComponent(String(row.transaction_id))}`, "sale details", read);
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

    if (/^\/member\/(login|signup|verif|two_factor|password|reset_password)/i.test(path)) {
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
    assertReadable(page);
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

    const current = await readJson<{ user?: { id?: number | string | null; login?: string | null } }>(context, "/api/v2/users/current", "account details", reading());
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
    const read = reading();

    for (let page = 1; ; page++) {
      const list = await readJson<VintedOrderList>(context, `/api/v2/my_orders?type=sold&status=all&per_page=${ordersPerPage}&page=${page}`, "orders", read);
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

        const sale = await readSale(context, row, read);
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
    const read = reading();
    for (let page = 1; page <= maxPages; page++) {
      const wardrobe = await readJson<VintedWardrobe>(
        context,
        `/api/v2/wardrobe/${encodeURIComponent(session.marketplaceUserId)}/items?page=${page}&per_page=${itemsPerPage}&order=newest_first`,
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
