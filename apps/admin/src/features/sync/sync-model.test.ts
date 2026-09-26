import { describe, expect, it } from "vitest";
import { ago, bindableUsernames, cadenceOptions, dueIn, formatCode, healthBadge, loopSummary, photoSyncSummary, platformSummary, requestStatus, salesNote, scannableLoops } from "./sync-model";

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
    expect(requestStatus({ status: "Queued" }, { health: "Ready", healthMessage: "Ready on Laptop." }, true))
      .toEqual({ label: "Queued", tone: "info", note: "Starting in a moment." });
    expect(requestStatus({ status: "Queued" }, undefined, false).label).toBe("Waiting for a browser");
    expect(requestStatus({ status: "Running", runningOn: "Laptop" }, undefined, true).label).toBe("Running on Laptop");
  });

  it("says what a queued scan is waiting for, and that it runs by itself after", () => {
    expect(requestStatus({ status: "Queued" }, { health: "NoBrowser", healthMessage: "Laptop is signed out of eBay. Sign in to eBay there." }, true))
      .toEqual({ label: "Waiting", tone: "warning", note: "Laptop is signed out of eBay. Sign in to eBay there. It starts by itself after." });
  });

  it("says where an account's photos stand", () => {
    expect(photoSyncSummary({ enabled: false, waiting: 0 }))
      .toEqual({ text: "Item photos are left as they are", tone: "neutral" });
    expect(photoSyncSummary({ enabled: true, waiting: 0 }))
      .toEqual({ text: "Item photos match their listings", tone: "success" });
    expect(photoSyncSummary({ enabled: true, waiting: 1 }).text).toBe("1 listing still to come over");
    expect(photoSyncSummary({ enabled: true, waiting: 12 }).text).toBe("12 listings still to come over");
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

describe("what a marketplace can be asked for", () => {
  it("offers only the scans its adapter reads", () => {
    expect(scannableLoops({ supportedLoops: ["Sales", "Listings"] })).toEqual(["Sales", "Listings"]);
    // Depop reads listings only; a sales button there would always come back skipped.
    expect(scannableLoops({ supportedLoops: ["Listings"] })).toEqual(["Listings"]);
    expect(scannableLoops({ supportedLoops: [] })).toEqual([]);
    expect(scannableLoops({})).toEqual([]);
  });

  it("says where a marketplace's sales come from instead, when the browser cannot read them", () => {
    expect(salesNote({ supportedLoops: ["Sales", "Listings"], platform: "eBay" })).toBeNull();
    expect(salesNote({ supportedLoops: ["Listings"], platform: "Depop" }))
      .toBe("Depop sales are not read from the browser. Import its report for sales.");
  });
});

describe("what each marketplace syncs", () => {
  it("says what it reads and how far back", () => {
    expect(platformSummary({ platform: "eBay", loops: ["Sales", "Listings"], historyDays: 90, enabled: true }))
      .toEqual({ syncs: "Sales and listings", history: "90 days of sales history", off: null });
    expect(platformSummary({ platform: "Vinted", loops: ["Sales", "Listings"], historyDays: 3650, enabled: true }).history)
      .toBe("10 years of sales history");
    expect(platformSummary({ platform: "Depop", loops: ["Listings"], historyDays: 0, enabled: true }))
      .toEqual({ syncs: "Listings only", history: "No sales history", off: null });
  });

  it("says plainly when a marketplace has been switched off, and why", () => {
    expect(platformSummary({ platform: "eBay", loops: ["Sales"], historyDays: 90, enabled: false, disabledReason: "eBay changed its orders page." }).off)
      .toBe("eBay changed its orders page.");
    expect(platformSummary({ platform: "eBay", loops: ["Sales"], historyDays: 90, enabled: false }).off)
      .toBe("Switched off for now");
  });
});
