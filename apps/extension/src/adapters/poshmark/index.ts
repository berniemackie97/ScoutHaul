import type { IngestListing, IngestOrder, RunIssue } from "../../agent/types";
import { dig } from "../embedded-json";
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
import { readClosetPage } from "./listings";
import { assertReadable, siteChanged } from "./pages";
import { bookedAt, bundleSize, listingIds, readSalesPage, saleId, saleStamp, saleState, toIngestOrder } from "./sales";
import { readState, stateGlobal } from "./state";

const origin = "https://poshmark.com";

const salesPage = `${origin}/seller-hub/sales`;

/** A scan never reads more closet pages than this; past it, it stops and says the snapshot is partial. */
export const maxPages = 40;
export const maxSalesPages = 100;

/** The listings a bundle held, read once from the sale's own page and remembered. Null when the page does not say. */
async function bundleListings(context: MarketplaceContext, id: string, size: number): Promise<string[] | null> {
  const key = `bundle:${id}`;
  const known = await context.memory.get<string[]>(key);
  if (known?.length === size) {
    return known;
  }

  try {
    const response = await context.page(`${origin}/order/sales/${encodeURIComponent(id)}`, { global: stateGlobal });
    assertReadable(response);
    const ids = listingIds(JSON.stringify(readState(response, "sale")));
    if (ids.length !== size) {
      return null;
    }

    await context.memory.set(key, ids);
    return ids;
  } catch (error) {
    // One bundle's page not saying is that bundle's problem; the site going wrong stops the scan.
    if (error instanceof AdapterError && (error.kind === "Error" || error.kind === "SiteChanged")) {
      return null;
    }

    throw error;
  }
}

interface SalesCursor {
  newest: string | null;
  resume: string | null;
}

function readCursor(cursor: string | null): SalesCursor | null {
  if (!cursor) {
    return null;
  }

  try {
    const parsed = JSON.parse(cursor) as Partial<SalesCursor>;
    return { newest: typeof parsed.newest === "string" ? parsed.newest : null, resume: typeof parsed.resume === "string" ? parsed.resume : null };
  } catch {
    return { newest: cursor, resume: null };
  }
}

/** Who every Poshmark page's state says is signed in, or null when it carries no sign-in at all. */
function sessionIn(state: Record<string, unknown>): Session | null {
  const auth = dig(state, "auth");
  if (auth === undefined) {
    return null;
  }

  if (dig(auth, "isUserLoggedIn") !== true) {
    return { signedIn: false, username: null, marketplaceUserId: null };
  }

  // Poshmark abbreviates the signed-in user's own details; `dh` is the handle their closet lives at.
  const handle = dig(state, "userCookies", "userInfo", "dh");
  const identity = dig(auth, "identity");
  return {
    signedIn: true,
    username: typeof handle === "string" && handle.length > 0 ? handle : null,
    marketplaceUserId: typeof identity === "string" ? identity : null,
  };
}

/**
 * Poshmark, read the way its own pages are served: a closet page carries its listings whole, and Seller Hub's sales
 * page carries every sale with its money, buyer and ship-to, in the state Poshmark writes into the HTML. No API is
 * called.
 */
