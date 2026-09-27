import { browser } from "wxt/browser";
import { AdapterError, type MarketplaceAdapter, type PageRead, type PageResponse } from "../adapters/types";
import { pageLoadTimeoutMs, syncTabIdleMs, untilWaitMs } from "./config";
import { syncTabsItem } from "./storage";

/** A marketplace tab the agent reads through. */
export interface MarketplaceTab {
  /** Navigates the tab to a page, as a person's click would, and reads it once loaded. */
  page(url: string, read?: PageRead): Promise<PageResponse>;
  /** A request made from the marketplace's own page, for the JSON endpoints its pages call. */
  request(url: string): Promise<PageResponse>;
}

/** A tab the operator has open: never navigated, but its page can be read as it stands. */
export interface BorrowedTab extends MarketplaceTab {
  current(global?: string): Promise<PageResponse>;
}

/** Time, for waiting on tabs — injected so the waiting is tested without waiting. */
export interface Clock {
  now(): number;
  sleep(ms: number): Promise<void>;
}

const realClock: Clock = { now: () => Date.now(), sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)) };

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
 * Reads the page the tab has loaded, and with it, when asked, a variable the page built for itself — in one go, so both
 * come from the same document. The variable needs the page's own world: the extension's cannot see it, and a
 * server-rendered page often deletes the script that wrote it, so it is not in the HTML. Self-contained, like pageFetch.
 */
function readLoadedPage(global: string | null): PageResponse {
  const navigation = performance.getEntriesByType("navigation")[0] as (PerformanceEntry & { responseStatus?: number }) | undefined;
  const page: PageResponse = {
    status: navigation?.responseStatus || 200,
    url: location.href,
    contentType: document.contentType,
    body: document.documentElement.outerHTML,
    document: performance.timeOrigin,
  };
  if (global === null) {
    return page;
  }

  try {
    const value = (window as unknown as Record<string, unknown>)[global];
    return { ...page, global: value === undefined ? null : JSON.stringify(value) };
  } catch {
    return { ...page, global: null };
  }
}

/**
 * A page still loading whose document is parsed and already holds what will be read. Marketplace pages keep loading
 * ads and trackers long after that, and some never tell the browser they are done. Self-contained, like pageFetch.
 */
function pageSettled(global: string | null, until: string | null): Settling {
  return {
    parsed: document.readyState !== "loading" && (global === null || (window as unknown as Record<string, unknown>)[global] !== undefined),
    found: until === null || document.querySelector(until) !== null,
    document: performance.timeOrigin,
  };
}

interface Settling {
  parsed: boolean;
  found: boolean;
  document: number;
}

/** How far the new page has got — never the document the navigation left, which can linger until the new one commits. */
async function settlingIn(tabId: number, read: PageRead, leaving: number | null): Promise<Settling | null> {
  const args: [string | null, string | null] = [read.global ?? null, read.until ?? null];
  try {
    const [first] = await browser.scripting.executeScript(read.global === undefined
      ? { target: { tabId }, func: pageSettled, args, injectImmediately: true }
      : { target: { tabId }, world: "MAIN", func: pageSettled, args, injectImmediately: true });
    const probe = first?.result as Settling | undefined;
    return probe && probe.document !== leaving ? probe : null;
  } catch {
    return null;
  }
}

/** The time origin of the document a tab shows now: what identifies it once a navigation has replaced it. */
function documentOrigin(): number {
  return performance.timeOrigin;
}

async function documentIn(tabId: number): Promise<number | null> {
  try {
    const [first] = await browser.scripting.executeScript({ target: { tabId }, func: documentOrigin, injectImmediately: true });
    return typeof first?.result === "number" ? first.result : null;
  } catch {
    return null;
  }
}

/** One look at a tab: how far it has loaded, and where it is. */
export interface TabLook {
  status?: string;
  url?: string;
  /** Whether what will be read from the page is there, whatever the browser says of its loading. */
  settled?: boolean;
}

export type TabVerdict = "wait" | "ready" | "elsewhere";

/**
 * Judges one look at a tab that should be loading a marketplace page.
 *
 * This is where browsers differ, and where the extension once went wrong: Firefox reports a tab it
 * has only just opened as "complete" while it still shows about:blank, before the page it was opened
 * at has even begun to load — and a page read then is refused ("Missing host permission for the
 * tab"). So a page counts only once it has finished loading at a marketplace address; and, for a
 * navigation, only once that navigation has begun, so the page it replaces is never taken for it.
 */
