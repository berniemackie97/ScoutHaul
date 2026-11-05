import { describe, expect, it } from "vitest";
import {
  afterUpdate,
  checkCleared,
  checkSlot,
  dayKey,
  holdOf,
  initialGovernorState,
  maxChecksPerHour,
  nextSlot,
  quiet,
  recordCheck,
  recordOutcome,
  recordRequest,
  resume,
  type GovernorPolicy,
} from "./governor";

const policy: GovernorPolicy = { minSecondsBetweenRequests: 3, maxRequestsPerMinute: 4, maxRequestsPerDay: 10 };
const start = new Date(2026, 8, 18, 12, 0, 0).getTime();

describe("the safety governor", () => {
  it("spaces requests out, with jitter on top of the minimum gap", () => {
    let state = recordRequest(initialGovernorState(), start);
    expect(nextSlot(state, policy, start + 1000, () => 0)).toEqual({ allowed: true, waitMs: 2000 });
    expect(nextSlot(state, policy, start + 1000, () => 1)).toEqual({ allowed: true, waitMs: 3500 });
    state = recordRequest(state, start + 5000);
    expect(nextSlot(state, policy, start + 9000, () => 0)).toEqual({ allowed: true, waitMs: 0 });
  });

  it("holds the per-minute cap by waiting for the oldest request to age out", () => {
    let state = initialGovernorState();
    for (const at of [0, 10_000, 20_000, 30_000]) {
      state = recordRequest(state, start + at);
    }

    const slot = nextSlot(state, policy, start + 40_000, () => 0);
    expect(slot).toEqual({ allowed: true, waitMs: 20_000 });
  });

  it("stops for the day at the daily cap, and starts again tomorrow", () => {
    let state = initialGovernorState();
    for (let index = 0; index < policy.maxRequestsPerDay; index++) {
      state = recordRequest(state, start - 3 * 60 * 60 * 1000 + index * 60_000);
    }

    const refused = nextSlot(state, policy, start, () => 0);
    expect(refused.allowed).toBe(false);
    expect(nextSlot(state, policy, start + 13 * 60 * 60 * 1000, () => 0).allowed).toBe(true);
    expect(dayKey(start)).toBe("2026-09-18");
  });

  it("pauses when the marketplace pushes back, longer each time it does", () => {
    const first = recordOutcome(initialGovernorState(), "rate-limited", start, "eBay");
    expect(first.pausedUntil).toBe(start + 15 * 60_000);
    const second = recordOutcome(first, "rate-limited", start, "eBay");
    expect(second.pausedUntil).toBe(start + 30 * 60_000);

    const refused = nextSlot(second, policy, start + 60_000);
    expect(refused).toMatchObject({ allowed: false, reason: "eBay asked for a slower pace, so the extension is holding off." });
    expect(recordOutcome(second, "ok", start, "eBay").strikes).toBe(0);
  });

  it("pauses for a security check and for a changed site, and a person can resume", () => {
    const challenged = recordOutcome(initialGovernorState(), "challenge", start, "eBay");
    expect(challenged.pauseReason).toContain("security check");
    const changed = recordOutcome(initialGovernorState(), "site-changed", start, "eBay");
    expect(changed.pausedUntil).toBe(start + 6 * 60 * 60_000);
    expect(nextSlot(resume(changed), policy, start + 1000).allowed).toBe(true);
  });

  it("pauses only after three failures in a row", () => {
    let state = initialGovernorState();
    state = recordOutcome(state, "failed", start, "eBay");
    state = recordOutcome(state, "failed", start, "eBay");
    expect(state.pausedUntil).toBeNull();
    state = recordOutcome(state, "failed", start, "eBay");
    expect(state.pausedUntil).toBe(start + 30 * 60_000);
  });

  it("lifts a changed-site pause when a new build is installed, and no other", () => {
    const now = Date.parse("2026-09-26T20:00:00Z");
    const changed = recordOutcome(initialGovernorState(), "site-changed", now, "Poshmark");
    const challenged = recordOutcome(initialGovernorState(), "challenge", now, "Depop");

    expect(afterUpdate(changed).pausedUntil).toBeNull();
    expect(afterUpdate(challenged)).toEqual(challenged);
  });

  it("holds a security check off longer each time one comes back, until a scan goes through", () => {
    const hour = 60 * 60_000;
    let state = recordOutcome(initialGovernorState(), "challenge", start, "Vinted");
    expect(state.pausedUntil).toBe(start + hour);
    state = recordOutcome(resume(state), "challenge", start, "Vinted");
    expect(state.pausedUntil).toBe(start + 4 * hour);
    state = recordOutcome(state, "challenge", start, "Vinted");
    state = recordOutcome(state, "challenge", start, "Vinted");
    state = recordOutcome(state, "challenge", start, "Vinted");
    expect(state.pausedUntil).toBe(start + 24 * hour);

    expect(recordOutcome(recordOutcome(state, "ok", start, "Vinted"), "challenge", start, "Vinted").pausedUntil).toBe(start + hour);
  });

  it("lifts a cleared security check into a few quiet minutes, not straight into requests", () => {
    const challenged = recordOutcome(initialGovernorState(), "challenge", start, "Vinted");
    const cleared = checkCleared(challenged, start, "Vinted");

    expect(cleared.pausedUntil).toBeNull();
    expect(cleared.challenges).toBe(1);
    expect(nextSlot(cleared, policy, start + 60_000)).toMatchObject({ allowed: false, until: start + 3 * 60_000 });
    expect(nextSlot(cleared, policy, start + 3 * 60_000).allowed).toBe(true);
    expect(holdOf(cleared, start)).toMatchObject({ until: start + 3 * 60_000 });
  });

  it("keeps the longest quiet spell asked for, and a person resuming ends it", () => {
    const held = quiet(quiet(initialGovernorState(), start + 120_000, "Signing in."), start + 30_000, "Settling.");

    expect(nextSlot(held, policy, start)).toEqual({ allowed: false, until: start + 120_000, reason: "Signing in." });
    expect(nextSlot(resume(held), policy, start).allowed).toBe(true);
  });

  it("checks a sign-in only so often an hour, and waits longer after each check that ended in trouble", () => {
    let state = recordCheck(initialGovernorState(), start, false);
    expect(checkSlot(state, start + 60_000)).toEqual({ allowed: false, until: start + 2 * 60_000 });
    expect(checkSlot(state, start + 60_000, true)).toEqual({ allowed: true });

    state = recordCheck(state, start + 2 * 60_000, false);
    expect(checkSlot(state, start + 9 * 60_000)).toEqual({ allowed: false, until: start + 10 * 60_000 });

    state = recordCheck(state, start + 10 * 60_000, true);
    expect(checkSlot(state, start + 10 * 60_000 + 1).allowed).toBe(true);
    state = recordCheck(state, start + 11 * 60_000, true);
    expect(state.checks).toHaveLength(maxChecksPerHour);
    // The hourly budget holds even when a person asks.
    expect(checkSlot(state, start + 12 * 60_000, true)).toEqual({ allowed: false, until: start + 60 * 60_000 });
    expect(checkSlot(state, start + 60 * 60_000 + 1, true).allowed).toBe(true);
  });
});
