import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fakeBrowser } from "wxt/testing/fake-browser";
import { ledgerPage, listingsPage, ordersPage, profileJson } from "../test/ebay-fixtures";
import { fakeTabs, servedTab } from "../test/tabs";
import { Agent } from "./agent";
import { ApiError, type AgentApi } from "./api";
import { initialGovernorState } from "./governor";
import {
  activityItem,
  authProblemItem,
  connectionItem,
  governorItem,
  heartbeatItem,
  lastRunsItem,
  pairingItem,
  queuedItem,
  sessionsItem,
} from "./storage";
import type { Heartbeat } from "./types";

const now = new Date("2026-09-18T20:00:00Z");
const account = "3f2b8c1e-0000-4000-8000-000000000001";
const pairing = { agentId: "agent", agentName: "Laptop", organizationName: "Overhaul", token: "hsa_old", pairedAt: "2026-09-18T00:00:00Z" };

const beat: Heartbeat = {
  agentId: "agent",
  agentName: "Laptop",
  organizationName: "Overhaul",
  heartbeatSeconds: 60,
  platforms: [{ platform: "eBay", enabled: true, loops: ["Sales", "Listings"], minSecondsBetweenRequests: 3, maxRequestsPerMinute: 12, maxRequestsPerDay: 1500, historyDays: 90 }],
  accounts: [{ channelAccountId: account, platform: "eBay", displayName: "Overhaul eBay", boundUsername: "seller" }],
  work: [],
  serverTime: now.toISOString(),
};

const ebay = (profile: string | null = "seller") => ({
  "/gh/user_profile": profileJson(profile),
  "/sh/ord/": ordersPage([{ orderId: "26-15152-72360", created: ["Sep 18", "12:48am PDT"], soldFor: "$20.00", total: "$20.00", lines: [{ listingId: "1", title: "Lamp", price: "$20.00" }] }]),
  "/mes/transactionlist": ledgerPage([]),
  "/sh/lst/active": listingsPage([{ id: "377000000001", title: "Lamp", price: 20 }]),
});

function api(heartbeats: (Heartbeat | Error)[] = [beat]) {
  let call = 0;
  return {
    heartbeat: vi.fn(async (_request: unknown) => {
      const next = heartbeats[Math.min(call++, heartbeats.length - 1)]!;
      if (next instanceof Error) {
        throw next;
      }

      return next;
    }),
    startRun: vi.fn(async () => ({ runId: "run-1", leaseExpiresAt: now.toISOString(), cursor: null, from: null })),
    addBatch: vi.fn(async () => ({ created: 1, updated: 0, unchanged: 0 })),
    complete: vi.fn(async () => ({ status: "Succeeded", summary: "1 new order.", created: 1, updated: 0, rejected: 0 })),
    fail: vi.fn(async () => ({ status: "Failed" })),
    reportWorkFailure: vi.fn(async () => undefined),
    disconnect: vi.fn(async () => undefined),
  };
}

function agentWith(calls: ReturnType<typeof api>, tabs = fakeTabs(servedTab(ebay()))) {
  return { agent: new Agent({ api: calls as unknown as AgentApi, tabs, now: () => now, sleep: async () => undefined }), tabs };
}

const signedInSession = { signedIn: true, username: "seller", marketplaceUserId: null, checkedAt: now.toISOString() };

/** Access to eBay alone, as a seller who has only allowed eBay would have. */
function allowEbayOnly() {
  fakeBrowser.permissions.contains = vi.fn(async (permissions: { origins?: string[] }) =>
    (permissions.origins ?? []).every((origin) => origin.includes("ebay.com"))) as typeof fakeBrowser.permissions.contains;
}

