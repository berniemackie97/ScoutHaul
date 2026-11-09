import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fakeBrowser } from "wxt/testing/fake-browser";
import { ebayAdapter } from "../adapters/ebay";
import { poshmarkAdapter } from "../adapters/poshmark";
import { AdapterError } from "../adapters/types";
import { assertMarketplaceDestination, judgeTab, pageSettled, SyncTabs, waitForPage, type Clock, type TabLook } from "./transport";

const robots = "https://www.ebay.com/robots.txt";
const readable = ebayAdapter.readable;

function fakeClock(): Clock & { elapsed: () => number } {
  let now = 0;
  return { now: () => now, sleep: async (ms) => void (now += ms), elapsed: () => now };
}

function looks(...sequence: (TabLook | null)[]) {
  let index = 0;
  return vi.fn(async () => sequence[Math.min(index++, sequence.length - 1)] ?? null);
}

describe("judging a tab that should be loading a marketplace page", () => {
  it("never takes Firefox's blank new tab for the page it was opened at", () => {
    expect(judgeTab({ status: "complete", url: "about:blank" }, readable, true)).toBe("wait");
    expect(judgeTab({ status: "loading", url: robots }, readable, true)).toBe("wait");
    expect(judgeTab({ status: "complete", url: robots }, readable, true)).toBe("ready");
  });

  it("never takes the page a navigation is leaving for the one it is going to", () => {
    expect(judgeTab({ status: "complete", url: robots }, readable, false)).toBe("wait");
  });

  it("takes a page that is still loading once what will be read from it is there, but never before its navigation begins", () => {
    // Poshmark and Depop keep loading ads and trackers long after their data is in, and may never say they are done.
    expect(judgeTab({ status: "loading", url: robots, settled: true }, readable, true)).toBe("ready");
    expect(judgeTab({ status: "loading", url: robots, settled: true }, readable, false)).toBe("wait");
    expect(judgeTab({ status: "loading", url: robots, settled: false }, readable, true)).toBe("wait");
  });

  it("knows a page that finished somewhere other than the marketplace", () => {
    expect(judgeTab({ status: "complete", url: "https://example.com/" }, readable, true)).toBe("elsewhere");
    expect(judgeTab({ status: "complete", url: "https://signin.ebay.com/ws/eBayISAPI.dll" }, readable, true)).toBe("ready");
  });
});

describe("waiting for a page", () => {
  it("waits out Firefox's about:blank, then the load, and returns the page", async () => {
    const clock = fakeClock();
    const look = looks(
      { status: "complete", url: "about:blank" },
      { status: "loading", url: "about:blank" },
      { status: "loading", url: robots },
      { status: "complete", url: robots },
    );

    await expect(waitForPage(look, { ...clock, readable, begun: () => true, timeoutMs: 45_000 })).resolves.toBe(robots);
    expect(look).toHaveBeenCalledTimes(4);
  });

  it("says the tab was closed, in words, when it goes away", async () => {
    const clock = fakeClock();
    await expect(waitForPage(looks(null), { ...clock, readable, begun: () => true, timeoutMs: 45_000 })).rejects.toMatchObject({ kind: "Interrupted" });
  });

  it("gives up on a page that never loads as unreachable", async () => {
    const clock = fakeClock();
    const waiting = waitForPage(looks({ status: "loading", url: robots }), { ...clock, readable, begun: () => true, timeoutMs: 45_000 });
    await expect(waiting).rejects.toMatchObject({ kind: "Network" });
    expect(clock.elapsed()).toBeGreaterThanOrEqual(45_000);
  });

  it("stops when the tab is taken off the marketplace", async () => {
    const clock = fakeClock();
    const waiting = waitForPage(looks({ status: "complete", url: "https://www.example.com/" }), { ...clock, readable, begun: () => true, timeoutMs: 45_000 });
    await expect(waiting).rejects.toThrow("The sync tab moved to www.example.com.");
  });
});

