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
import { assertReadable, siteChanged } from "./pages";
import { readProduct } from "./product";
import { cancelled, dayOf, readReceipt, readSoldList, receiptSelector, saleStamp, soldRowSelector, toIngestOrder, type SoldRow } from "./sales";

const origin = "https://www.depop.com";

/** How many listings one scan reads in full. Each is a page of its own, so this is what a scan costs. */
const listingsPerScan = 20;

/** A listing is read again only when it has not been read for this long. */
const staleDays = 7;

const soldPage = `${origin}/sellinghub/sold-items/`;

/** How many receipts one scan reads. Each is a page of its own; the rest are read on the scans after. */
const receiptsPerScan = 15;
const stallsToStop = 2;

/** A shop's own listings, drawn in after the page loads. */
const shopGridSelector = 'ol li a[href^="/products/"]';

/** A receipt whose sale is still open is read again after this long: its payout and parcel change without its row. */
const openReceiptRefreshMs = 6 * 60 * 60 * 1000;

/** A sale as remembered between scans: its receipt, when it was read, and how its row looked then. */
interface RememberedSale {
  stamp: string;
  order: IngestOrder;
  readAt: number;
}

/** Whether a remembered receipt still says all there is: the row unchanged, and the sale either settled or read lately. */
function current(known: RememberedSale, row: SoldRow, now: number): boolean {
  if (known.stamp !== saleStamp(row)) {
    return false;
  }

  const settled = known.order.payoutAmount != null && known.order.fulfillmentStage === "Delivered";
  return settled || now - known.readAt < openReceiptRefreshMs;
}

/** What this browser already read for one listing, so an unchanged shop costs one request. */
interface ListingMemory {
  at: number;
  listing: IngestListing;
}

/**
 * The header's own Likes link is the signed-in seller's, and it is the only handle on a page that is certainly theirs:
 * a page can show any number of other people's, and an order page shows the buyer's.
 */
function sessionOnPage(body: string): Session | null {
  const handle = /href="\/([A-Za-z0-9_.-]{2,40})\/likes\/"/.exec(body);
  return handle ? { signedIn: true, username: handle[1]!, marketplaceUserId: null } : null;
}

/**
 * Depop, read the way a shopper's browser reads it.
 *
 * A shop page names every listing it shows but says almost nothing about them — a link, a picture, a price. The
 * listing's own page publishes the whole of it as structured data: title, description, every photo, brand, size,
 * condition and whether it has sold. So the shop is read for which listings exist, and the listings themselves are
 * read a few at a time, newest first, with what was read kept between scans.
 *
 * A snapshot is never called complete. The shop reveals its tiles as a person scrolls, so a scan cannot prove it saw
 * every listing, and claiming otherwise would end listings that are simply further down the page. A listing that
 * sold is closed by its own page saying so, not by its absence.
 *
 * Sales come from Seller Hub's sold list, which names every sale with its buyer, date, status and total. Each new or
 * changed sale's receipt is then read, a few per scan, for its fees, payout, parcel and the listing it came from.
 */