describe("checking in", () => {
  beforeEach(() => {
    fakeBrowser.reset();
    fakeBrowser.permissions.contains = vi.fn(async () => false) as typeof fakeBrowser.permissions.contains;
    fakeBrowser.runtime.getManifest = vi.fn(() => ({ manifest_version: 3 as const, name: "HaulScout Sync", version: "0.3.0" }));
  });

  it("does nothing until the browser is connected", async () => {
    const calls = api();
    await agentWith(calls).agent.checkIn();
    expect(calls.heartbeat).not.toHaveBeenCalled();
  });

  it("reports what it can see, and keeps a rotated token", async () => {
    await pairingItem.setValue(pairing);
    const calls = api([{ ...beat, agentName: "Office mini-PC", newToken: "hsa_new" }]);

    await agentWith(calls).agent.checkIn();

    expect(calls.heartbeat).toHaveBeenCalledWith(expect.objectContaining({
      marketplaces: [
        expect.objectContaining({ platform: "eBay", accessGranted: false }),
        expect.objectContaining({ platform: "Vinted", accessGranted: false }),
      ],
    }));
    expect(await pairingItem.getValue()).toMatchObject({ token: "hsa_new", agentName: "Office mini-PC" });
    expect((await heartbeatItem.getValue())?.newToken).toBeNull();
    expect(await connectionItem.getValue()).toMatchObject({ ok: true });
  });

  it("coalesces check-ins that arrive together into one", async () => {
    await pairingItem.setValue(pairing);
    const calls = api();
    const { agent } = agentWith(calls);

    await Promise.all([agent.checkIn(), agent.checkIn(), agent.checkIn()]);

    expect(calls.heartbeat).toHaveBeenCalledTimes(1);
  });

  it("forgets a token HaulScout no longer accepts, and says why", async () => {
    await pairingItem.setValue(pairing);
    await agentWith(api([new ApiError(401, "agent_revoked", "This browser was disconnected from HaulScout.")])).agent.checkIn();

    expect(await pairingItem.getValue()).toBeNull();
    expect(await authProblemItem.getValue()).toMatchObject({ code: "agent_revoked", message: "This browser was disconnected from HaulScout." });
  });

  it("keeps the connection through a network failure, and shows it rather than hiding it", async () => {
    await pairingItem.setValue(pairing);
    await agentWith(api([new ApiError(0, "network", "HaulScout could not be reached (Failed to fetch).")])).agent.checkIn();

    expect(await pairingItem.getValue()).toEqual(pairing);
    expect(await connectionItem.getValue()).toMatchObject({ ok: false, message: "HaulScout could not be reached." });
  });
});

describe("running the work a check-in hands out", () => {
  beforeEach(async () => {
    fakeBrowser.reset();
    allowEbayOnly();
    fakeBrowser.runtime.getManifest = vi.fn(() => ({ manifest_version: 3 as const, name: "HaulScout Sync", version: "0.3.0" }));
    await pairingItem.setValue(pairing);
    await sessionsItem.setValue({ eBay: signedInSession });
  });

  it("runs it all in one sync tab, one piece after another, then lets the tab go", async () => {
    const calls = api([{ ...beat, work: [
      { channelAccountId: account, platform: "eBay", loop: "Sales", trigger: "Requested", scanRequestId: "request-1" },
      { channelAccountId: account, platform: "eBay", loop: "Listings", trigger: "Scheduled" },
    ] }, beat]);
    calls.complete
      .mockResolvedValueOnce({ status: "Succeeded", summary: "1 new order.", created: 1, updated: 0, rejected: 0 })
      .mockResolvedValueOnce({ status: "Succeeded", summary: "No changes — 1 listing checked.", created: 0, updated: 0, rejected: 0 });
    const { agent, tabs } = agentWith(calls);

    await agent.checkIn();

    expect(tabs.acquire).toHaveBeenCalledTimes(1);
    expect(tabs.release).toHaveBeenCalledTimes(1);
    expect(calls.startRun).toHaveBeenCalledTimes(2);
    expect(await lastRunsItem.getValue()).toEqual({
      [`${account}:Sales`]: { at: now.toISOString(), tone: "success", text: "1 new order." },
      [`${account}:Listings`]: { at: now.toISOString(), tone: "success", text: "No changes — 1 listing checked." },
    });
    // A scan someone asked for always says how it went; a quiet scheduled one stays out of the list.
    expect((await activityItem.getValue()).map((entry) => entry.message)).toEqual(["Overhaul eBay sales: 1 new order."]);
  });

  it("tells HaulScout at once when it finds itself signed out, so the work is held back and the portal says why", async () => {
    const calls = api([{ ...beat, work: [{ channelAccountId: account, platform: "eBay", loop: "Sales", trigger: "Requested", scanRequestId: "request-1" }] }, beat]);
    const { agent } = agentWith(calls, fakeTabs(servedTab(ebay(null))));

    await agent.checkIn();

    expect(calls.heartbeat).toHaveBeenCalledTimes(2);
    expect(calls.heartbeat.mock.calls[1]![0]).toMatchObject({ marketplaces: expect.arrayContaining([expect.objectContaining({ platform: "eBay", signedIn: false })]) });
    expect(calls.reportWorkFailure).not.toHaveBeenCalled();
    expect((await lastRunsItem.getValue())[`${account}:Sales`]).toMatchObject({ tone: "warning", text: expect.stringContaining("Signed out of eBay") });
  });

  it("opens no tab for work that cannot run anyway", async () => {
    await governorItem.setValue({ eBay: { ...initialGovernorState(), pausedUntil: now.getTime() + 60_000, pauseReason: "Paused.", pausedFor: "rate-limited" } });
    const calls = api([{ ...beat, work: [{ channelAccountId: account, platform: "eBay", loop: "Sales", trigger: "Scheduled" }] }]);
    const { agent, tabs } = agentWith(calls);

    await agent.checkIn();

    expect(tabs.acquire).not.toHaveBeenCalled();
  });
});