export function judgeTab(look: TabLook, readable: (url: string) => boolean, begun: boolean): TabVerdict {
  const url = look.url ?? "";
  const loaded = look.settled ?? look.status === "complete";
  if (!begun || !loaded || url === "" || url.startsWith("about:")) {
    return "wait";
  }

  return readable(url) ? "ready" : "elsewhere";
}

export interface PageWait extends Clock {
  readable(url: string): boolean;
  /** Whether the load being waited for has begun, given a look at the tab. */
  begun(look: TabLook): boolean;
  timeoutMs: number;
}

/** Waits until a tab has loaded a marketplace page, and returns the page's address. */
export async function waitForPage(look: () => Promise<TabLook | null>, wait: PageWait): Promise<string> {
  const deadline = wait.now() + wait.timeoutMs;
  for (;;) {
    const seen = await look();
    if (seen === null) {
      throw new AdapterError("Interrupted", "The sync tab was closed.");
    }

    const verdict = judgeTab(seen, wait.readable, wait.begun(seen));
    if (verdict === "ready") {
      return seen.url!;
    }

    if (verdict === "elsewhere") {
      throw new AdapterError("Interrupted", `The sync tab moved to ${hostOf(seen.url)}.`);
    }

    if (wait.now() >= deadline) {
      throw new AdapterError("Network", "The page took too long to load.");
    }

    await wait.sleep(150);
  }
}

function hostOf(url: string | undefined): string {
  try {
    return new URL(url ?? "").host || "another page";
  } catch {
    return "another page";
  }
}

async function lookAt(tabId: number): Promise<TabLook | null> {
  try {
    const tab = await browser.tabs.get(tabId);
    return { status: tab.status, url: tab.url };
  } catch {
    return null;
  }
}

type Injection = () => Promise<unknown[]>;

/**
 * Runs a function in a tab and returns its result. A tab caught between pages can refuse a script
 * for a moment, so a refusal is tried again briefly before it is explained in words a person can act
 * on — access taken away, the tab closed or moved elsewhere, or a page that did not load — rather
 * than as the browser's own message.
 */
async function inTab<T>(tabId: number, adapter: MarketplaceAdapter, clock: Clock, inject: Injection): Promise<T> {
  let failure: unknown = null;
  for (let attempt = 0; attempt < 3; attempt++) {
    if (attempt > 0) {
      await clock.sleep(400);
    }

    try {
      const [first] = (await inject()) as ({ result?: unknown } | undefined)[];
      if (first?.result !== undefined && first.result !== null) {
        return first.result as T;
      }
    } catch (error) {
      failure = error;
    }
  }

  throw await explain(failure, tabId, adapter);
}

async function explain(error: unknown, tabId: number, adapter: MarketplaceAdapter): Promise<AdapterError> {
  if (!(await browser.permissions.contains({ origins: adapter.origins }).catch(() => true))) {
    return new AdapterError("NoAccess", `HaulScout Sync is no longer allowed to reach ${adapter.platform}.`);
  }

  const look = await lookAt(tabId);
  if (look === null) {
    return new AdapterError("Interrupted", "The sync tab was closed.");
  }

  if (!look.url || !adapter.readable(look.url)) {
    return new AdapterError("Interrupted", `The sync tab moved to ${hostOf(look.url)}.`);
  }

  console.warn(`HaulScout Sync could not read a ${adapter.platform} page.`, error);
  return new AdapterError("Network", `${adapter.platform}'s page did not load properly.`);
}

function readTab(tabId: number, adapter: MarketplaceAdapter, clock: Clock, global: string | undefined): Promise<PageResponse> {
  return inTab<PageResponse>(tabId, adapter, clock, () =>
    global === undefined
      ? browser.scripting.executeScript({ target: { tabId }, func: readLoadedPage, args: [null], injectImmediately: true })
      : browser.scripting.executeScript({ target: { tabId }, world: "MAIN", func: readLoadedPage, args: [global], injectImmediately: true }));
}

function fetchIn(tabId: number, url: string): Injection {
  return () => browser.scripting.executeScript({ target: { tabId }, world: "MAIN", func: pageFetch, args: [url], injectImmediately: true });
}

/**
 * The tabs the agent reads marketplaces through: one per marketplace, and only while there is work.
 *
 * A sync tab opens pinned and in the background, in an ordinary window of the browser — never a
 * private one, which has no sign-in — so it never takes focus. Work that follows soon after reuses
 * it, and it closes itself a few seconds after the last. Nobody has to open, keep or close anything:
 * a sync tab closed by hand mid-scan fails that scan with a reason, and the next scan opens another.
 */
