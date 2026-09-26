import type { IngestListing, RunIssue } from "../../agent/types";
import { dig } from "../embedded-json";
import {
  AdapterError,
  type ListingsSnapshot,
  type MarketplaceAdapter,
  type MarketplaceContext,
  type PageKind,
  type SalesScan,
  type Session,
} from "../types";
import { readClosetPage } from "./listings";
import { assertReadable, siteChanged } from "./pages";
import { readState } from "./state";

const origin = "https://poshmark.com";

/** A scan never reads more closet pages than this; past it, it stops and says the snapshot is partial. */
export const maxPages = 40;

/**
 * Poshmark, read the way its own pages are served: a closet page carries its listings whole, in the state Poshmark
 * writes into the HTML. No API is called and nothing private is read — a closet is the same page a shopper sees.
 *
 * Sales are not read yet. They live behind the seller's own order pages, which look nothing like a closet, and an
 * adapter that guessed at them would be worse than one that says it cannot.
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

    return /^https:\/\/poshmark\.com\/(closet|feed|order|my)/i.test(url) ? "seller" : "other";
  },

  async detectSession(context: MarketplaceContext): Promise<Session> {
    const response = await context.page(origin);
    assertReadable(response);
    const state = readState(response.body, "home");
    const auth = dig(state, "auth");
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
  },

  scanSales(): Promise<SalesScan> {
    // Listed as a listings-only marketplace, so the server never asks. Saying so beats inventing sales.
    return Promise.reject(new AdapterError("Error", "Poshmark sales are not read from the browser yet."));
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
        const response = await context.page(address);
        assertReadable(response);
        const read = readClosetPage(response.body);
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
