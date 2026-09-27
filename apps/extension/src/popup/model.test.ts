import { describe, expect, it } from "vitest";
import { ebayAdapter } from "../adapters/ebay";
import { initialGovernorState } from "../agent/governor";
import type { AgentAccount } from "../agent/types";
import { dueIn, headline, liveRunning, loopRow, marketplaceStatus, scanBlocker, type MarketplaceStatus } from "./model";

const now = new Date("2026-09-19T12:00:00Z");
const session = { signedIn: true, username: "overhaultradingcompany", marketplaceUserId: null, checkedAt: now.toISOString() };
const account: AgentAccount = {
  channelAccountId: "a1",
  platform: "eBay",
  displayName: "overhaultradingco (ebay)",
  boundUsername: "overhaultradingcompany",
  salesEveryMinutes: 240,
  listingsEveryMinutes: null,
  loops: [
    { loop: "Sales", everyMinutes: 240, lastSucceededAt: "2026-09-19T11:55:00Z", lastError: null, nextDueAt: "2026-09-19T15:55:00Z", running: false },
    { loop: "Listings", everyMinutes: null, lastSucceededAt: null, lastError: null, nextDueAt: null, running: false },
  ],
};

const status = (input: Partial<Parameters<typeof marketplaceStatus>[1]>): MarketplaceStatus =>
  marketplaceStatus(ebayAdapter, { policy: undefined, access: true, session, governor: undefined, now: now.getTime(), ...input });

describe("the popup's view of a marketplace", () => {
  it("puts what the person must do first: allow access, then any pause, then signing in", () => {
    expect(status({ access: false }).kind).toBe("no-access");
    expect(status({ governor: { ...initialGovernorState(), pausedUntil: now.getTime() + 1000, pauseReason: "Check.", pausedFor: "challenge" } }))
      .toMatchObject({ kind: "paused", challenge: true });
    expect(status({ session: { ...session, signedIn: false, username: null } }).kind).toBe("signed-out");
    expect(status({ session: undefined }).kind).toBe("checking");
    expect(status({})).toMatchObject({ kind: "signed-in", username: "overhaultradingcompany" });
  });

  it("keeps a scan button off, with the reason, whenever the scan could not run", () => {
    expect(scanBlocker(ebayAdapter, account, status({}))).toBeNull();
    expect(scanBlocker(ebayAdapter, account, status({ session: { ...session, signedIn: false, username: null } }))).toBe("Sign in to eBay first.");
    expect(scanBlocker(ebayAdapter, account, status({ session: { ...session, username: "someone_else" } })))
      .toBe("This browser is signed in as someone_else, not overhaultradingcompany.");
    expect(scanBlocker(ebayAdapter, { ...account, boundUsername: null }, status({}))).toBe("Bind this account in HaulScout first.");
  });
});

describe("an account's loops in the popup", () => {
  it("shows this browser's own last result when it is the newest, with what the scan found", () => {
    const row = loopRow(account, "Sales", { at: "2026-09-19T11:59:30Z", tone: "success", text: "3 updated orders." }, null, [], now);
    expect(row).toMatchObject({ cadence: "every 4 h", next: "next in 4 h", state: "idle", last: { tone: "success", text: "3 updated orders · just now" } });
  });

  it("falls back to HaulScout's record, and says when nothing has run yet", () => {
    expect(loopRow(account, "Sales", undefined, null, [], now).last).toEqual({ tone: "muted", text: "Last synced 5 min ago" });
    expect(loopRow(account, "Listings", undefined, null, [], now)).toMatchObject({ cadence: "schedule off", next: null, last: { text: "Not synced yet" } });
  });

  it("shows a scan waiting its turn, and one under way", () => {
    expect(loopRow(account, "Sales", undefined, null, ["a1:Sales"], now).state).toBe("queued");
    expect(loopRow(account, "Sales", undefined, { label: "Reading", key: "a1:Sales", startedAt: now.toISOString(), pages: 2 }, [], now).state).toBe("running");
  });
});

describe("the headline", () => {
  const ready = [{ adapter: ebayAdapter, status: status({}) }];

  it("says HaulScout is out of reach before anything else", () => {
    expect(headline({ connection: { ok: false, message: "HaulScout could not be reached." }, running: null, marketplaces: ready, accounts: [account], now }))
      .toMatchObject({ tone: "error", title: "Can't reach HaulScout" });
  });

  it("follows work under way, page by page", () => {
    expect(headline({ connection: null, running: { label: "Reading overhaultradingco (ebay) sales", key: "a1:Sales", startedAt: now.toISOString(), pages: 3 }, marketplaces: ready, accounts: [account], now }))
      .toEqual({ tone: "busy", title: "Reading overhaultradingco (ebay) sales…", text: "3 pages read so far." });
  });

  it("asks the person for what only they can do", () => {
    expect(headline({ connection: null, running: null, marketplaces: [{ adapter: ebayAdapter, status: status({ session: { ...session, signedIn: false, username: null } }) }], accounts: [account], now }))
      .toMatchObject({ tone: "warning", title: "Sign in to eBay" });
  });

  it("says all is well, and when the next scan runs", () => {
    expect(headline({ connection: { ok: true, message: null }, running: null, marketplaces: ready, accounts: [account], now }))
      .toEqual({ tone: "ok", title: "All set", text: "Next: overhaultradingco (ebay) sales in 4 h." });
  });
});

describe("relative times", () => {
  it("rounds a wait the way a person says it", () => {
    expect(dueIn("2026-09-19T12:00:30Z", now)).toBe("now");
    expect(dueIn("2026-09-19T12:12:00Z", now)).toBe("in 12 min");
    expect(dueIn("2026-09-19T15:55:00Z", now)).toBe("in 4 h");
  });
});

describe("what the popup shows as running", () => {
  const running = { label: "Checking the Poshmark sign-in", key: null, startedAt: "2026-09-27T10:00:00Z", pages: 0 };
  const at = Date.parse("2026-09-27T10:01:00Z");

  it("shows work while the background keeps saying it is alive", () => {
    expect(liveRunning(running, "2026-09-27T10:00:45Z", at)).toBe(running);
  });

  it("drops a label a stopped background left behind, instead of checking forever", () => {
    expect(liveRunning(running, "2026-09-27T09:58:00Z", at)).toBeNull();
    expect(liveRunning(running, null, at)).toBeNull();
  });
});
