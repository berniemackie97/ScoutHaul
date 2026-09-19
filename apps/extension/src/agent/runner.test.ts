import { beforeEach, describe, expect, it, vi } from "vitest";
import { fakeBrowser } from "wxt/testing/fake-browser";
import { ledgerPage, listingsPage, ordersPage, profileJson, type FixtureListing } from "../test/ebay-fixtures";
import type { PageResponse } from "../adapters/types";
import { ApiError, type AgentApi } from "./api";
import { initialGovernorState } from "./governor";
import { runWork, type RunnerDeps } from "./runner";
import { activityItem, governorItem, sessionsItem } from "./storage";
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

type Route = string | ((url: URL) => string);

function harness(routes: Record<string, Route>) {
  const opened: string[] = [];
  const closed = vi.fn();
  const api = {
    startRun: vi.fn(async () => ({ runId: "run-1", leaseExpiresAt: now.toISOString(), cursor: null, from: null })),
    addBatch: vi.fn(async (_runId: string, _batch: { orders?: unknown[]; listings?: unknown[] }) => ({ created: 1, updated: 0, unchanged: 0 })),
    complete: vi.fn(async () => ({ status: "Succeeded", summary: "1 new order.", created: 1, updated: 0, rejected: 0 })),
    fail: vi.fn(async () => ({ status: "Failed" })),
  };
  const deps: RunnerDeps = {
    api: api as unknown as AgentApi,
    now: () => now,
    sleep: async () => undefined,
    async openTab(workerUrl: string) {
      opened.push(workerUrl);
      const serve = async (url: string) => {
        const parsed = new URL(url);
        const route = routes[parsed.pathname];
        return route === undefined ? page("", url, 404) : page(typeof route === "string" ? route : route(parsed), url);
      };
      return { page: serve, request: serve, close: closed };
    },
  };
  return { api, deps, opened, closed };
}

const signedIn = { "/gh/user_profile": profileJson("seller") };

describe("running one piece of work", () => {
  beforeEach(() => {
    fakeBrowser.reset();
    fakeBrowser.permissions.contains = vi.fn(async () => true) as typeof fakeBrowser.permissions.contains;
  });

  it("checks who is signed in, takes the run, delivers the sales and closes the tab", async () => {
    const { api, deps, opened, closed } = harness({
      ...signedIn,
      "/sh/ord/": ordersPage([{ orderId: "26-15152-72360", created: ["Sep 18", "12:48am PDT"], soldFor: "$20.00", total: "$20.00", lines: [{ listingId: "1", title: "Lamp", price: "$20.00" }] }]),
      "/mes/transactionlist": ledgerPage([]),
    });

    await runWork(sales, heartbeat, deps);

    expect(opened).toEqual(["https://www.ebay.com/robots.txt"]);
    expect(api.startRun).toHaveBeenCalledWith(expect.objectContaining({ channelAccountId: account, loop: "Sales", signedInAs: "seller" }));
    expect(api.addBatch).toHaveBeenCalledTimes(1);
    expect(api.addBatch.mock.calls[0]![1]).toMatchObject({ orders: [expect.objectContaining({ externalOrderId: "26-15152-72360", feesPending: true })] });
    expect(api.complete).toHaveBeenCalledWith("run-1", expect.objectContaining({ cursor: "2026-09-18T07:48:00.000Z" }));
    expect(closed).toHaveBeenCalled();
    expect((await governorItem.getValue()).eBay?.dayCount).toBe(3);
    expect((await sessionsItem.getValue()).eBay).toMatchObject({ signedIn: true, username: "seller" });
    expect((await activityItem.getValue())[0]).toMatchObject({ tone: "success", message: "Overhaul eBay sales: 1 new order." });
  });

  it("leaves a loop another browser holds, without calling it a problem", async () => {
    const { api, deps, closed } = harness(signedIn);
    api.startRun.mockRejectedValueOnce(new ApiError(409, "sync_lease_held", "Another browser is already running it."));

    await runWork(sales, heartbeat, deps);

    expect(api.fail).not.toHaveBeenCalled();
    expect(closed).toHaveBeenCalled();
    expect(await activityItem.getValue()).toEqual([]);
  });

  it("fails the run with the reason when eBay's page changed, and pauses eBay", async () => {
    const { api, deps } = harness({ ...signedIn, "/sh/ord/": `<html>${"a redesigned seller page ".repeat(8000)}</html>` });

    await runWork(sales, heartbeat, deps);

    expect(api.fail).toHaveBeenCalledWith("run-1", "SiteChanged", expect.stringContaining("orders"));
    const governor = (await governorItem.getValue()).eBay!;
    expect(governor.pausedUntil).toBe(now.getTime() + 6 * 60 * 60 * 1000);
  });

  it("does not start a run when the browser is signed out", async () => {
    const { api, deps } = harness({ "/gh/user_profile": profileJson(null) });

    await runWork(sales, heartbeat, deps);

    expect(api.startRun).not.toHaveBeenCalled();
    expect((await activityItem.getValue())[0]?.message).toBe("Sign in to eBay in this browser to sync Overhaul eBay.");
  });

  it("delivers a large snapshot in batches, and marks it complete only when everything was read", async () => {
    const listings: FixtureListing[] = Array.from({ length: 1200 }, (_, index) => ({ id: String(377000000000 + index), title: `Item ${index}`, price: 10 }));
    const { api, deps } = harness({
      ...signedIn,
      "/sh/lst/active": (url) => {
        const offset = Number(url.searchParams.get("offset"));
        return listingsPage(listings.slice(offset, offset + Number(url.searchParams.get("limit"))), listings.length);
      },
    });

    await runWork(listingsWork, heartbeat, deps);

    expect(api.addBatch.mock.calls.map((call) => call[1].listings?.length)).toEqual([500, 500, 200]);
    expect(api.complete).toHaveBeenCalledWith("run-1", { completeSnapshot: true });
  });

  it("opens nothing while the marketplace is paused, or when sync for it is switched off", async () => {
    const { deps, opened } = harness(signedIn);
    await governorItem.setValue({ eBay: { ...initialGovernorState(), pausedUntil: now.getTime() + 60_000, pauseReason: "Paused." } });
    await runWork(sales, heartbeat, deps);

    await governorItem.setValue({});
    await runWork(sales, { ...heartbeat, platforms: [{ ...heartbeat.platforms![0]!, enabled: false }] }, deps);

    expect(opened).toEqual([]);
  });
});