export const poshmarkAdapter: MarketplaceAdapter = {
  platform: "Poshmark",
  origins: ["https://*.poshmark.com/*"],
  home: origin,
  workerUrl: `${origin}/robots.txt`,
  signInUrl: `${origin}/login`,
  checkUrl: origin,

  readable: (url: string) => /^https:\/\/([a-z0-9-]+\.)*poshmark\.com(\/|$)/i.test(url),

  pageKind(url: string): PageKind {
    if (/^https:\/\/poshmark\.com\/(login|signup)/i.test(url)) {
      return "sign-in";
    }

    if (/\/captcha|challenge/i.test(url)) {
      return "challenge";
    }

    return /^https:\/\/poshmark\.com\/(closet|feed|order|my|seller-hub)/i.test(url) ? "seller" : "other";
  },

  async detectSession(context: MarketplaceContext): Promise<Session> {
    const response = await context.page(origin, { global: stateGlobal });
    assertReadable(response);
    return sessionIn(readState(response, "home")) ?? { signedIn: false, username: null, marketplaceUserId: null };
  },

  sessionGlobal: stateGlobal,

  sessionFrom(page: PageResponse): Session | null {
    try {
      return sessionIn(readState(page, "page"));
    } catch {
      return null;
    }
  },

  async scanSales(context: MarketplaceContext, window: SalesWindow): Promise<SalesScan> {
    const now = context.now();
    const since = window.from?.getTime() ?? null;
    const previous = readCursor(window.cursor);
    // The first scan and a history scan read every page. A routine scan reads on until a page holds nothing new and
    // no sale still under way, so a sale completing weeks later still has its payout recorded.
    const everything = since !== null || previous === null;
    const orders: IngestOrder[] = [];
    const cancelledOrderIds: string[] = [];
    const issues: RunIssue[] = [];
    const seen = new Set<string>();
    let newest = Date.parse(previous?.newest ?? "");
    let oldest = now.getTime();
    let budget = maxSalesPages;

    /** Reads pages from a cursor; answers where it had to stop for the page budget, or null when it finished. */
    const readFrom = async (start: string | null, stopWhenSettled: boolean): Promise<string | null> => {
      let cursor = start;
      while (budget > 0) {
        budget--;
        const response = await context.page(cursor === null ? salesPage : `${salesPage}?max_id=${encodeURIComponent(cursor)}`, { global: stateGlobal });
        assertReadable(response);
        const read = readSalesPage(response);
        if (!read.found) {
          throw siteChanged("sales");
        }

        let unsettled = 0;
        for (const sale of read.sales) {
          const id = saleId(sale);
          if (id === null) {
            issues.push({ message: "A sale in Poshmark's sales list has no id.", externalReference: null, rejected: true });
            continue;
          }

          if (seen.has(id)) {
            continue;
          }

          seen.add(id);
          const at = bookedAt(sale);
          if (since !== null && Number.isFinite(at) && at < since) {
            continue;
          }

          if (Number.isFinite(at)) {
            newest = Number.isFinite(newest) ? Math.max(newest, at) : at;
            oldest = Math.min(oldest, at);
          }

          const key = `sale:${id}`;
          const stamp = saleStamp(sale);
          const state = saleState(sale);
          if (state === "open" || (await context.memory.get<string>(key)) !== stamp) {
            unsettled++;
          }

          if (state === "cancelled") {
            cancelledOrderIds.push(id);
            await context.memory.set(key, stamp);
            continue;
          }

          const size = bundleSize(sale);
          const sold = toIngestOrder(sale, size !== null && size > 1 ? await bundleListings(context, id, size) : null);
          issues.push(...sold.issues);
          if (sold.order) {
            orders.push(sold.order);
            await context.memory.set(key, stamp);
          }
        }

        if (read.nextMaxId === null || read.nextMaxId === cursor || read.sales.length === 0 || (stopWhenSettled && unsettled === 0)) {
          return null;
        }

        cursor = read.nextMaxId;
      }

      return cursor;
    };

    // Where a scan ran out of pages is carried in the loop's own cursor, so the next one picks the history up there
    // after reading what is new, instead of stopping at the settled pages above it for good.
    let resume = await readFrom(null, !everything);
    if (resume === null && previous?.resume) {
      resume = await readFrom(previous.resume, false);
    }

    if (resume !== null) {
      issues.push({ message: "Poshmark lists more sales than one scan reads; the next scan carries on from where this one stopped.", externalReference: null, rejected: false });
    }

    return {
      orders,
      cancelledOrderIds,
      issues,
      cursor: JSON.stringify({ newest: Number.isFinite(newest) ? new Date(newest).toISOString() : null, resume }),
      coversFrom: new Date(since ?? oldest).toISOString(),
      coversTo: now.toISOString(),
    };
  },

  async snapshotListings(context: MarketplaceContext, session: Session): Promise<ListingsSnapshot> {
    const closet = session.username;
    if (!closet) {
      throw new AdapterError("SignedOut", "The browser is signed out of Poshmark.");
    }

    // Keyed by listing id: a page that repeats a listing can never count it twice.
    const listings = new Map<string, IngestListing>();
    const issues: RunIssue[] = [];
    let complete = true;

    // Sold listings are a separate view of the same closet, and a sale that ends a listing has to be seen.
    for (const availability of [null, "sold_out"]) {
      // Poshmark pages by a cursor of its own: the first page is asked for without one, and each answer carries the
      // next. It is echoed back untouched, never built here. A closet that says there is more without saying where
      // falls back to the page number its pages also answer to.
      let cursor: string | null = null;
      for (let page = 1; ; page++) {
        if (page > maxPages) {
          issues.push({ message: "This closet has more listings than one scan reads; the rest come next time.", rejected: false });
          complete = false;
          break;
        }

        const query = [
          cursor === null ? null : `max_id=${encodeURIComponent(cursor)}`,
          availability === null ? null : `availability=${availability}`,
        ].filter((part) => part !== null);
        const address = `${origin}/closet/${encodeURIComponent(closet)}${query.length === 0 ? "" : `?${query.join("&")}`}`;
        const response = await context.page(address, { global: stateGlobal });
        assertReadable(response);
        const read = readClosetPage(response);
        if (!read.found) {
          // No list at all, which is not the same as a closet with nothing in it.
          throw siteChanged("closet");
        }

        const before = listings.size;
        for (const listing of read.listings) {
          listings.set(listing.externalListingId!, listing);
        }

        if (!read.more || listings.size === before) {
          break;
        }

        cursor = read.nextMaxId ?? String(page + 1);
      }
    }

    return { listings: [...listings.values()], issues, complete };
  },
};
