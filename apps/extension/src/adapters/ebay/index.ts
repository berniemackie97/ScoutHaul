import { waitsForAPerson } from "../../agent/failures";
import type { IngestListing, IngestOrder, RunIssue } from "../../agent/types";
import { dig, spans } from "../embedded-json";
import { AdapterError, type ListingsSnapshot, type MarketplaceAdapter, type MarketplaceContext, type PageKind, type SalesScan, type SalesWindow, type Session } from "../types";
import { readLedgerPage, type LedgerOrder } from "./ledger";
import { readGallery } from "./gallery";
import { readListingsPage } from "./listings";
import { descriptionUrl, readDescription, readSpecifics, type ListingSpecifics } from "./specifics";
import { readOrdersPage, toIngestOrder, type EbayOrderRow } from "./orders";
import { assertReadable, siteChanged, unreadable } from "./pages";
import { isoDay, saleTime } from "./text";

const origin = "https://www.ebay.com";
const day = 24 * 60 * 60 * 1000;

/** eBay keeps the order list and the seller's ledger for 90 days. */
export const historyDays = 90;

/** A routine sales scan re-reads two weeks, the time in which labels, ad fees and fees settle. */
export const routineDays = 14;

const pageSize = 200;

/**
 * How many item pages one listings scan reads for their pictures. Each costs a request, paced by the governor, so
 * a first scan of a large shop spreads its galleries over several scans rather than making one scan take an hour.
 */
const galleriesPerScan = 25;

/** An item page is read again only when the listing's own picture changed, or after this long. */
const galleryStaleDays = 30;

/**
 * How many descriptions one scan fetches. eBay keeps them in a separate document, so each is a request of its own —
 * fewer than the pages themselves, because a description matters less than a photo and changes less often.
 */
const descriptionsPerScan = 10;

/** The first day a sales scan reads from. */
export function salesFrom(window: SalesWindow, now: Date): Date {
  const earliest = new Date(now.getTime() - historyDays * day);
  if (window.from) {
    return window.from < earliest ? earliest : window.from;
  }

  // Back to where the last successful scan got to, if that is further than the routine window —
  // so days with every browser off are read on the next scan, with no gap.
  let from = new Date(now.getTime() - routineDays * day);
  const cursor = window.cursor ? Date.parse(window.cursor) : Number.NaN;
  if (!Number.isNaN(cursor) && cursor - day < from.getTime()) {
    from = new Date(cursor - day);
  }

  return from < earliest ? earliest : from;
}

/**
 * eBay, read the way Seller Hub reads it: the signed-in user from the site's profile endpoint, sales
 * from the order list with their money after the sale from the seller's transaction ledger, and
 * listings from Manage active listings — each page's own embedded data, not its rendered table.
 */
