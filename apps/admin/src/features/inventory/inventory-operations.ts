import type { components } from "@overhaul/api-client";

export type InventoryStatus = components["schemas"]["InventoryStatus"] | "Bundled";
export type InventoryBundleComponent = {
  id?: string;
  bundleInventoryItemId?: string;
  componentInventoryItemId?: string;
  componentTitle?: string | null;
  componentStatus?: InventoryStatus;
  quantity?: number;
  costSnapshot?: number;
  notes?: string | null;
};
export type InventoryItem = Omit<components["schemas"]["InventoryItemDto"], "status"> & {
  status?: InventoryStatus;
  isBundle?: boolean;
  bundleComponentCount?: number;
  bundleComponents?: InventoryBundleComponent[] | null;
  acquisitionLotId?: string | null;
  acquisitionLotName?: string | null;
  acquisitionAllocatedCost?: number | null;
};

export const INVENTORY_STATUSES: InventoryStatus[] = [
  "Draft",
  "Researching",
  "Watchlist",
  "Purchased",
  "NeedsCleaning",
  "NeedsTesting",
  "NeedsRepair",
  "NeedsPhotos",
  "ReadyToList",
  "Bundled",
  "Listed",
  "Sold",
  "Packed",
  "Shipped",
  "Delivered",
  "Returned",
  "Donated",
  "Disposed",
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

export type InventoryLifecycleGroup = components["schemas"]["InventoryLifecycleGroup"];

// Full ordered status list used for display and lifecycle filtering. Commerce-managed stages are
// read-only in inventory controls and must advance through the canonical sale workflow.
export const INVENTORY_STATUS_GROUPS: Array<{
  group: string;
  options: Array<{ value: InventoryStatus; label: string }>;
}> = [
  {
    group: "Active",
    options: [
      { value: "Draft", label: "Draft" },
      { value: "Researching", label: "Researching" },
      { value: "Watchlist", label: "Watchlist" },
      { value: "Purchased", label: "Purchased" },
      { value: "NeedsCleaning", label: "Needs cleaning" },
      { value: "NeedsTesting", label: "Needs testing" },
      { value: "NeedsRepair", label: "Needs repair" },
      { value: "NeedsPhotos", label: "Needs photos" },
      { value: "ReadyToList", label: "Ready to list" },
      { value: "Bundled", label: "Bundled component" },
      { value: "Listed", label: "Listed" },
    ],
  },
  {
    group: "Sold & fulfillment",
    options: [
      { value: "Sold", label: "Sold" },
      { value: "Packed", label: "Packed" },
      { value: "Shipped", label: "Shipped" },
      { value: "Delivered", label: "Delivered" },
    ],
  },
  {
    group: "Closed",
    options: [
      { value: "Returned", label: "Returned" },
      { value: "Donated", label: "Donated" },
      { value: "Disposed", label: "Disposed" },
    ],
  },
];

// Order-aware group filters for the inventory list (maps to the API `lifecycle` query param).
export const LIFECYCLE_FILTERS: Array<{ value: InventoryLifecycleGroup; label: string }> = [
  { value: "Active", label: "Active (unsold)" },
  { value: "Sold", label: "Sold & fulfilling" },
  { value: "Returned", label: "Returned" },
  { value: "Removed", label: "Removed" },
];

export const LIFECYCLE_GROUP_VALUES: InventoryLifecycleGroup[] = ["Active", "Sold", "Returned", "Removed"];

export const TERMINAL_STATUSES: InventoryStatus[] = ["Donated", "Disposed"];

const STATUS_RANK = new Map<InventoryStatus, number>(INVENTORY_STATUSES.map((status, index) => [status, index]));

/** Position in the canonical lifecycle; higher = later. Mirrors the API's InventoryLifecycle.Rank. */
export function statusRank(status: InventoryStatus): number {
  return STATUS_RANK.get(status) ?? Number.MAX_SAFE_INTEGER;
}

export function isSoldStatus(status: InventoryStatus): boolean {
  return (["Sold", "Packed", "Shipped", "Delivered"] as InventoryStatus[]).includes(status);
}

export function getInventoryCost(item: InventoryItem): number | null {
  return item.purchasePrice ?? item.sellerAskingPrice ?? null;
}

export function getProjectedMetrics(item: InventoryItem): {
  profit: number | null;
  roiPercent: number | null;
} {
  const cost = getInventoryCost(item);
  const profit = item.estimatedSalePrice != null && cost != null ? item.estimatedSalePrice - cost : null;
  const roiPercent = profit != null && cost != null && cost > 0 ? (profit / cost) * 100 : null;
  return { profit, roiPercent };
}

export function matchesInventorySearch(item: InventoryItem, query: string): boolean {
  const normalized = query.trim().toLowerCase();
  if (!normalized) return true;
  return [item.title, item.brand, item.model, item.category, item.barcode, item.storageLocation].some((value) =>
    value?.toLowerCase().includes(normalized)
  );
}

export function inventoryStatusLabel(value: string | null | undefined): string {
  return value?.replace(/([a-z])([A-Z])/g, "$1 $2") ?? "—";
}