describe("the sync tab", () => {
  beforeEach(() => {
    fakeBrowser.reset();
    fakeBrowser.permissions.contains = vi.fn(async () => true) as typeof fakeBrowser.permissions.contains;
    fakeBrowser.windows.getAll = vi.fn(async () => [
      { id: 5, focused: false, incognito: true, alwaysOnTop: false },
      { id: 1, focused: true, incognito: false, alwaysOnTop: false },
    ]) as unknown as typeof fakeBrowser.windows.getAll;
  });

  it("opens pinned and in the background, in an ordinary window, and reads only once eBay has loaded", async () => {
    const create = vi.fn(async () => ({ id: 7, index: 0, pinned: true, highlighted: false, windowId: 1, active: false, incognito: false, selected: false, discarded: false, autoDiscardable: true, groupId: -1, frozen: false }));
    fakeBrowser.tabs.create = create as unknown as typeof fakeBrowser.tabs.create;
    const sequence: TabLook[] = [
      { status: "complete", url: "about:blank" },
      { status: "loading", url: "about:blank" },
      { status: "complete", url: robots },
    ];
    let loaded = false;
    fakeBrowser.tabs.get = vi.fn(async () => {
      const next = sequence.shift() ?? { status: "complete", url: robots };
      loaded = next.url === robots && next.status === "complete";
      return { id: 7, ...next };
    }) as unknown as typeof fakeBrowser.tabs.get;
    const execute = vi.fn(async () => {
      if (!loaded) {
        throw new Error("Missing host permission for the tab");
      }

      return [{ frameId: 0, result: { status: 200, url: "https://www.ebay.com/gh/user_profile", contentType: "application/json", body: "{}" } }];
    });
    fakeBrowser.scripting.executeScript = execute as unknown as typeof fakeBrowser.scripting.executeScript;

    const tabs = new SyncTabs(fakeClock());
    const tab = await tabs.acquire(ebayAdapter);
    const answer = await tab.request("https://www.ebay.com/gh/user_profile");

    expect(create).toHaveBeenCalledWith({ windowId: 1, url: robots, active: false, pinned: true });
    expect(answer.status).toBe(200);
    expect(execute).toHaveBeenCalledTimes(1);
    expect(tabs.owns(7)).toBe(true);
  });

  it("never hands back the page a navigation was leaving, even if the browser says the new one loaded", async () => {
    const orders = "https://www.ebay.com/sh/ord/?offset=0";
    fakeBrowser.tabs.create = vi.fn(async () => ({ id: 9 })) as unknown as typeof fakeBrowser.tabs.create;
    let at = robots;
    fakeBrowser.tabs.get = vi.fn(async () => ({ id: 9, status: "complete", url: at })) as unknown as typeof fakeBrowser.tabs.get;
    fakeBrowser.tabs.update = vi.fn(async () => {
      at = orders;
      return { id: 9 };
    }) as unknown as typeof fakeBrowser.tabs.update;
    // The first read still finds the old document; the second, the new one.
    const documents = [robots, orders];
    fakeBrowser.scripting.executeScript = vi.fn(async () => [
      { frameId: 0, result: { status: 200, url: documents.shift() ?? orders, contentType: "text/html", body: "<html></html>" } },
    ]) as unknown as typeof fakeBrowser.scripting.executeScript;

    const tab = await new SyncTabs(fakeClock()).acquire(ebayAdapter);

    expect((await tab.page(orders)).url).toBe(orders);
  });

  it("explains a closed tab as that, not as the browser's own message", async () => {
    fakeBrowser.tabs.create = vi.fn(async () => ({ id: 9 })) as unknown as typeof fakeBrowser.tabs.create;
    let closed = false;
    fakeBrowser.tabs.get = vi.fn(async () => {
      if (closed) {
        throw new Error("Invalid tab ID: 9");
      }

      return { id: 9, status: "complete", url: robots };
    }) as unknown as typeof fakeBrowser.tabs.get;
    fakeBrowser.scripting.executeScript = vi.fn(async () => {
      closed = true;
      throw new Error("Missing host permission for the tab");
    }) as unknown as typeof fakeBrowser.scripting.executeScript;

    const tab = await new SyncTabs(fakeClock()).acquire(ebayAdapter);
    const reading = tab.request("https://www.ebay.com/gh/user_profile");

    await expect(reading).rejects.toBeInstanceOf(AdapterError);
    await expect(reading).rejects.toMatchObject({ kind: "Interrupted", message: "The sync tab was closed." });
  });

  it("says so when access to the marketplace was taken away", async () => {
    fakeBrowser.tabs.create = vi.fn(async () => ({ id: 9 })) as unknown as typeof fakeBrowser.tabs.create;
    fakeBrowser.tabs.get = vi.fn(async () => ({ id: 9, status: "complete", url: robots })) as unknown as typeof fakeBrowser.tabs.get;
    fakeBrowser.scripting.executeScript = vi.fn(async () => {
      throw new Error("Missing host permission for the tab");
    }) as unknown as typeof fakeBrowser.scripting.executeScript;

    const tab = await new SyncTabs(fakeClock()).acquire(ebayAdapter);
    fakeBrowser.permissions.contains = vi.fn(async () => false) as typeof fakeBrowser.permissions.contains;

    await expect(tab.request("https://www.ebay.com/gh/user_profile")).rejects.toMatchObject({ kind: "NoAccess" });
  });

  it("borrows only an ordinary, loaded tab the operator has on eBay's own site — never a security check or a private window", async () => {
    fakeBrowser.tabs.query = vi.fn(async () => [
      { id: 2, status: "complete", url: "https://www.ebay.com/splashui/challenge?ap=1", incognito: false },
      { id: 3, status: "complete", url: "https://www.ebay.com/itm/1", incognito: true },
      { id: 4, status: "complete", url: "https://www.ebay.com/itm/2", incognito: false, cookieStoreId: "firefox-container-1" },
      { id: 6, status: "complete", url: "https://www.ebay.com/sh/ord/", incognito: false, cookieStoreId: "firefox-default" },
    ]) as unknown as typeof fakeBrowser.tabs.query;
    const execute = vi.fn(async () => [{ frameId: 0, result: { status: 200, url: "https://www.ebay.com/gh/user_profile", contentType: "application/json", body: "{}" } }]);
    fakeBrowser.scripting.executeScript = execute as unknown as typeof fakeBrowser.scripting.executeScript;

    const update = vi.fn();
    fakeBrowser.tabs.update = update as unknown as typeof fakeBrowser.tabs.update;

    const borrowed = await new SyncTabs(fakeClock()).borrow(ebayAdapter);
    await borrowed!.request("https://www.ebay.com/gh/user_profile");
    await borrowed!.current();

    expect(execute).toHaveBeenCalledWith(expect.objectContaining({ target: { tabId: 6 }, world: "MAIN" }));
    // The operator's tab is read as it stands, and never taken somewhere else.
    await expect(borrowed!.page("https://www.ebay.com/sh/lst/active")).rejects.toThrow("never navigated");
    expect(update).not.toHaveBeenCalled();
  });

  it("reads a page that has settled without waiting for a load that may never finish, and never waits for the page to idle", async () => {
    const orders = "https://www.ebay.com/sh/ord/?offset=0";
    fakeBrowser.tabs.create = vi.fn(async () => ({ id: 9 })) as unknown as typeof fakeBrowser.tabs.create;
    let at = robots;
    let status = "complete";
    fakeBrowser.tabs.get = vi.fn(async () => ({ id: 9, status, url: at })) as unknown as typeof fakeBrowser.tabs.get;
    fakeBrowser.tabs.update = vi.fn(async () => {
      at = orders;
      status = "loading";
      return { id: 9 };
    }) as unknown as typeof fakeBrowser.tabs.update;
    const answers: Record<string, unknown> = {
      documentOrigin: 1,
      pageSettled: { parsed: true, found: true, document: 2 },
      readLoadedPage: { status: 200, url: orders, contentType: "text/html", body: "<html></html>", document: 2 },
    };
    const execute = vi.fn(async (injection: { func: { name: string } }) => [{ frameId: 0, result: answers[injection.func.name] }]);
    fakeBrowser.scripting.executeScript = execute as unknown as typeof fakeBrowser.scripting.executeScript;
    const clock = fakeClock();

    const tab = await new SyncTabs(clock).acquire(ebayAdapter);
    const page = await tab.page(orders, { global: "__INITIAL_STATE__" });

    expect(page.url).toBe(orders);
    expect(clock.elapsed()).toBeLessThan(1000);
    expect(execute).toHaveBeenCalledWith(expect.objectContaining({ world: "MAIN", args: ["__INITIAL_STATE__", null, null], injectImmediately: true }));
    for (const [injection] of execute.mock.calls) {
      expect(injection).toMatchObject({ injectImmediately: true });
    }
  });

  it("waits past a finished load for what the page draws in afterwards", async () => {
    const sold = "https://www.ebay.com/sh/ord/?offset=0";
    fakeBrowser.tabs.create = vi.fn(async () => ({ id: 9 })) as unknown as typeof fakeBrowser.tabs.create;
    let at = robots;
    fakeBrowser.tabs.get = vi.fn(async () => ({ id: 9, status: "complete", url: at })) as unknown as typeof fakeBrowser.tabs.get;
    fakeBrowser.tabs.update = vi.fn(async () => {
      at = sold;
      return { id: 9 };
    }) as unknown as typeof fakeBrowser.tabs.update;
    const probes = [{ parsed: true, found: false, document: 2 }, { parsed: true, found: false, document: 2 }, { parsed: true, found: true, document: 2 }];
    fakeBrowser.scripting.executeScript = vi.fn(async (injection: { func: { name: string } }) => {
      const name = injection.func.name;
      const result = name === "documentOrigin" ? 1
        : name === "pageSettled" ? probes.shift() ?? { parsed: true, found: true, document: 2 }
          : { status: 200, url: sold, contentType: "text/html", body: "<ul><li>row</li></ul>", document: 2 };
      return [{ frameId: 0, result }];
    }) as unknown as typeof fakeBrowser.scripting.executeScript;

    const tab = await new SyncTabs(fakeClock()).acquire(ebayAdapter);
    await tab.page(sold, { until: "ul li" });

    expect(probes).toEqual([]);
  });

  it("waits for the data a page's state holds, not only for the state, before reading it", async () => {
    const sales = "https://poshmark.com/seller-hub/sales";
    fakeBrowser.tabs.create = vi.fn(async () => ({ id: 9 })) as unknown as typeof fakeBrowser.tabs.create;
    let at = poshmarkAdapter.workerUrl;
    fakeBrowser.tabs.get = vi.fn(async () => ({ id: 9, status: "complete", url: at })) as unknown as typeof fakeBrowser.tabs.get;
    fakeBrowser.tabs.update = vi.fn(async () => {
      at = sales;
      return { id: 9 };
    }) as unknown as typeof fakeBrowser.tabs.update;
    const probes = [{ parsed: true, found: false, document: 2 }, { parsed: true, found: true, document: 2 }];
    const asked: unknown[] = [];
    fakeBrowser.scripting.executeScript = vi.fn(async (injection: { func: { name: string }; args?: unknown[] }) => {
      const name = injection.func.name;
      if (name === "pageSettled") {
        asked.push(injection.args);
      }

      const result = name === "documentOrigin" ? 1
        : name === "pageSettled" ? probes.shift() ?? { parsed: true, found: true, document: 2 }
          : { status: 200, url: sales, contentType: "text/html", body: "<div></div>", global: "{}", document: 2 };
      return [{ frameId: 0, result }];
    }) as unknown as typeof fakeBrowser.scripting.executeScript;

    const tab = await new SyncTabs(fakeClock()).acquire(poshmarkAdapter);
    await tab.page(sales, { global: "__INITIAL_STATE__", holds: ["$_order_list", "userSales", "data", "sales_summary"] });

    expect(probes).toEqual([]);
    expect(asked[0]).toEqual(["__INITIAL_STATE__", null, ["$_order_list", "userSales", "data", "sales_summary"]]);
  });

  it("reads an empty page after a while, since what it waits for may never be drawn", async () => {
    const sold = "https://www.ebay.com/sh/ord/?offset=0";
    fakeBrowser.tabs.create = vi.fn(async () => ({ id: 9 })) as unknown as typeof fakeBrowser.tabs.create;
    let at = robots;
    fakeBrowser.tabs.get = vi.fn(async () => ({ id: 9, status: "complete", url: at })) as unknown as typeof fakeBrowser.tabs.get;
    fakeBrowser.tabs.update = vi.fn(async () => {
      at = sold;
      return { id: 9 };
    }) as unknown as typeof fakeBrowser.tabs.update;
    fakeBrowser.scripting.executeScript = vi.fn(async (injection: { func: { name: string } }) => {
      const name = injection.func.name;
      const result = name === "documentOrigin" ? 1
        : name === "pageSettled" ? { parsed: true, found: false, document: 2 }
          : { status: 200, url: sold, contentType: "text/html", body: "<p>Nothing sold yet</p>", document: 2 };
      return [{ frameId: 0, result }];
    }) as unknown as typeof fakeBrowser.scripting.executeScript;
    const clock = fakeClock();

    const tab = await new SyncTabs(clock).acquire(ebayAdapter);
    const page = await tab.page(sold, { until: "ul li" });

    expect(page.body).toContain("Nothing sold yet");
    expect(clock.elapsed()).toBeGreaterThanOrEqual(12_000);
    expect(clock.elapsed()).toBeLessThan(45_000);
  });

  it("never takes the page a reload is leaving for the new one, however settled it looks", async () => {
    // Reloading the same address leaves the old document up until the new one commits.
    const orders = "https://www.ebay.com/sh/ord/?offset=0";
    fakeBrowser.tabs.create = vi.fn(async () => ({ id: 9 })) as unknown as typeof fakeBrowser.tabs.create;
    let status = "complete";
    fakeBrowser.tabs.get = vi.fn(async () => ({ id: 9, status, url: orders })) as unknown as typeof fakeBrowser.tabs.get;
    fakeBrowser.tabs.update = vi.fn(async () => {
      status = "loading";
      // The same address, so only the browser's own event says the reload began.
      await fakeBrowser.tabs.onUpdated.trigger(9, { status: "loading" }, { id: 9, status: "loading", url: orders } as never);
      return { id: 9 };
    }) as unknown as typeof fakeBrowser.tabs.update;
    const probes = [{ parsed: true, found: true, document: 1 }, { parsed: true, found: true, document: 1 }, { parsed: true, found: true, document: 2 }];
    const reads = [1, 2];
    const execute = vi.fn(async (injection: { func: { name: string } }) => {
      const name = injection.func.name;
      const result = name === "documentOrigin" ? 1
        : name === "pageSettled" ? probes.shift() ?? { parsed: true, found: true, document: 2 }
          : { status: 200, url: orders, contentType: "text/html", body: "<html></html>", document: reads.shift() ?? 2 };
      return [{ frameId: 0, result }];
    });
    fakeBrowser.scripting.executeScript = execute as unknown as typeof fakeBrowser.scripting.executeScript;

    const tab = await new SyncTabs(fakeClock()).acquire(ebayAdapter);
    const page = await tab.page(orders);

    expect(page.document).toBe(2);
    expect(probes).toEqual([]);
    expect(reads).toEqual([]);
  });
});

