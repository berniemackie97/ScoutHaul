import { beforeEach, describe, expect, it, vi } from "vitest";
import { fakeBrowser } from "wxt/testing/fake-browser";
import { ledgerPage, listingsPage, ordersPage, profileJson, type FixtureListing } from "../test/ebay-fixtures";
import { AdapterError, type MarketplaceContext, type PageResponse } from "../adapters/types";
import { ApiError, type AgentApi } from "./api";
import { initialGovernorState } from "./governor";
import { keepingLease, preflight, runWork, type RunnerDeps } from "./runner";
import { activityItem, governorItem, sessionsItem } from "./storage";
import type { MarketplaceTab } from "./transport";
import { servedTab, type Route } from "../test/tabs";
import { orderList, orderRow, transaction } from "../test/vinted-fixtures";
import type { Heartbeat, Work } from "./types";

const account = "3f2b8c1e-0000-4000-8000-000000000001";
const now = new Date("2026-09-18T20:00:00Z");

const heartbeat: Heartbeat = {
  agentId: "agent",
  agentName: "Office mini-PC",
  organizationName: "Overhaul Trading Company",
  heartbeatSeconds: 60,
  platforms: [{ platform: "eBay", enabled: true, loops: ["Sales", "Listings"], minSecondsBetweenRequests: 3, maxRequestsPerMinute: 12, maxRequestsPerDay: 1500, historyDays: 90 }],
  accounts: [{ channelAccountId: account, platform: "eBay", displayName: "Overhaul eBay", boundUsername: "seller" }],
  work: [],
  serverTime: now.toISOString(),
};

const sales: Work = { channelAccountId: account, platform: "eBay", loop: "Sales", trigger: "Scheduled" };
const listingsWork: Work = { ...sales, loop: "Listings" };

function page(body: string, url: string, status = 200): PageResponse {
  return { status, url, contentType: "text/html", body };
}

function harness(routes: Record<string, Route>) {
  const api = {
    startRun: vi.fn(async () => ({ runId: "run-1", leaseExpiresAt: now.toISOString(), cursor: null, from: null })),
    addBatch: vi.fn(async (_runId: string, _batch: { orders?: unknown[]; listings?: unknown[] }) => ({ created: 1, updated: 0, unchanged: 0 })),
    complete: vi.fn(async () => ({ status: "Succeeded", summary: "1 new order.", created: 1, updated: 0, rejected: 0 })),
    fail: vi.fn(async () => ({ status: "Failed" })),
    reportWorkFailure: vi.fn(async () => undefined),
  };
  const deps: RunnerDeps = { api: api as unknown as AgentApi, now: () => now, sleep: async () => undefined };
  return { api, deps, tab: servedTab(routes) };
}

const signedIn = { "/gh/user_profile": profileJson("seller") };

