import { beforeEach, describe, expect, it, vi } from "vitest";
import { fakeBrowser } from "wxt/testing/fake-browser";
import { ebayAdapter } from "../adapters/ebay";
import { AdapterError } from "../adapters/types";
import { judgeTab, SyncTabs, waitForPage, type Clock, type TabLook } from "./transport";

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
    await borrowed!.page("https://www.ebay.com/sh/lst/active");

    expect(execute).toHaveBeenCalledWith(expect.objectContaining({ target: { tabId: 6 }, world: "MAIN" }));
    // The operator's tab is read as it stands: a sign-in check never takes it somewhere else.
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
    const execute = vi.fn(async (injection: { func: { name: string } }) => [
      { frameId: 0, result: injection.func.name === "pageSettled" ? true : { status: 200, url: orders, contentType: "text/html", body: "<html></html>" } },
    ]);
    fakeBrowser.scripting.executeScript = execute as unknown as typeof fakeBrowser.scripting.executeScript;
    const clock = fakeClock();

    const tab = await new SyncTabs(clock).acquire(ebayAdapter);
    const page = await tab.page(orders, "__INITIAL_STATE__");

    expect(page.url).toBe(orders);
    expect(clock.elapsed()).toBeLessThan(1000);
    expect(execute).toHaveBeenCalledWith(expect.objectContaining({ world: "MAIN", args: ["__INITIAL_STATE__"], injectImmediately: true }));
    for (const [injection] of execute.mock.calls) {
      expect(injection).toMatchObject({ injectImmediately: true });
    }
  });
});