describe("whether a page holds what will be read", () => {
  function onPage(state: unknown, readyState = "complete") {
    vi.stubGlobal("document", { readyState, querySelector: () => null });
    vi.stubGlobal("window", { __INITIAL_STATE__: state });
    vi.stubGlobal("performance", { timeOrigin: 2 });
  }

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const sales = ["$_order_list", "userSales", "data", "sales_summary"];

  it("takes a page as parsed once its state is defined, but not as holding its data until the data is in it", () => {
    onPage({ $_order_list: { userSales: {} } });
    expect(pageSettled("__INITIAL_STATE__", null, sales)).toEqual({ parsed: true, found: false, document: 2 });

    onPage({ $_order_list: { userSales: { data: { sales_summary: [] } } } });
    expect(pageSettled("__INITIAL_STATE__", null, sales)).toEqual({ parsed: true, found: true, document: 2 });
  });

  it("is not parsed while the state is not yet defined", () => {
    onPage(undefined);
    expect(pageSettled("__INITIAL_STATE__", null, sales)).toMatchObject({ parsed: false, found: false });
  });

  it("asks nothing of the state when no data path is given", () => {
    onPage({});
    expect(pageSettled("__INITIAL_STATE__", null, null)).toMatchObject({ parsed: true, found: true });
    expect(pageSettled(null, null, null)).toMatchObject({ parsed: true, found: true });
  });
});

