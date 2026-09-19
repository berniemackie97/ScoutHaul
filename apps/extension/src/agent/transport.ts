import { browser } from "wxt/browser";
import type { PageResponse } from "../adapters/types";

/** A marketplace tab the agent reads through; closed when the run is done. */
export interface MarketplaceTab {
  /** Navigates the tab to a page, as a person's click would, and reads it once loaded. */
  page(url: string): Promise<PageResponse>;
  /** A request made from the page, for the JSON endpoints the site's own pages call. */
  request(url: string): Promise<PageResponse>;
  close(): Promise<void>;
}

/**
 * Runs inside the marketplace's own page, so a request is the site's own — same origin, its own
 * first-party cookies. Must stay self-contained: the browser copies this function into the page.
 */
async function pageFetch(url: string): Promise<PageResponse> {
  try {
    const response = await fetch(url, { credentials: "include", redirect: "follow", headers: { Accept: "application/json, text/plain, */*" } });
    return { status: response.status, url: response.url, contentType: response.headers.get("content-type"), body: await response.text() };
  } catch (error) {
    return { status: 0, url, contentType: null, body: error instanceof Error ? error.message : String(error) };
  }
}

/**
 * Reads the page the tab has loaded — including the data it embeds in its inline scripts, which stay
 * in the document after it renders. Self-contained, like pageFetch.
 */
function readLoadedPage(): PageResponse {
  const navigation = performance.getEntriesByType("navigation")[0] as (PerformanceEntry & { responseStatus?: number }) | undefined;
  return {
    status: navigation?.responseStatus || 200,
    url: location.href,
    contentType: document.contentType,
    body: document.documentElement.outerHTML,
  };
}

const loadTimeoutMs = 45_000;

/** Resolves when the tab finishes loading the navigation started after this is called. */
function nextLoad(tabId: number): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      browser.tabs.onUpdated.removeListener(listener);
      reject(new Error("The marketplace page did not finish loading."));
    }, loadTimeoutMs);
    const listener = (updatedId: number, change: { status?: string }) => {
      if (updatedId === tabId && change.status === "complete") {
        clearTimeout(timer);
        browser.tabs.onUpdated.removeListener(listener);
        resolve();
      }
    };
    browser.tabs.onUpdated.addListener(listener);
  });
}

/**
 * Opens a background tab on the marketplace and reads through it.
 *
 * Reading happens in a marketplace tab rather than the extension's background because every
 * browser treats that alike — Firefox isolates cookies per site, so a background request would not
 * reliably carry the seller's session. The tab opens without taking focus and closes when done.
 */
export async function openMarketplaceTab(workerUrl: string): Promise<MarketplaceTab> {
  const tab = await browser.tabs.create({ url: workerUrl, active: false });
  const tabId = tab.id;
  if (tabId === undefined) {
    throw new Error("The browser did not open a tab.");
  }

  // A new tab has no earlier page whose "complete" could be mistaken for this one's, so the first
  // load is simply waited for; every later page is a navigation watched from before it starts.
  const started = Date.now();
  while ((await browser.tabs.get(tabId)).status !== "complete") {
    if (Date.now() - started > loadTimeoutMs) {
      await browser.tabs.remove(tabId).catch(() => undefined);
      throw new Error("The marketplace page did not finish loading.");
    }

    await new Promise((resolve) => setTimeout(resolve, 200));
  }

  const navigate = async (url: string) => {
    const loaded = nextLoad(tabId);
    await browser.tabs.update(tabId, { url });
    await loaded;
  };

  return {
    async page(url: string): Promise<PageResponse> {
      await navigate(url);
      const [result] = await browser.scripting.executeScript({ target: { tabId }, func: readLoadedPage });
      return (result?.result as PageResponse | undefined) ?? { status: 0, url, contentType: null, body: "The page could not be read." };
    },
    async request(url: string): Promise<PageResponse> {
      const [result] = await browser.scripting.executeScript({ target: { tabId }, world: "MAIN", func: pageFetch, args: [url] });
      return (result?.result as PageResponse | undefined) ?? { status: 0, url, contentType: null, body: "The page did not answer." };
    },
    async close() {
      await browser.tabs.remove(tabId).catch(() => undefined);
    },
  };
}
