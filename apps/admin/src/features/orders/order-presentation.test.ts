import { describe, expect, it } from "vitest";
import { daysBetween, paymentNote, stageLabel, trackingHref } from "./order-presentation";

describe("order presentation", () => {
  it("keeps a recorded tracking link over a built one", () => {
    expect(trackingHref("USPS", "9400 1", "https://example.com/t")).toBe("https://example.com/t");
  });

  it("builds the carrier's tracking page when none was saved", () => {
    expect(trackingHref("USPS", "9400 111")).toBe("https://tools.usps.com/go/TrackConfirmAction?tLabels=9400111");
    expect(trackingHref("UPS Ground", "1Z9")).toBe("https://www.ups.com/track?tracknum=1Z9");
    expect(trackingHref("Pigeon", "1")).toBeNull();
    expect(trackingHref(null, "1")).toBeNull();
  });

  it("says nothing about payment when an order is simply paid", () => {
    expect(paymentNote("Paid")).toBeNull();
    expect(paymentNote("Pending")?.label).toBe("Unpaid");
  });

  it("names stages the way the queues do", () => {
    expect(stageLabel("ToPack")).toBe("To pack");
    expect(stageLabel(undefined)).toBe("Unknown");
  });

  it("counts whole days and refuses a missing end", () => {
    expect(daysBetween("2026-09-29T15:00:00Z", "2026-10-02T18:00:00Z")).toBe(3);
    expect(daysBetween("2026-09-29T15:00:00Z", null)).toBeNull();
  });
});