describe("a scan asked for in the popup", () => {
  beforeEach(async () => {
    fakeBrowser.reset();
    allowEbayOnly();
    fakeBrowser.runtime.getManifest = vi.fn(() => ({ manifest_version: 3 as const, name: "HaulScout Sync", version: "0.3.0" }));
    await pairingItem.setValue(pairing);
    await sessionsItem.setValue({ eBay: signedInSession });
    await heartbeatItem.setValue({ ...beat, receivedAt: now.toISOString() });
  });

  it("runs in the sync tab and says how it went, even when nothing changed", async () => {
    const calls = api();
    calls.complete.mockResolvedValueOnce({ status: "Succeeded", summary: "No changes — 1 listing checked.", created: 0, updated: 0, rejected: 0 });
    const { agent, tabs } = agentWith(calls);

    await agent.scanNow(account, "Listings");

    expect(tabs.acquire).toHaveBeenCalledTimes(1);
    expect(calls.startRun).toHaveBeenCalledWith(expect.objectContaining({ loop: "Listings", trigger: "Extension" }));
    expect((await activityItem.getValue())[0]?.message).toBe("Overhaul eBay listings: No changes — 1 listing checked.");
    expect(await queuedItem.getValue()).toEqual([]);
  });

  it("says why it cannot run, without opening a tab, while the marketplace is paused", async () => {
    await governorItem.setValue({ eBay: { ...initialGovernorState(), pausedUntil: now.getTime() + 60_000, pauseReason: "eBay showed a security check.", pausedFor: "challenge" } });
    const { agent, tabs } = agentWith(api());

    await agent.scanNow(account, "Sales");

    expect(tabs.acquire).not.toHaveBeenCalled();
    expect((await lastRunsItem.getValue())[`${account}:Sales`]).toMatchObject({ tone: "warning", text: "eBay showed a security check." });
  });
});

describe("noticing the operator sign in, or clear a security check", () => {
  beforeEach(async () => {
    vi.useFakeTimers();
    fakeBrowser.reset();
    allowEbayOnly();
    fakeBrowser.runtime.getManifest = vi.fn(() => ({ manifest_version: 3 as const, name: "HaulScout Sync", version: "0.3.0" }));
    await pairingItem.setValue(pairing);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("looks again from the operator's own tab once an eBay page loads there while signed out", async () => {
    await sessionsItem.setValue({ eBay: { ...signedInSession, signedIn: false, username: null } });
    const calls = api();
    const tabs = fakeTabs(servedTab(ebay()), servedTab(ebay("seller")));
    const { agent } = agentWith(calls, tabs);

    await agent.noticePage(3, "https://www.ebay.com/");
    // It waits for the pages to settle — a sign-in lands through a redirect or two — then looks.
    await vi.advanceTimersByTimeAsync(2000);
    await vi.waitFor(async () => expect((await sessionsItem.getValue()).eBay).toMatchObject({ signedIn: true, username: "seller" }));

    expect(tabs.borrow).toHaveBeenCalledWith(expect.objectContaining({ platform: "eBay" }), 3);
    expect(tabs.acquire).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(calls.heartbeat).toHaveBeenCalled());
    expect((await activityItem.getValue())[0]?.message).toBe("Signed in to eBay as seller.");
  });

  it("leaves the operator alone on the sign-in form and on the security check itself", async () => {
    await sessionsItem.setValue({ eBay: { ...signedInSession, signedIn: false, username: null } });
    const tabs = fakeTabs(servedTab(ebay()), servedTab(ebay()));
    const { agent } = agentWith(api(), tabs);

    await agent.noticePage(3, "https://signin.ebay.com/signin/");
    await agent.noticePage(3, "https://www.ebay.com/splashui/challenge?ap=1");
    await vi.advanceTimersByTimeAsync(30_000);

    expect(tabs.borrow).not.toHaveBeenCalled();
  });

  it("lifts a security-check pause once the operator has Seller Hub open again", async () => {
    await sessionsItem.setValue({ eBay: signedInSession });
    await governorItem.setValue({ eBay: { ...initialGovernorState(), pausedUntil: now.getTime() + 3_600_000, pauseReason: "eBay showed a security check.", pausedFor: "challenge", strikes: 1 } });
    const { agent } = agentWith(api(), fakeTabs(servedTab(ebay()), servedTab(ebay())));

    await agent.noticePage(3, "https://www.ebay.com/sh/ord/");
    await vi.advanceTimersByTimeAsync(2000);
    await vi.waitFor(async () => expect((await governorItem.getValue()).eBay?.pausedUntil).toBeNull());

    expect((await activityItem.getValue()).map((entry) => entry.message)).toContain("eBay's security check is done. Syncing picks up again.");
  });

  it("ignores pages loading once it is signed in and nothing is paused", async () => {
    await sessionsItem.setValue({ eBay: signedInSession });
    const tabs = fakeTabs(servedTab(ebay()), servedTab(ebay()));
    const { agent } = agentWith(api(), tabs);

    await agent.noticePage(3, "https://www.ebay.com/sh/ord/");
    await vi.advanceTimersByTimeAsync(30_000);

    expect(tabs.borrow).not.toHaveBeenCalled();
  });
});
