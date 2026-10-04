import { describe, expect, it } from "vitest";
import { changeTone, compactMoney, humanize, moneyBreakdown, percentChange, reportDay } from "./report-format";

describe("report format", () => {
  it("has no change without a prior figure", () => {
    expect(percentChange(10, 0)).toBeNull();
    expect(percentChange(15, 10)).toBe(50);
    expect(percentChange(-5, -10)).toBe(50);
  });

  it("reads rising costs as bad news", () => {
    expect(changeTone(12, true)).toBe("up");
    expect(changeTone(12, false)).toBe("down");
    expect(changeTone(0.01)).toBe("flat");
    expect(changeTone(null)).toBe("flat");
  });

  it("breaks gross sales into parts that add back up", () => {
    const { segments, loss } = moneyBreakdown({
      grossSales: 270,
      costOfGoods: 100,
      realizedNetProfit: 100,
      platformFees: 32,
      paymentFees: 2,
      shippingCosts: 12,
      suppliesAndOther: 4,
      refundsAndAdjustments: 20,
    });
    expect(segments.map((segment) => segment.key)).toEqual(["net", "cogs", "fees", "shipping", "other"]);
    expect(segments.reduce((sum, segment) => sum + segment.value, 0)).toBe(270);
    expect(loss).toBe(0);
  });

  it("reports a loss beside the bar instead of as a segment", () => {
    const { segments, loss } = moneyBreakdown({ grossSales: 10, costOfGoods: 25, realizedNetProfit: -16, platformFees: 1 });
    expect(segments.some((segment) => segment.key === "net")).toBe(false);
    expect(loss).toBe(16);
  });

  it("names enum values the way people say them", () => {
    expect(humanize("InventoryPurchase")).toBe("Inventory purchase");
    expect(humanize(undefined)).toBe("—");
  });

  it("compacts money for axis ticks", () => {
    expect(compactMoney(850)).toBe("$850");
    expect(compactMoney(1250)).toBe("$1.3K");
    expect(compactMoney(25_400)).toBe("$25K");
    expect(compactMoney(-1_200_000)).toBe("−$1.2M");
  });

  it("reads a boundary's calendar day as sent", () => {
    expect(reportDay("2026-10-04T23:59:59.9999999-07:00")).toBe("2026-10-04");
  });
});
