import type { components } from "@overhaul/api-client";

export type InventoryItem = components["schemas"]["InventoryItemDto"];
export type InventoryStatus = components["schemas"]["InventoryStatus"];

export const INVENTORY_STATUSES: InventoryStatus[] = [
  "Draft", "Researching", "Watchlist", "Purchased", "NeedsCleaning", "NeedsTesting",
  "NeedsRepair", "NeedsPhotos", "ReadyToList", "Listed", "Sold", "Packed", "Shipped",
  "Delivered", "Returned", "Donated", "Disposed",
];

export const MANUAL_INVENTORY_STATUSES: Array<{ value: InventoryStatus; label: string }> = [
  { value: "Draft", label: "Draft" },
  { value: "Researching", label: "Researching" },
  { value: "Watchlist", label: "Watchlist" },
  { value: "Purchased", label: "Purchased" },
  { value: "NeedsCleaning", label: "Needs cleaning" },
  { value: "NeedsTesting", label: "Needs testing" },
  { value: "NeedsRepair", label: "Needs repair" },
  { value: "NeedsPhotos", label: "Needs photos" },
  { value: "ReadyToList", label: "Ready to list" },
  { value: "Listed", label: "Listed" },
  { value: "Donated", label: "Donated" },
  { value: "Disposed", label: "Disposed" },
];

export const COMMERCE_MANAGED_STATUSES: InventoryStatus[] = ["Sold", "Packed", "Shipped", "Delivered", "Returned"];

export function getInventoryCost(item: InventoryItem): number | null {
  return item.purchasePrice ?? item.sellerAskingPrice ?? null;
}

export function getProjectedMetrics(item: InventoryItem): { profit: number | null; roiPercent: number | null } {
  const cost = getInventoryCost(item);
  const profit = item.estimatedSalePrice != null && cost != null ? item.estimatedSalePrice - cost : null;
  const roiPercent = profit != null && cost != null && cost > 0 ? (profit / cost) * 100 : null;
  return { profit, roiPercent };
}

export function matchesInventorySearch(item: InventoryItem, query: string): boolean {
  const normalized = query.trim().toLowerCase();
  if (!normalized) return true;
  return [item.title, item.brand, item.model, item.category, item.barcode, item.storageLocation]
    .some((value) => value?.toLowerCase().includes(normalized));
}

export function inventoryStatusLabel(value: string | null | undefined): string {
  return value?.replace(/([a-z])([A-Z])/g, "$1 $2") ?? "—";
}
