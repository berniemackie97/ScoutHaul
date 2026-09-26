import { browser } from "wxt/browser";
import { AdapterError, type MarketplaceAdapter, type PageResponse } from "../adapters/types";
import { pageLoadTimeoutMs, syncTabIdleMs } from "./config";
import { syncTabsItem } from "./storage";

/** A marketplace tab the agent reads through. */
export interface MarketplaceTab {
  /** Navigates the tab to a page, as a person's click would, and reads it once loaded. */
  page(url: string, global?: string): Promise<PageResponse>;
  /** A request made from the marketplace's own page, for the JSON endpoints its pages call. */
  request(url: string): Promise<PageResponse>;
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

/**
 * A variable the page built for itself, as JSON. Runs in the page's own world, since the extension's cannot see it;
 * a server-rendered page often deletes the script that wrote it once it has run, so it is not in the HTML to read.
 */
function readGlobal(name: string): { json: string | null } {
  try {
    const value = (window as unknown as Record<string, unknown>)[name];
    return { json: value === undefined ? null : JSON.stringify(value) };
  } catch {
    return { json: null };
  }
}

/** One look at a tab: how far it has loaded, and where it is. */
export interface TabLook {
  status?: string;
  url?: string;
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
  if (!begun || look.status !== "complete" || url === "" || url.startsWith("about:")) {
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

function fetchIn(tabId: number, url: string): Injection {
  return () => browser.scripting.executeScript({ target: { tabId }, world: "MAIN", func: pageFetch, args: [url] });
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
  async borrow(adapter: MarketplaceAdapter, tabId?: number): Promise<MarketplaceTab | null> {
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
    const navigate = async (url: string): Promise<string | undefined> => {
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

        await waitForPage(() => lookAt(tabId), {
          ...this.clock,
          readable: adapter.readable,
          begun: (seen) => started || (seen.url !== undefined && seen.url !== before),
          timeoutMs: pageLoadTimeoutMs,
        });
        return before;
      } finally {
        browser.tabs.onUpdated.removeListener(listener);
      }
    };
    const readPage = () => inTab<PageResponse>(tabId, adapter, this.clock, () => browser.scripting.executeScript({ target: { tabId }, func: readLoadedPage }));
    const read = async (global: string | undefined): Promise<PageResponse> => {
      const loaded = await readPage();
      if (global === undefined) {
        return loaded;
      }

      const captured = await inTab<{ json: string | null }>(tabId, adapter, this.clock, () =>
        browser.scripting.executeScript({ target: { tabId }, world: "MAIN", func: readGlobal, args: [global] }));
      return { ...loaded, global: captured.json };
    };

    return {
      page: async (url: string, global?: string) => {
        const before = await navigate(url);
        const loaded = await read(global);
        if (before === undefined || loaded.url !== before || url === before) {
          return loaded;
        }

        // Still the page it was leaving: the browser said the new one had loaded before it had
        // replaced the old. Wait for the address to move on, then read again.
        await waitForPage(() => lookAt(tabId), { ...this.clock, readable: adapter.readable, begun: (seen) => seen.url !== before, timeoutMs: pageLoadTimeoutMs });
        return read(global);
      },
      request: async (url: string) => {
        // A request is the site's own only from a page on the site's own origin.
        const current = (await lookAt(tabId))?.url;
        if (!current?.startsWith(`${adapter.home}/`)) {
          await navigate(adapter.workerUrl);
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