export class SyncTabs {
  private readonly open = new Map<string, number>();
  private readonly closers = new Map<string, ReturnType<typeof setTimeout>>();

  constructor(private readonly clock: Clock = realClock) {}

  /** Whether a tab is a sync tab — whose page loads are the agent's own, not the operator's. */
  owns(tabId: number): boolean {
    return [...this.open.values()].includes(tabId);
  }

  /** The marketplace's sync tab — the one already open, or a new one — loaded and ready to read through. */
  async acquire(adapter: MarketplaceAdapter): Promise<MarketplaceTab> {
    this.cancelClose(adapter.platform);
    const tabId = (await this.reusable(adapter)) ?? (await this.create(adapter));
    return this.reader(adapter, tabId);
  }

  /** Done with it for now: it closes unless more work comes along within a few seconds. */
  release(adapter: MarketplaceAdapter): void {
    this.cancelClose(adapter.platform);
    this.closers.set(adapter.platform, setTimeout(() => void this.close(adapter.platform), syncTabIdleMs));
  }

  async close(platform: string): Promise<void> {
    this.cancelClose(platform);
    const tabId = this.open.get(platform);
    if (tabId === undefined) {
      return;
    }

    this.open.delete(platform);
    await this.remember();
    await browser.tabs.remove(tabId).catch(() => undefined);
  }

  /** Closes sync tabs that a background stopped mid-work left open. Run as the background starts. */
  async sweep(): Promise<void> {
    const left = await syncTabsItem.getValue();
    for (const [platform, tabId] of Object.entries(left)) {
      if (this.open.get(platform) !== tabId) {
        await browser.tabs.remove(tabId).catch(() => undefined);
      }
    }

    await this.remember();
  }

  /**
   * A tab the operator already has open on the marketplace, fully loaded, for a quick request that
   * then needs no tab of its own — checking who is signed in, just after they sign in. Only a
   * request is ever made through it, never a navigation: it is their tab.
   */
  async borrow(adapter: MarketplaceAdapter, tabId?: number): Promise<BorrowedTab | null> {
    const candidates = tabId === undefined
      ? await browser.tabs.query({ url: `${adapter.home}/*`, status: "complete" }).catch(() => [])
      : [await browser.tabs.get(tabId).catch(() => null)];
    const tab = candidates.find((candidate) =>
      candidate?.id !== undefined
      && !this.owns(candidate.id)
      && candidate.status === "complete"
      && !candidate.incognito
      && !candidate.discarded
      && (candidate.url?.startsWith(`${adapter.home}/`) ?? false)
      && adapter.pageKind(candidate.url!) !== "challenge"
      && inDefaultContainer(candidate));
    if (tab?.id === undefined) {
      return null;
    }

    const borrowed = tab.id;
    return {
      page: async () => {
        throw new Error("A borrowed tab is never navigated.");
      },
      current: (global?: string) => readTab(borrowed, adapter, this.clock, global),
      request: (url: string) => inTab<PageResponse>(borrowed, adapter, this.clock, fetchIn(borrowed, url)),
    };
  }

  private cancelClose(platform: string): void {
    clearTimeout(this.closers.get(platform));
    this.closers.delete(platform);
  }

  private async remember(): Promise<void> {
    await syncTabsItem.setValue(Object.fromEntries(this.open));
  }

  private async reusable(adapter: MarketplaceAdapter): Promise<number | null> {
    const tabId = this.open.get(adapter.platform);
    if (tabId === undefined) {
      return null;
    }

    const tab = await browser.tabs.get(tabId).catch(() => null);
    if (tab && !tab.active && tab.status === "complete" && tab.url && adapter.readable(tab.url)) {
      return tabId;
    }

    // Closed, or the operator has taken it over — it is theirs now, and the work gets a new one.
    this.open.delete(adapter.platform);
    await this.remember();
    return null;
  }

  private async create(adapter: MarketplaceAdapter): Promise<number> {
    const windowId = await this.windowFor();
    const tab = windowId === undefined
      ? await this.inNewWindow(adapter.workerUrl)
      : await browser.tabs.create({ windowId, url: adapter.workerUrl, active: false, pinned: true }).catch((error: unknown) => {
          console.warn("HaulScout Sync could not open its tab.", error);
          return undefined;
        });
    const tabId = tab?.id;
    if (tabId === undefined) {
      throw new AdapterError("Interrupted", "The browser did not open a tab to sync in.");
    }

    this.open.set(adapter.platform, tabId);
    await this.remember();
    // A new tab has no earlier page to mistake for this one: any marketplace page it finishes is it.
    await waitForPage(() => lookAt(tabId), { ...this.clock, readable: adapter.readable, begun: () => true, timeoutMs: pageLoadTimeoutMs });
    return tabId;
  }

