import { describe, expect, it } from "vitest";
import { daysHeld, presenceLabel, suggestKeeper } from "./inventory-rows";

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

  it("suggests keeping the record with photos and listings, then the oldest", () => {
    const imported = { id: "sale-import", thumbnailUrl: null, createdAt: "2026-09-01T00:00:00Z", facts: { channels: [{ platform: "eBay", status: "Sold" as const }] } };
    const original = { id: "original", thumbnailUrl: "/photo.jpg", createdAt: "2026-09-20T00:00:00Z", facts: { channels: [] } };
    const older = { id: "older", thumbnailUrl: "/photo.jpg", createdAt: "2026-08-01T00:00:00Z" };
    expect(suggestKeeper([imported, original])?.id).toBe("original");
    expect(suggestKeeper([original, older])?.id).toBe("older");
    expect(suggestKeeper([])).toBeUndefined();
  });
});
