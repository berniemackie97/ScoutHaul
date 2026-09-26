import type { IngestListing, RunIssue } from "../../agent/types";
import {
  AdapterError,
  type ListingsSnapshot,
  type MarketplaceAdapter,
  type MarketplaceContext,
  type PageKind,
  type SalesScan,
  type Session,
} from "../types";
import { assertReadable, siteChanged } from "./pages";
import { readProduct } from "./product";

const origin = "https://www.depop.com";

/** How many listings one scan reads in full. Each is a page of its own, so this is what a scan costs. */
const listingsPerScan = 20;

/** A listing is read again only when it has not been read for this long. */
const staleDays = 7;

/** What this browser already read for one listing, so an unchanged shop costs one request. */
interface ListingMemory {
  at: number;
  listing: IngestListing;
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
 * Sales are not read: they are behind the seller's own pages, which this adapter has never seen.
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
    // The header's own Likes link is the signed-in seller's, and it is the only handle on the page that is
    // certainly theirs: a page can show any number of other people's, and an order page shows the buyer's.
    const handle = /href="\/([A-Za-z0-9_.-]{2,40})\/likes\/"/.exec(response.body);
    return handle
      ? { signedIn: true, username: handle[1]!, marketplaceUserId: null }
      : { signedIn: false, username: null, marketplaceUserId: null };
  },

  scanSales(): Promise<SalesScan> {
    // Registered as a listings-only marketplace, so the server never asks. Saying so beats inventing sales.
    return Promise.reject(new AdapterError("Error", "Depop sales are not read from the browser yet."));
  },

  async snapshotListings(context: MarketplaceContext, session: Session): Promise<ListingsSnapshot> {
    const shop = session.username;
    if (!shop) {
      throw new AdapterError("SignedOut", "The browser is signed out of Depop.");
    }

    const response = await context.page(`${origin}/${encodeURIComponent(shop)}/`);
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
    slugs.add(match[1]!);
  }

  return [...slugs];
}