  /** An ordinary window to open the sync tab in: the one in front, or any — never a private one. */
  private async windowFor(): Promise<number | undefined> {
    const windows = await browser.windows.getAll({ windowTypes: ["normal"] }).catch(() => []);
    const usable = windows.filter((window) => !window.incognito);
    return (usable.find((window) => window.focused) ?? usable[0])?.id;
  }

  /** With every window closed, the sync tab gets a minimized window of its own. */
  private async inNewWindow(url: string) {
    const created = await browser.windows.create({ url, focused: false, state: "minimized" })
      .catch(() => browser.windows.create({ url, state: "minimized" }))
      .catch((error: unknown) => {
        console.warn("HaulScout Sync could not open a window.", error);
        return undefined;
      });
    return created?.tabs?.[0];
  }

  private reader(adapter: MarketplaceAdapter, tabId: number): MarketplaceTab {
    /** Navigates the tab and waits for the new page. Returns the address it left. */
    const navigate = async (url: string, read: PageRead, leaving: number | null): Promise<string | undefined> => {
      const before = (await lookAt(tabId))?.url;
      let started = false;
      const listener = (updatedId: number, change: { status?: string; url?: string }) => {
        if (updatedId === tabId && (change.status === "loading" || change.url !== undefined)) {
          started = true;
        }
      };
      browser.tabs.onUpdated.addListener(listener);
      try {
        const updated = await browser.tabs.update(tabId, { url }).catch(() => null);
        if (!updated) {
          throw new AdapterError("Interrupted", "The sync tab was closed.");
        }

        const begun = (seen: TabLook) => started || (seen.url !== undefined && seen.url !== before);
        let parsedAt: number | null = null;
        const look = async (): Promise<TabLook | null> => {
          const seen = await lookAt(tabId);
          const waitsForMore = read.until !== undefined || read.global !== undefined;
          if (!seen || !seen.url || !adapter.readable(seen.url) || !begun(seen) || (seen.status === "complete" && !waitsForMore)) {
            return seen;
          }

          const probe = await settlingIn(tabId, read, leaving);
          if (!probe?.parsed) {
            return { ...seen, settled: false };
          }

          // Something drawn in after load may never come — an empty list draws nothing — so it is waited for a while.
          parsedAt ??= this.clock.now();
          return { ...seen, settled: probe.found || this.clock.now() - parsedAt >= untilWaitMs };
        };
        await waitForPage(look, { ...this.clock, readable: adapter.readable, begun, timeoutMs: pageLoadTimeoutMs });
        return before;
      } finally {
        browser.tabs.onUpdated.removeListener(listener);
      }
    };
    const read = (global: string | undefined) => readTab(tabId, adapter, this.clock, global);

    return {
      page: async (url: string, pageRead: PageRead = {}) => {
        const leaving = await documentIn(tabId);
        const before = await navigate(url, pageRead, leaving);
        const deadline = this.clock.now() + pageLoadTimeoutMs;
        for (;;) {
          const loaded = await read(pageRead.global);
          // Still the page it was leaving: the browser said the new one had loaded before it had replaced the old.
          const stale = leaving !== null && loaded.document !== undefined
            ? loaded.document === leaving
            : before !== undefined && url !== before && loaded.url === before;
          if (!stale) {
            return loaded;
          }

          if (this.clock.now() >= deadline) {
            throw new AdapterError("Network", "The page took too long to load.");
          }

          await this.clock.sleep(150);
        }
      },
      request: async (url: string) => {
        // A request is the site's own only from a page on the site's own origin.
        const current = (await lookAt(tabId))?.url;
        if (!current?.startsWith(`${adapter.home}/`)) {
          await navigate(adapter.workerUrl, {}, await documentIn(tabId));
        }

        return inTab<PageResponse>(tabId, adapter, this.clock, fetchIn(tabId, url));
      },
    };
  }
}

/** Firefox containers keep sign-ins of their own; only the ordinary one is the browser's. */
function inDefaultContainer(tab: object): boolean {
  const store = (tab as { cookieStoreId?: string }).cookieStoreId;
  return store === undefined || store === "firefox-default" || store === "0";
}
