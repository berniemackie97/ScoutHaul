import { vi } from "vitest";
import type { PageResponse } from "../adapters/types";
import type { TabSource } from "../agent/agent";
import type { MarketplaceTab } from "../agent/transport";

export type Route = string | ((url: URL) => string);

/** A marketplace tab that answers from a table of pages, keyed by path, and remembers what it was asked for. */
export function servedTab(routes: Record<string, Route>): MarketplaceTab & { read: string[] } {
  const read: string[] = [];
  const serve = async (url: string): Promise<PageResponse> => {
    read.push(url);
    const parsed = new URL(url);
    const route = routes[parsed.pathname];
    return route === undefined
      ? { status: 404, url, contentType: "text/html", body: "" }
      : { status: 200, url, contentType: "text/html", body: typeof route === "string" ? route : route(parsed) };
  };
  return { page: serve, request: serve, read };
}

/** The agent's tabs, faked: its own sync tab, and optionally one the operator has open to borrow. */
export function fakeTabs(sync: MarketplaceTab, operator: MarketplaceTab | null = null) {
  const tabs = {
    acquire: vi.fn(async () => sync),
    release: vi.fn(),
    borrow: vi.fn(async () => operator),
    owns: vi.fn(() => false),
  } satisfies TabSource;
  return tabs;
}