describe("running one piece of work", () => {
  beforeEach(() => {
    fakeBrowser.reset();
    fakeBrowser.permissions.contains = vi.fn(async () => true) as typeof fakeBrowser.permissions.contains;
  });

  it("checks who is signed in, takes the run, delivers the sales and says what came of it", async () => {
    const { api, deps, tab } = harness({
      ...signedIn,
      "/sh/ord/": ordersPage([{ orderId: "26-15152-72360", created: ["Sep 18", "12:48am PDT"], soldFor: "$20.00", total: "$20.00", lines: [{ listingId: "1", title: "Lamp", price: "$20.00" }] }]),
      "/mes/transactionlist": ledgerPage([]),
    });

    const result = await runWork(sales, heartbeat, tab, deps);

    expect(result).toEqual({ status: "done", text: "1 new order.", changed: true, issues: false });
    expect(api.startRun).toHaveBeenCalledWith(expect.objectContaining({ channelAccountId: account, loop: "Sales", signedInAs: "seller" }));
    expect(api.addBatch.mock.calls[0]![1]).toMatchObject({ orders: [expect.objectContaining({ externalOrderId: "26-15152-72360", feesPending: true })] });
    expect(api.complete).toHaveBeenCalledWith("run-1", expect.objectContaining({ cursor: "2026-09-18T07:48:00.000Z" }));
    expect((await governorItem.getValue()).eBay?.dayCount).toBe(3);
    expect((await sessionsItem.getValue()).eBay).toMatchObject({ signedIn: true, username: "seller" });
    expect((await activityItem.getValue())[0]?.message).toBe("Signed in to eBay as seller.");
  });

  it("leaves a loop another browser holds, without calling it a problem", async () => {
    const { api, deps, tab } = harness(signedIn);
    api.startRun.mockRejectedValueOnce(new ApiError(409, "sync_lease_held", "Another browser is already running it."));

    expect(await runWork(sales, heartbeat, tab, deps)).toEqual({ status: "skipped" });
    expect(api.fail).not.toHaveBeenCalled();
  });

  it("fails the run with the reason when eBay's page changed, and pauses eBay", async () => {
    const { api, deps, tab } = harness({ ...signedIn, "/sh/ord/": `<html>${"a redesigned seller page ".repeat(8000)}</html>` });

    const result = await runWork(sales, heartbeat, tab, deps);

    expect(api.fail).toHaveBeenCalledWith("run-1", "SiteChanged", expect.stringContaining("orders"));
    expect(result).toMatchObject({ status: "failed", kind: "SiteChanged", text: expect.stringContaining("eBay's pages changed") });
    expect((await governorItem.getValue()).eBay!.pausedUntil).toBe(now.getTime() + 6 * 60 * 60 * 1000);
  });

  it("starts nothing when the browser is signed out, and leaves the telling to the heartbeat", async () => {
    const { api, deps, tab } = harness({ "/gh/user_profile": profileJson(null) });

    const result = await runWork({ ...sales, trigger: "Requested", scanRequestId: "request-1" }, heartbeat, tab, deps);

    expect(result).toMatchObject({ status: "failed", kind: "SignedOut" });
    expect(api.startRun).not.toHaveBeenCalled();
    expect(api.reportWorkFailure).not.toHaveBeenCalled();
    expect((await sessionsItem.getValue()).eBay).toMatchObject({ signedIn: false });
    expect((await governorItem.getValue()).eBay?.strikes ?? 0).toBe(0);
  });

  it("starts nothing when the browser is signed in as someone else, and names both", async () => {
    const { api, deps, tab } = harness({ "/gh/user_profile": profileJson("someone_else") });

    const result = await runWork(sales, heartbeat, tab, deps);

    expect(result).toMatchObject({ status: "failed", kind: "WrongAccount" });
    expect(result.status === "failed" && result.text).toContain("signed in to eBay as someone_else, but Overhaul eBay syncs seller");
    expect(api.startRun).not.toHaveBeenCalled();
  });

  it("reports work it could not start for its own reasons, so a scan someone asked for says why", async () => {
    const { api, deps } = harness({});
    const closed: MarketplaceTab = {
      page: async () => page("", ""),
      request: async () => {
        throw new AdapterError("Interrupted", "The sync tab was closed.");
      },
    };

    const result = await runWork({ ...sales, trigger: "Requested", scanRequestId: "request-1" }, heartbeat, closed, deps);

    expect(result).toMatchObject({ status: "failed", kind: "Interrupted" });
    expect(api.reportWorkFailure).toHaveBeenCalledWith({ channelAccountId: account, loop: "Sales", kind: "Interrupted", message: "The sync tab was closed.", scanRequestId: "request-1" });
  });

  it("says which page failed to load, so a slow site is told apart from a broken read", async () => {
    const { api, deps } = harness({});
    const slow: MarketplaceTab = {
      page: async () => {
        throw new AdapterError("Network", "The page took too long to load.");
      },
      request: async () => {
        throw new AdapterError("Network", "The page took too long to load.");
      },
    };

    const result = await runWork({ ...sales, trigger: "Requested", scanRequestId: "request-1" }, heartbeat, slow, deps);

    expect(result).toMatchObject({ status: "failed", kind: "Network" });
    expect(result.status === "failed" && result.text).toContain("took too long to load");
    expect(api.reportWorkFailure).toHaveBeenCalledWith(expect.objectContaining({ kind: "Network", message: "The page took too long to load." }));
  });

  it("does not hold a scan asked for in the popup against the account's schedule", async () => {
    const { api, deps } = harness({});
    const closed: MarketplaceTab = {
      page: async () => page("", ""),
      request: async () => {
        throw new AdapterError("Interrupted", "The sync tab was closed.");
      },
    };

    await runWork({ ...sales, trigger: "Extension" }, heartbeat, closed, deps);

    expect(api.reportWorkFailure).not.toHaveBeenCalled();
  });

  it("delivers the sales a marketplace cancelled beside the sales it recorded", async () => {
    const vinted: Heartbeat = {
      ...heartbeat,
      platforms: [{ platform: "Vinted", enabled: true, loops: ["Sales", "Listings"], minSecondsBetweenRequests: 4, maxRequestsPerMinute: 10, maxRequestsPerDay: 800, historyDays: 3650 }],
      accounts: [{ channelAccountId: account, platform: "Vinted", displayName: "Overhaul Vinted", boundUsername: "test_seller" }],
    };
    const rows = [orderRow(1000, "2026-09-18T10:00:00-06:00", "8.5"), orderRow(1001, "2026-09-17T10:00:00-06:00", "6.0", "cancelled"), orderRow(1002, "2026-09-16T10:00:00-06:00", "4.0", "open")];
    const { api, deps, tab } = harness({
      "/api/v2/users/current": JSON.stringify({ user: { id: 5550001, login: "test_seller" } }),
      "/api/v2/my_orders": JSON.stringify(orderList(rows, 1, 1)),
      "/api/v2/transactions/1000": JSON.stringify(transaction(1000, { price: "8.5" })),
      "/api/v2/transactions/1002": JSON.stringify(transaction(1002, { price: "4.0" })),
    });

    await runWork({ ...sales, platform: "Vinted" }, vinted, tab, deps);

    expect(api.addBatch).toHaveBeenCalledTimes(1);
    expect(api.addBatch.mock.calls[0]![1]).toMatchObject({
      orders: [expect.objectContaining({ externalOrderId: "1000", payoutAmount: 8.5 }), expect.objectContaining({ externalOrderId: "1002", payoutAmount: null })],
      cancelledOrderIds: ["1001"],
    });
  });

  it("delivers a large snapshot in batches, and marks it complete only when everything was read", async () => {
    const listings: FixtureListing[] = Array.from({ length: 1200 }, (_, index) => ({ id: String(377000000000 + index), title: `Item ${index}`, price: 10 }));
    const { api, deps, tab } = harness({
      ...signedIn,
      "/sh/lst/active": (url) => {
        const offset = Number(url.searchParams.get("offset"));
        return listingsPage(listings.slice(offset, offset + Number(url.searchParams.get("limit"))), listings.length);
      },
    });

    await runWork(listingsWork, heartbeat, tab, deps);

    expect(api.addBatch.mock.calls.map((call) => call[1].listings?.length)).toEqual([500, 500, 200]);
    expect(api.complete).toHaveBeenCalledWith("run-1", { completeSnapshot: true });
  });
});

