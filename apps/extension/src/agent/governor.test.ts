import { describe, expect, it } from "vitest";
import { dayKey, initialGovernorState, nextSlot, recordOutcome, recordRequest, resume, type GovernorPolicy } from "./governor";

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
});