export const depopAdapter: MarketplaceAdapter = {
  platform: "Depop",
  origins: ["https://*.depop.com/*"],
  home: origin,
  workerUrl: `${origin}/robots.txt`,
  signInUrl: `${origin}/login/`,
  checkUrl: origin,

  readable: (url: string) => /^https:\/\/([a-z0-9-]+\.)*depop\.com(\/|$)/i.test(url),

  pageKind(url: string): PageKind {
    if (/^https:\/\/www\.depop\.com\/(login|signup)/i.test(url)) {
      return "sign-in";
    }

    if (/\/cdn-cgi\/challenge|captcha/i.test(url)) {
      return "challenge";
    }

    return /^https:\/\/www\.depop\.com\/[^/]+\/?$/i.test(url) ? "seller" : "other";
  },

  async detectSession(context: MarketplaceContext): Promise<Session> {
    const response = await context.page(origin);
    assertReadable(response);
    return sessionOnPage(response.body) ?? { signedIn: false, username: null, marketplaceUserId: null };
  },

  // Any Depop page with the header shows who is signed in. One without it cannot say they are signed out.
  sessionFrom: (page: PageResponse) => sessionOnPage(page.body),

  async scanSales(context: MarketplaceContext, window: SalesWindow): Promise<SalesScan> {
    const now = context.now();
    const since = window.from?.getTime() ?? null;
    const list = await context.page(soldPage, { until: soldRowSelector });
    assertReadable(list);
    const rows = readSoldList(list.body);
    if (rows === null) {
      throw siteChanged("sold items");
    }

    const orders: IngestOrder[] = [];
    const cancelledOrderIds: string[] = [];
    const issues: RunIssue[] = [];
    let newest = Date.parse(window.cursor ?? "");
    let oldest = now.getTime();
    let budget = receiptsPerScan;
    let waiting = 0;
    let stalled = 0;

    for (const row of rows) {
      const at = dayOf(row.soldOn)?.getTime() ?? Number.NaN;
      if (since !== null && Number.isFinite(at) && at < since) {
        continue;
      }

      if (Number.isFinite(at)) {
        newest = Number.isFinite(newest) ? Math.max(newest, at) : at;
        oldest = Math.min(oldest, at);
      }

      if (cancelled(row.status)) {
        cancelledOrderIds.push(row.id);
        continue;
      }

      const key = `sale:${row.id}`;
      const known = await context.memory.get<RememberedSale>(key);
      if (known && current(known, row, now.getTime())) {
        orders.push(known.order);
        continue;
      }

      if (budget === 0) {
        // A receipt due a refresh still sends what it last said; one never read waits its turn.
        if (known) {
          orders.push(known.order);
        } else {
          waiting++;
        }

        continue;
      }

      budget--;
      const sold = await readSale(context, row);
      // One slow receipt waits for the next scan; several in a row is Depop, or the connection, and not this sale.
      stalled = sold.stalled ? stalled + 1 : 0;
      if (stalled >= stallsToStop) {
        throw new AdapterError("Network", "Depop's receipts stopped loading.");
      }

      issues.push(...sold.issues);
      if (sold.order) {
        orders.push(sold.order);
        await context.memory.set<RememberedSale>(key, { stamp: saleStamp(row), order: sold.order, readAt: now.getTime() });
      } else if (known) {
        orders.push(known.order);
      }
    }

    if (waiting > 0) {
      issues.push({ message: `${waiting} more Depop sale${waiting === 1 ? " is" : "s are"} read on the next scan.`, externalReference: null, rejected: false });
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
    const shop = session.username;
    if (!shop) {
      throw new AdapterError("SignedOut", "The browser is signed out of Depop.");
    }

    const response = await context.page(`${origin}/${encodeURIComponent(shop)}/`, { until: shopGridSelector });
    assertReadable(response);
    const slugs = readShop(response.body);
    if (slugs.length === 0) {
      throw siteChanged("shop");
    }

    const issues: RunIssue[] = [];
    const listings: IngestListing[] = [];
    const stale = context.now().getTime() - staleDays * 24 * 60 * 60 * 1000;
    let budget = listingsPerScan;

    for (const slug of slugs) {
      const known = await context.memory.get<ListingMemory>(`listing:${slug}`);
      if (known && known.at > stale) {
        listings.push(known.listing);
        continue;
      }

      if (budget === 0) {
        // The rest keep whatever the server already knows: they are simply not in this snapshot.
        continue;
      }

      budget--;
      try {
        const page = await context.page(`${origin}/products/${slug}/`);
        assertReadable(page);
        const listing = readProduct(slug, page.body);
        listings.push(listing);
        await context.memory.set<ListingMemory>(`listing:${slug}`, { at: context.now().getTime(), listing });
      } catch (error) {
        if (error instanceof AdapterError && (error.kind === "Challenge" || error.kind === "RateLimited" || error.kind === "SignedOut")) {
          throw error;
        }

        issues.push({ message: "This listing's page could not be read.", externalReference: slug, rejected: false });
      }
    }

    // Never complete: the shop reveals its listings as a person scrolls, so nothing here proves the rest are gone.
    return { listings, issues, complete: false };
  },
};

/** Every listing a shop page names, in the order it shows them. */
export function readShop(html: string): string[] {
  const slugs = new Set<string>();
  for (const match of html.matchAll(/href="\/products\/([A-Za-z0-9][A-Za-z0-9._-]{0,140})\/?"/g)) {
    // The header's Sell button is a link under /products/ too.
    if (match[1] !== "create") {
      slugs.add(match[1]!);
    }
  }

  return [...slugs];
}

/** One sale's receipt, read in full. Trouble with this one sale only skips it; trouble with the site stops the scan. */
async function readSale(context: MarketplaceContext, row: SoldRow): Promise<{ order: IngestOrder | null; issues: RunIssue[]; stalled?: boolean }> {
  const skip = (message: string) => ({ order: null, issues: [{ message, externalReference: row.id, rejected: true }] });
  try {
    const page = await context.page(`${origin}/sellinghub/sold-items/${encodeURIComponent(row.id)}/`, { until: receiptSelector });
    assertReadable(page);
    const receipt = readReceipt(page.body);
    return receipt ? toIngestOrder(row, receipt) : skip("This sale's receipt could not be read.");
  } catch (error) {
    if (error instanceof AdapterError && (error.kind === "Error" || error.kind === "SiteChanged")) {
      return skip(`This sale's receipt could not be read. ${error.message}`);
    }

    if (error instanceof AdapterError && error.kind === "Network") {
      return { order: null, issues: [{ message: "This sale's receipt did not load in time; the next scan reads it.", externalReference: row.id, rejected: false }], stalled: true };
    }

    throw error;
  }
}