describe("what stops work before a tab is opened", () => {
  beforeEach(() => {
    fakeBrowser.reset();
    fakeBrowser.permissions.contains = vi.fn(async () => true) as typeof fakeBrowser.permissions.contains;
  });

  it("lets ready work through", async () => {
    expect(await preflight(sales, heartbeat, now)).toBeNull();
  });

  it("holds work back while the marketplace is paused, and says why", async () => {
    await governorItem.setValue({ eBay: { ...initialGovernorState(), pausedUntil: now.getTime() + 60_000, pauseReason: "eBay showed a security check.", pausedFor: "challenge" } });
    expect(await preflight(sales, heartbeat, now)).toEqual({ status: "failed", kind: "Challenge", text: "eBay showed a security check." });
  });

  it("holds work back when sync is switched off, access is missing, or the account is not bound", async () => {
    expect(await preflight(sales, { ...heartbeat, platforms: [{ ...heartbeat.platforms![0]!, enabled: false, disabledReason: "Paused by HaulScout." }] }, now))
      .toMatchObject({ status: "failed", text: "Paused by HaulScout." });
    expect(await preflight(sales, { ...heartbeat, accounts: [{ ...heartbeat.accounts![0]!, boundUsername: null }] }, now))
      .toMatchObject({ status: "failed", text: "Bind Overhaul eBay to its eBay seller account in HaulScout first." });

    fakeBrowser.permissions.contains = vi.fn(async () => false) as typeof fakeBrowser.permissions.contains;
    expect(await preflight(sales, heartbeat, now)).toMatchObject({ status: "failed", kind: "NoAccess" });
  });
});

