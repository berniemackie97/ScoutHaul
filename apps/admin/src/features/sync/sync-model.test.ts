import { describe, expect, it } from "vitest";
import { ago, bindableUsernames, cadenceOptions, dueIn, formatCode, healthBadge, loopSummary, requestStatus } from "./sync-model";

const now = new Date("2026-09-18T20:00:00Z");
const minutesAgo = (minutes: number) => new Date(now.getTime() - minutes * 60_000).toISOString();

describe("sync model", () => {
  it("offers only cadences the marketplace allows, marking the suggested one", () => {
    const options = cadenceOptions("Sales", { minSalesMinutes: 5, suggestedSalesMinutes: 10, minListingsMinutes: 60, suggestedListingsMinutes: 240 });
    expect(options[0]).toEqual({ value: null, label: "Off" });
    expect(options.map((option) => option.value)).toEqual([null, 5, 10, 15, 30, 60, 120, 240]);
    expect(options.find((option) => option.value === 10)?.label).toBe("Every 10 min (suggested)");
    expect(cadenceOptions("Listings", { minListingsMinutes: 60, suggestedListingsMinutes: 240 }).find((option) => option.value === 240)?.label)
      .toBe("Every 4 h (suggested)");
  });

  it("says when something ran and when it runs next", () => {
    expect(ago(minutesAgo(0.2), now)).toBe("just now");
    expect(ago(minutesAgo(4), now)).toBe("4 min ago");
    expect(ago(minutesAgo(180), now)).toBe("3 h ago");
    expect(ago(null, now)).toBe("never");
    expect(dueIn(new Date(now.getTime() + 6 * 60_000).toISOString(), now)).toBe("in 6 min");
    expect(dueIn(minutesAgo(2), now)).toBe("due now");
  });

  it("describes a loop by what matters most: running, failing, or fine", () => {
    expect(loopSummary({ runningOn: "Office mini-PC" }, now)).toEqual({ text: "Running on Office mini-PC", tone: "info" });
    expect(loopSummary({ lastError: "eBay showed a security check.", consecutiveFailures: 1, lastStartedAt: minutesAgo(5) }, now).tone).toBe("danger");
    expect(loopSummary({ lastSucceededAt: minutesAgo(12) }, now)).toEqual({ text: "Last synced 12 min ago", tone: "success" });
    expect(loopSummary({}, now).text).toBe("Not synced yet");
  });

  it("never lets a queued scan look imminent when no browser can run it", () => {
    expect(requestStatus({ status: "Queued" }, true).label).toBe("Queued");
    expect(requestStatus({ status: "Queued" }, false)).toEqual({ label: "Waiting for a browser", tone: "warning" });
    expect(requestStatus({ status: "Running", runningOn: "Laptop" }, true).label).toBe("Running on Laptop");
  });

  it("offers each signed-in username once for binding", () => {
    expect(bindableUsernames({
      sessions: [
        { signedIn: true, username: "seller" },
        { signedIn: true, username: "seller" },
        { signedIn: false, username: null },
      ],
    })).toEqual(["seller"]);
  });

  it("labels health plainly", () => {
    expect(healthBadge("NoBrowser")).toEqual({ label: "No browser", tone: "warning" });
    expect(healthBadge("Unsupported").label).toBe("Not yet supported");
  });

  it("groups a pairing code for reading", () => {
    expect(formatCode("7kq2mx9d")).toBe("7KQ2-MX9D");
  });
});
