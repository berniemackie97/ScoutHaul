import { beforeEach, describe, expect, it } from "vitest";
import { fakeBrowser } from "wxt/testing/fake-browser";
import { afterUpdate, recordOutcome, recordRequest } from "./governor";
import { governorItem, updateGovernor, updateGovernors } from "./storage";

describe("the governor's record", () => {
  beforeEach(() => fakeBrowser.reset());

  it("keeps every change made at once, so a request counted is never lost to another write", async () => {
    const now = Date.parse("2026-09-26T20:00:00Z");

    await Promise.all([
      updateGovernor("eBay", (state) => recordRequest(state, now)),
      updateGovernor("Poshmark", (state) => recordOutcome(state, "site-changed", now, "Poshmark")),
      updateGovernors((states) => Object.fromEntries(Object.entries(states).map(([platform, state]) => [platform, afterUpdate(state)]))),
      updateGovernor("eBay", (state) => recordRequest(state, now + 1000)),
    ]);

    const states = await governorItem.getValue();
    expect(states.eBay!.recent).toHaveLength(2);
    expect(states.Poshmark!.pausedUntil).toBeNull();
  });
});