describe("a run's lease, while it reads", () => {
  const leaseMs = 5 * 60 * 1000;

  function reading() {
    let at = now.getTime();
    const addBatch = vi.fn(async (_runId: string, _batch: object) => ({ created: 0, updated: 0, unchanged: 0 }));
    const deps: RunnerDeps = { api: { addBatch } as unknown as AgentApi, now: () => new Date(at), sleep: async () => undefined };
    const inner: MarketplaceContext = {
      now: () => new Date(at),
      page: vi.fn(async (url: string) => page("<p>ok</p>", url)),
      request: vi.fn(async (url: string) => page("{}", url)),
      memory: { get: async () => null, set: async () => undefined } as unknown as MarketplaceContext["memory"],
    };
    const context = keepingLease(inner, "run-1", new Date(now.getTime() + leaseMs).toISOString(), deps);
    return { addBatch, context, inner, pass: (ms: number) => void (at += ms) };
  }

  it("is renewed with an empty batch once half of it has gone, and not before", async () => {
    const { addBatch, context, pass } = reading();

    pass(leaseMs / 2 - 1000);
    await context.page("https://www.depop.com/sellinghub/sold-items/1/");
    expect(addBatch).not.toHaveBeenCalled();

    pass(1000);
    await context.page("https://www.depop.com/sellinghub/sold-items/2/");
    await context.request("https://www.depop.com/api/");
    expect(addBatch.mock.calls).toEqual([["run-1", {}]]);

    pass(leaseMs / 2);
    await context.page("https://www.depop.com/sellinghub/sold-items/3/");
    expect(addBatch).toHaveBeenCalledTimes(2);
  });

  it("carries on reading when HaulScout does not answer, and tries again on the next page", async () => {
    const { addBatch, context, inner, pass } = reading();
    addBatch.mockRejectedValueOnce(new ApiError(0, "network", "HaulScout could not be reached."));

    pass(leaseMs / 2);
    await context.page("https://www.depop.com/sellinghub/sold-items/1/");
    await context.page("https://www.depop.com/sellinghub/sold-items/2/");

    expect(inner.page).toHaveBeenCalledTimes(2);
    expect(addBatch).toHaveBeenCalledTimes(2);
  });

  it("stops the read when the lease has gone to another browser", async () => {
    const { addBatch, context, inner, pass } = reading();
    addBatch.mockRejectedValueOnce(new ApiError(409, "sync_lease_lost", "Another browser took this over."));

    pass(leaseMs / 2);
    await expect(context.page("https://www.depop.com/sellinghub/sold-items/1/")).rejects.toMatchObject({ code: "sync_lease_lost" });
    expect(inner.page).not.toHaveBeenCalled();
  });
});