describe("marketplace destination boundaries", () => {
  it("limits page navigation to its own marketplace origin", () => {
    expect(() => assertMarketplaceDestination(ebayAdapter, "https://www.ebay.com/sh/ord/", "page")).not.toThrow();
    expect(() => assertMarketplaceDestination(ebayAdapter, "https://vi.vipr.ebaydesc.com/itmdesc", "page"))
      .toThrowError(/outside its permitted destinations/);
    expect(() => assertMarketplaceDestination(poshmarkAdapter, "https://www.ebay.com/", "page"))
      .toThrowError(/outside its permitted destinations/);
  });

  it("permits eBay description requests but refuses untrusted request targets", () => {
    expect(() => assertMarketplaceDestination(ebayAdapter, "https://vi.vipr.ebaydesc.com/itmdesc?item=123", "request"))
      .not.toThrow();
    const hostile = [
      "https://vi.vipr.ebaydesc.com.attacker.example/collect",
      "https://attacker.example/collect",
      "http://vi.vipr.ebaydesc.com/itmdesc",
      "https://user@vi.vipr.ebaydesc.com/itmdesc",
      "https://www.ebay.com.attacker.example/collect",
      "/relative-path",
    ];
    for (const destination of hostile) {
      expect(() => assertMarketplaceDestination(ebayAdapter, destination, "request")).toThrow(AdapterError);
    }
  });
});
