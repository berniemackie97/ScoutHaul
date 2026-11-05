import { vi } from "vitest";
import type { PageResponse } from "../adapters/types";
import type { TabSource } from "../agent/agent";
import type { BorrowedTab, MarketplaceTab } from "../agent/transport";

export type Route = string | ((url: URL) => string);

/** A marketplace tab that answers from a table of pages, keyed by path, and remembers what it was asked for. */
export function servedTab(routes: Record<string, Route>): BorrowedTab & { read: string[] } {
  const read: string[] = [];
  const serve = async (url: string): Promise<PageResponse> => {
    read.push(url);
    const parsed = new URL(url);
    const route = routes[parsed.pathname];
    return route === undefined
      ? { status: 404, url, contentType: "text/html", body: "" }
      : { status: 200, url, contentType: "text/html", body: typeof route === "string" ? route : route(parsed) };
  };
  // As a borrowed tab, it stands on the marketplace's home page.
  return { page: serve, request: serve, current: () => serve("https://marketplace.test/"), read };
}

/** The agent's tabs, faked: its own sync tab, and optionally one the operator has open to borrow. */
export function fakeTabs(sync: MarketplaceTab, operator: BorrowedTab | null = null) {
  const tabs = {
    acquire: vi.fn(async () => sync),
    release: vi.fn(),
    borrow: vi.fn(async () => operator),
    owns: vi.fn(() => false),
    close: vi.fn(async () => undefined),
  } satisfies TabSource;
  return tabs;
}
