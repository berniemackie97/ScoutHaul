import type { FailureKind, IngestListing, IngestOrder, RunIssue } from "../agent/types";

/** How to know a page is ready to read. */
export interface PageRead {
  /** A variable the page builds for itself, read as the page sees it rather than from the HTML. */
  global?: string;
  /**
   * Where in that variable the page's data lives. A page can define the variable before it has filled it in, so the
   * read waits a while for the data to be there too, then reads what is there — as it does for `until`.
   */
  holds?: readonly string[];
  /** A selector for something the page draws in after it loads. The read waits a while for it, then reads what is there. */
  until?: string;
}

/** What came back from a request made inside a marketplace tab. */
export interface PageResponse {
  status: number;
  url: string;
  contentType: string | null;
  body: string;
  /** The page global asked for, as JSON from the page's own world; null when the page has none. */
  global?: string | null;
  /** Which document was read (its time origin), so a navigation can tell the new page from the one it left. */
  document?: number;
}

/**
 * How an adapter reaches its marketplace, from inside a marketplace tab, with every call paced by the
 * safety governor.
 *
 * A seller page is *opened* — the tab navigates to it the way a person's click would, and the loaded
 * page is read — because a page fetched from script looks nothing like a person viewing it, and
 * marketplaces' bot protection notices. Only the small JSON endpoints the site's own pages call are
 * *requested* from the page.
 */
export interface MarketplaceContext {
  page(url: string, read?: PageRead): Promise<PageResponse>;
  request(url: string): Promise<PageResponse>;
  now(): Date;
  /** Waits, without asking the marketplace anything. */
  sleep?(ms: number): Promise<void>;
  /**
   * One of the marketplace's cookies in this browser, read without a request: null when there is none, undefined when
   * cookies cannot be read here at all.
   */
  cookie?(name: string): Promise<string | null | undefined>;
  /** What the adapter keeps between scans in this browser, so work already done is not asked for again. */
  memory: AdapterMemory;
}

/**
 * A small store an adapter keeps between scans, in this browser only. Bounded: the oldest entries go
 * first, so it never grows without limit, and anything forgotten is simply read again.
 */
export interface AdapterMemory {
  get<T>(key: string): Promise<T | undefined>;
  set<T>(key: string, value: T): Promise<void>;
}

/** What one of the marketplace's own pages shows, read where it stands at no cost to anyone. */
export interface PageLook {
  /** A security check — a captcha, a block — rather than the page itself. */
  challenge: boolean;
  /** Whether someone is signed in, or null when the page does not say. */
  signedIn: boolean | null;
}

export interface Session {
  signedIn: boolean;
  username: string | null;
  marketplaceUserId: string | null;
}

export interface SalesScan {
  orders: IngestOrder[];
  /** Orders the marketplace shows as cancelled, by external order id — sales that did not happen after all. */
  cancelledOrderIds: string[];
  issues: RunIssue[];
  /** Where the next scan can resume from — the newest sale seen. */
  cursor: string | null;
  coversFrom: string;
  coversTo: string;
}

export interface ListingsSnapshot {
  listings: IngestListing[];
  issues: RunIssue[];
  /** True only when every live listing was read — so anything not in it is no longer live. */
  complete: boolean;
}

/** How far back a sales scan reads: explicitly from a date (history), or from where the last one stopped. */
export interface SalesWindow {
  from: Date | null;
  cursor: string | null;
}

/**
 * What a marketplace page is, from its address alone — so a sign-in form or a security check the
 * operator is working through is never mistaken for a page to read, or interrupted.
 */
export type PageKind = "sign-in" | "challenge" | "seller" | "other";

/**
 * One marketplace. An adapter only reads the seller's own account — orders, fees, listings — and
 * never engages with anything on the site: no sharing, following, offers or listing changes.
 */
export interface MarketplaceAdapter {
  readonly platform: string;
  /** Host permissions to ask for when the marketplace is connected. */
  readonly origins: string[];
  /** The origin requests are made from; a tab must be on it for them to be the site's own. */
  readonly home: string;
  /** Additional read-only document domains for this marketplace. */
  readonly requestDomains?: readonly string[];
  /** First-party API origins and the routing headers used for each one. */
  readonly apiRequests?: Readonly<Record<string, Readonly<Record<string, string>>>>;
  /** The page a marketplace tab is opened at when one is needed: a light one, on the site's own origin. */
  readonly workerUrl: string;
  /** Where to send the operator to sign in. */
  readonly signInUrl: string;
  /** Where to send the operator to clear a security check: the seller page that raised it. */
  readonly checkUrl: string;
  /** Whether an address is one of the marketplace's own pages — the only pages the extension reads. */
  readable(url: string): boolean;
  pageKind(url: string): PageKind;
  /** `shown`: a page already open on the marketplace, read where it stands, which the check may use instead of loading one. */
  detectSession(context: MarketplaceContext, shown?: PageResponse): Promise<Session>;
  /** A page as it stands: a security check, and whether someone is signed in. Without it, the address and `sessionFrom` tell. */
  look?(page: PageResponse): PageLook;
  /**
   * Who is signed in, as any of the marketplace's own pages shows it, or null when that page cannot tell — so a
   * sign-in check can read a tab the operator already has open instead of loading one. `sessionGlobal` is the page
   * global it reads, when it reads one.
   */
  sessionFrom?(page: PageResponse): Session | null;
  readonly sessionGlobal?: string;
  /** `session` is who the scan runs as, as just detected. */
  scanSales(context: MarketplaceContext, window: SalesWindow, session: Session): Promise<SalesScan>;
  snapshotListings(context: MarketplaceContext, session: Session): Promise<ListingsSnapshot>;
}

/** A failure an adapter understands, classified so the server and the governor react correctly. */
export class AdapterError extends Error {
  constructor(
    readonly kind: FailureKind,
    message: string,
  ) {
    super(message);
    this.name = "AdapterError";
  }
}