export const ebayAdapter: MarketplaceAdapter = {
  platform: "eBay",
  origins: ["https://*.ebay.com/*"],
  home: origin,
  workerUrl: `${origin}/robots.txt`,
  signInUrl: "https://signin.ebay.com/",
  checkUrl: `${origin}/sh/ord/`,

  readable: (url: string) => /^https:\/\/([a-z0-9-]+\.)*ebay\.com(\/|$)/i.test(url),

  pageKind(url: string): PageKind {
    if (/^https:\/\/signin\.ebay\.com(\/|$)/i.test(url)) {
      return "sign-in";
    }

    if (/^https:\/\/[^/]+\/splashui\/(challenge|captcha)/i.test(url)) {
      return "challenge";
    }

    return /^https:\/\/www\.ebay\.com\/(sh|mes)\//i.test(url) ? "seller" : "other";
  },

  async detectSession(context: MarketplaceContext): Promise<Session> {
    const response = await context.request(`${origin}/gh/user_profile?modules=USER_PROFILE&behavior=GET_PROFILE&moduleType=USER_PROFILE&v=2`);
    if (/signin\.ebay\./i.test(response.url) || response.status === 401) {
      return { signedIn: false, username: null, marketplaceUserId: null };
    }

    assertReadable(response);
    let profile: unknown;
    try {
      profile = dig(JSON.parse(response.body), "content", "response", "modules", "USER_PROFILE");
    } catch {
      throw siteChanged("profile");
    }

    const username = spans(dig(profile, "userLoginName"));
    return { signedIn: username !== null, username, marketplaceUserId: null };
  },

  async scanSales(context: MarketplaceContext, window: SalesWindow): Promise<SalesScan> {
    const now = context.now();
    const from = salesFrom(window, now);

    // Newest first, until the list reaches sales older than the window.
    const rows: EbayOrderRow[] = [];
    for (let offset = 0; ; offset += pageSize) {
      const response = await context.page(
        `${origin}/sh/ord/?offset=${offset}&limit=${pageSize}&filter=status:ALL_ORDERS,timerange:LAST90D&sort=-creationdate`);
      assertReadable(response);
      const page = readOrdersPage(response.body);
      if (!page) {
        throw unreadable(response, "orders");
      }

      rows.push(...page.rows);
      const oldest = page.rows.at(-1);
      const oldestAt = oldest ? saleTime(oldest.created, now) : null;
      const more = page.rows.length === pageSize && (page.total === null || offset + pageSize < page.total);
      if (!more || (oldestAt !== null && oldestAt < from)) {
        break;
      }
    }

    // The ledger for the same days, a day either side so nothing on the boundary is missed.
    const ledger = new Map<string, LedgerOrder>();
    const range = encodeURIComponent(`creationDate:(${isoDay(new Date(from.getTime() - day))}..${isoDay(new Date(now.getTime() + day))})`);
    for (let offset = 0; ; offset += pageSize) {
      const response = await context.page(`${origin}/mes/transactionlist?sh=true&limit=${pageSize}&offset=${offset}&filter=${range}`);
      assertReadable(response);
      const page = readLedgerPage(response.body);
      if (!page) {
        throw unreadable(response, "transactions");
      }

      for (const [id, entry] of page.byOrder) {
        const known = ledger.get(id);
        ledger.set(id, known ? merge(known, entry) : entry);
      }

      if (page.count < pageSize) {
        break;
      }
    }

    const orders: IngestOrder[] = [];
    const issues: RunIssue[] = [];
    let newest = Number.NEGATIVE_INFINITY;
    for (const row of rows) {
      const at = saleTime(row.created, now);
      if (at !== null && at < from) {
        continue;
      }

      const { order, issues: rowIssues } = toIngestOrder(row, ledger.get(row.orderId), now);
      issues.push(...rowIssues);
      if (order) {
        orders.push(order);
        newest = Math.max(newest, Date.parse(order.orderedAt!));
      }

      const refunds = ledger.get(row.orderId)?.refunds ?? [];
      if (refunds.length > 0) {
        issues.push({
          message: "eBay's ledger shows a refund on this order. Refunds are applied from the order report import for now.",
          externalReference: row.orderId,
          rejected: false,
        });
      }
    }

    return {
      orders,
      // Not read from eBay's scan yet; its refunds reach the books through the order report import.
      cancelledOrderIds: [],
      issues,
      cursor: Number.isFinite(newest) ? new Date(newest).toISOString() : window.cursor,
      coversFrom: from.toISOString(),
      coversTo: now.toISOString(),
    };
  },

  async snapshotListings(context: MarketplaceContext): Promise<ListingsSnapshot> {
    // Keyed by listing id: a page that repeats listings — eBay ignoring the page size, or the list
    // shifting while it is read — can never count one twice.
    const listings = new Map<string, IngestListing>();
    const issues: RunIssue[] = [];
    let total: number | null = null;
    for (let offset = 0; ; offset += pageSize) {
      const response = await context.page(`${origin}/sh/lst/active?offset=${offset}&limit=${pageSize}`);
      assertReadable(response);
      const page = readListingsPage(response.body);
      if (!page) {
        throw unreadable(response, "active listings");
      }

      const before = listings.size;
      for (const listing of page.listings) {
        listings.set(listing.externalListingId!, listing);
      }

      issues.push(...page.issues);
      total = page.total ?? total;
      const read = page.listings.length + page.issues.length;
      if (listings.size === before || read < pageSize || (total !== null && listings.size >= total)) {
        break;
      }
    }

    await readGalleries(context, [...listings.values()], issues);
    const complete = issues.every((issue) => !issue.rejected) && (total === null || listings.size === total);
    return { listings: [...listings.values()], issues, complete };
  },
};

