import type { FailureKind, IngestListing, IngestOrder, RunIssue } from "../agent/types";

/** What came back from a request made inside a marketplace tab. */
export interface PageResponse {
  status: number;
  url: string;
  contentType: string | null;
  body: string;
}

/**
 * How an adapter reaches its marketplace. Each call is one request, made inside a marketplace tab
 * with the site's own cookies, and paced by the safety governor before it goes out.
 */
export interface MarketplaceContext {
  get(url: string): Promise<PageResponse>;
  now(): Date;
}

export interface Session {
  signedIn: boolean;
  username: string | null;
  marketplaceUserId: string | null;
}

export interface SalesScan {
  orders: IngestOrder[];
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
 * One marketplace. An adapter only reads the seller's own account — orders, fees, listings — and
 * never engages with anything on the site: no sharing, following, offers or listing changes.
 */
export interface MarketplaceAdapter {
  readonly platform: string;
  /** Host permissions to ask for when the marketplace is connected. */
  readonly origins: string[];
  /** The page a marketplace tab is opened at when one is needed: a light one, on the site's own origin. */
  readonly workerUrl: string;
  /** Where to send the operator to sign in. */
  readonly signInUrl: string;
  detectSession(context: MarketplaceContext): Promise<Session>;
  scanSales(context: MarketplaceContext, window: SalesWindow): Promise<SalesScan>;
  snapshotListings(context: MarketplaceContext): Promise<ListingsSnapshot>;
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
