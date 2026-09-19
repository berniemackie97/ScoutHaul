import { browser } from "wxt/browser";
import type { PageResponse } from "../adapters/types";

/** A marketplace tab the agent reads through; closed when the run is done. */
export interface MarketplaceTab {
  get(url: string): Promise<PageResponse>;
  close(): Promise<void>;
}

/**
 * Runs inside the marketplace's own page, so a request is the site's own — same origin, its own
 * first-party cookies — exactly as if the seller's browser had asked. Must stay self-contained: the
 * browser copies this function into the page on its own.
 */
async function pageFetch(url: string): Promise<PageResponse> {
  try {
    const response = await fetch(url, {
      credentials: "include",
      redirect: "follow",
      headers: { Accept: "text/html,application/json;q=0.9,*/*;q=0.8" },
    });
    return { status: response.status, url: response.url, contentType: response.headers.get("content-type"), body: await response.text() };
  } catch (error) {
    return { status: 0, url, contentType: null, body: error instanceof Error ? error.message : String(error) };
  }
}

const loadTimeoutMs = 30_000;

/**
 * Opens a background tab at the marketplace's worker page and reads through it.
 *
 * Requests run in a marketplace tab rather than from the extension's background because every
 * browser treats that alike — Firefox isolates cookies per site, so a background request would not
 * reliably carry the seller's session. The tab opens without taking focus, and closes when done.
 */
export async function openMarketplaceTab(workerUrl: string): Promise<MarketplaceTab> {
  const tab = await browser.tabs.create({ url: workerUrl, active: false });
  const tabId = tab.id;
  if (tabId === undefined) {
    throw new Error("The browser did not open a tab.");
  }

  const started = Date.now();
  for (;;) {
    const current = await browser.tabs.get(tabId);
    if (current.status === "complete") {
      break;
    }

    if (Date.now() - started > loadTimeoutMs) {
      await browser.tabs.remove(tabId).catch(() => undefined);
      throw new Error("The marketplace page did not load.");
    }

    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  return {
    async get(url: string): Promise<PageResponse> {
      const [result] = await browser.scripting.executeScript({
        target: { tabId },
        world: "MAIN",
        func: pageFetch,
        args: [url],
      });
      return (result?.result as PageResponse | undefined) ?? { status: 0, url, contentType: null, body: "The page did not answer." };
    },
    async close() {
      await browser.tabs.remove(tabId).catch(() => undefined);
    },
  };
}