/** What this browser has already read from one listing's item page. */
interface ItemPageMemory {
  at: number;
  primary: string | null;
  photos: string[];
  /** Absent in anything remembered before specifics were read, which is what makes those entries stale. */
  specifics?: ListingSpecifics;
  description?: string | null;
}

/**
 * Fills in the listings' galleries from their item pages, newest-changed first and only up to this scan's budget.
 *
 * A listing whose picture has not changed since its gallery was read is skipped entirely, so a shop that is not
 * changing costs nothing. A listing not reached this time keeps `photoUrls` undefined, which tells the server to
 * leave that listing's photos exactly as they are rather than replacing them with nothing.
 */
async function readGalleries(context: MarketplaceContext, listings: IngestListing[], issues: RunIssue[]): Promise<void> {
  const stale = context.now().getTime() - galleryStaleDays * day;
  let budget = galleriesPerScan;
  let descriptions = descriptionsPerScan;

  for (const listing of listings) {
    if (budget === 0) {
      return;
    }

    const id = listing.externalListingId;
    if (!id) {
      continue;
    }

    const known = await context.memory.get<ItemPageMemory>(`gallery:${id}`);
    if (known?.specifics && known.at > stale && known.primary === listing.primaryImageUrl) {
      // Nothing on the page has changed, so what was read from it last time still stands.
      listing.photoUrls = known.photos;
      apply(listing, known.specifics, known.description ?? null);
      continue;
    }

    budget--;
    let photos: string[];
    let body = "";
    try {
      const response = await context.request(`${origin}/itm/${id}`);
      assertReadable(response);
      body = response.body;
      photos = readGallery(body);
    } catch (error) {
      // A marketplace refusing or asking for a person is the whole scan's problem. Anything else is this one
      // listing's, and the listings themselves have already been read — so say so and stop asking for pictures.
      if (error instanceof AdapterError && waitsForAPerson(error.kind)) {
        throw error;
      }

      issues.push({ message: "This listing's pictures could not be read from its page.", externalReference: id, rejected: false });
      return;
    }

    // The same page says what the thing is, so it is read while it is in hand.
    const specifics = readSpecifics(body);
    const description = descriptions > 0 ? await readDescriptionFor(context, body) : null;
    if (description !== null) {
      descriptions--;
    }

    apply(listing, specifics, description);

    if (photos.length === 0) {
      issues.push({ message: "This listing's pictures could not be read from its page.", externalReference: id, rejected: false });
      continue;
    }

    listing.photoUrls = photos;
    await context.memory.set<ItemPageMemory>(`gallery:${id}`, {
      at: context.now().getTime(),
      primary: listing.primaryImageUrl ?? null,
      photos,
      specifics,
      description,
    });
  }
}

function apply(listing: IngestListing, specifics: ListingSpecifics, description: string | null): void {
  listing.brand = specifics.brand;
  listing.color = specifics.color;
  listing.size = specifics.size;
  // Undefined rather than null when there is none: a source that did not read a description must not erase one.
  listing.description = description ?? undefined;
}

/** The listing's description, from the document its page links to. Null when the page does not link to one. */
async function readDescriptionFor(context: MarketplaceContext, page: string): Promise<string | null> {
  const address = descriptionUrl(page);
  if (address === null) {
    return null;
  }

  try {
    const response = await context.request(address);
    return response.status === 200 ? readDescription(response.body) : null;
  } catch {
    // A description is the least of what this scan is for; losing one costs the listing nothing.
    return null;
  }
}

function merge(left: LedgerOrder, right: LedgerOrder): LedgerOrder {
  return {
    gross: left.gross ?? right.gross,
    net: left.net ?? right.net,
    labels: [...left.labels, ...right.labels],
    adFees: [...left.adFees, ...right.adFees],
    refunds: [...left.refunds, ...right.refunds],
    carrier: left.carrier ?? right.carrier,
  };
}
