import { describe, expect, it } from "vitest";
import {
  COMMERCE_MANAGED_STATUSES,
  getInventoryCost,
  getProjectedMetrics,
  inventoryStatusLabel,
  matchesInventorySearch,
  type InventoryItem,
} from "./inventory-operations";

const item = (overrides: Partial<InventoryItem> = {}): InventoryItem => ({
  id: "00000000-0000-0000-0000-000000000001",
  title: "Sony Walkman",
  brand: "Sony",
  model: "WM-2",
  category: "Audio",
  barcode: "ABC123",
  storageLocation: "A-12",
  purchasePrice: 50,
  sellerAskingPrice: 60,
  estimatedSalePrice: 125,
  ...overrides,
});

describe("inventory operations", () => {
  it("uses actual acquisition cost before seller asking price", () => {
    expect(getInventoryCost(item())).toBe(50);
    expect(getInventoryCost(item({ purchasePrice: null }))).toBe(60);
  });

  it("calculates projected profit and ROI from specific item cost", () => {
    expect(getProjectedMetrics(item())).toEqual({ profit: 75, roiPercent: 150 });
  });

  it("does not invent financial metrics when cost is unavailable", () => {
    expect(getProjectedMetrics(item({ purchasePrice: null, sellerAskingPrice: null })))
      .toEqual({ profit: null, roiPercent: null });
  });

  it("searches operational identifiers case-insensitively", () => {
    expect(matchesInventorySearch(item(), "wm-2")).toBe(true);
    expect(matchesInventorySearch(item(), "a-12")).toBe(true);
    expect(matchesInventorySearch(item(), "furniture")).toBe(false);
  });

  it("keeps financial and fulfillment states out of manual lifecycle control", () => {
    expect(COMMERCE_MANAGED_STATUSES).toEqual(["Sold", "Packed", "Shipped", "Delivered", "Returned"]);
  });

  it("formats enum values for operators", () => {
    expect(inventoryStatusLabel("ReadyToList")).toBe("Ready To List");
  });
});
