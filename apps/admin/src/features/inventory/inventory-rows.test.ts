import { describe, expect, it } from "vitest";
import { daysHeld, presenceLabel } from "./inventory-rows";

describe("inventory rows", () => {
  it("counts whole days held and never goes negative", () => {
    const now = Date.parse("2026-10-04T12:00:00Z");
    expect(daysHeld("2026-09-04T12:00:00Z", now)).toBe(30);
    expect(daysHeld("2026-10-05T12:00:00Z", now)).toBe(0);
    expect(daysHeld(null, now)).toBeNull();
  });

  it("names a live listing by its price and flags one that must come down", () => {
    expect(presenceLabel({ platform: "eBay", status: "Active", price: 24.5 })).toEqual({ text: "eBay $25", tone: "default" });
    expect(presenceLabel({ platform: "Depop", status: "PendingDelist" }).tone).toBe("warning");
    expect(presenceLabel({ platform: "Depop", status: "Sold" }).text).toBe("Sold on Depop");
  });
});
