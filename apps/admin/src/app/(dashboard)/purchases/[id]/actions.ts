"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { apiErrorMessage, moneyField, purchasedAtIso, textField } from "../form-helpers";

type UpsertAcquisitionLotRequest = components["schemas"]["UpsertAcquisitionLotRequest"];
type AcquisitionLotAllocationMethod = components["schemas"]["AcquisitionLotAllocationMethod"];
type NewLotItemDto = components["schemas"]["NewLotItemDto"];

export type LotActionResult = { ok: boolean; message: string };

function revalidateLot(id: string) {
  revalidatePath("/purchases");
  revalidatePath(`/purchases/${id}`);
  revalidatePath("/inventory");
  revalidatePath("/orders");
}

/** Revise a lot's landed cost, allocation method, metadata, and the set/weights of allocated items. */
export async function updateLot(id: string, _previous: LotActionResult, formData: FormData): Promise<LotActionResult> {
  const name = textField(formData, "name");
  if (!name) return { ok: false, message: "Lot name is required." };

  const keptIds = formData.getAll("inventoryItemId").filter((value): value is string => typeof value === "string" && value.length > 0);
  if (keptIds.length === 0) return { ok: false, message: "A lot must keep at least one allocated item." };

  const costs = ["purchaseSubtotal", "shippingCost", "taxCost", "feeCost", "otherCost"].map((field) => moneyField(formData, field));
  if (costs.some((value) => !Number.isFinite(value))) return { ok: false, message: "Costs must be valid non-negative amounts." };
  const [purchaseSubtotal, shippingCost, taxCost, feeCost, otherCost] = costs;

  const purchasedAt = purchasedAtIso(textField(formData, "purchasedAt"));
  if (!purchasedAt) return { ok: false, message: "Enter a valid purchase date that is not in the future." };

  const items = keptIds.map((itemId) => ({
    inventoryItemId: itemId,
    manualAllocatedCost: textField(formData, `manualCost:${itemId}`) ? moneyField(formData, `manualCost:${itemId}`) : null,
    allocationWeight: textField(formData, `weight:${itemId}`) ? moneyField(formData, `weight:${itemId}`) : null,
    notes: null,
  }));
  if (items.some((item) => item.manualAllocatedCost !== null && !Number.isFinite(item.manualAllocatedCost))) {
    return { ok: false, message: "Manual costs must be valid non-negative amounts." };
  }

  const allocationMethod = (textField(formData, "allocationMethod") ?? "Equal") as AcquisitionLotAllocationMethod;
  const body: UpsertAcquisitionLotRequest = {
    name,
    purchasedAt,
    purchaseSubtotal,
    shippingCost,
    taxCost,
    feeCost,
    otherCost,
    allocationMethod,
    supplierName: textField(formData, "supplierName"),
    sourceLocationId: null,
    sourcingTripId: null,
    sourcingTripStopId: null,
    externalReference: textField(formData, "externalReference"),
    notes: textField(formData, "notes"),
    items,
  };

  const api = await getApiClient();
  const result = await api.PUT("/api/acquisition-lots/{id}", { params: { path: { id } }, body });
  if (!result.data) return { ok: false, message: apiErrorMessage(result.error, "The lot could not be updated.") };

  revalidateLot(id);
  return { ok: true, message: "Lot updated and cost basis re-allocated." };
}

/** Attach existing inventory items to a lot and re-allocate. Merges them with the lot's current items via PUT. */
export async function attachItemsToLot(id: string, _previous: LotActionResult, formData: FormData): Promise<LotActionResult> {
  const newIds = formData.getAll("inventoryItemId").filter((value): value is string => typeof value === "string" && value.length > 0);
  if (newIds.length === 0) return { ok: false, message: "Select at least one item to attach." };

  const api = await getApiClient();
  const current = await api.GET("/api/acquisition-lots/{id}", { params: { path: { id } } });
  const lot = current.data;
  if (!lot) return { ok: false, message: apiErrorMessage(current.error, "The lot could not be loaded.") };

  const manual = lot.allocationMethod === "Manual";
  const existing = (lot.items ?? []).map((item) => ({
    inventoryItemId: item.inventoryItemId!,
    manualAllocatedCost: manual ? item.allocatedCost ?? 0 : null,
    allocationWeight: item.allocationWeight ?? null,
    notes: null,
  }));
  const additions = newIds
    .filter((newId) => !existing.some((item) => item.inventoryItemId === newId))
    .map((newId) => ({ inventoryItemId: newId, manualAllocatedCost: manual ? 0 : null, allocationWeight: null, notes: null }));

  const body: UpsertAcquisitionLotRequest = {
    name: lot.name ?? "Lot",
    purchasedAt: lot.purchasedAt ?? new Date().toISOString(),
    purchaseSubtotal: lot.purchaseSubtotal ?? 0,
    shippingCost: lot.shippingCost ?? 0,
    taxCost: lot.taxCost ?? 0,
    feeCost: lot.feeCost ?? 0,
    otherCost: lot.otherCost ?? 0,
    allocationMethod: (lot.allocationMethod ?? "Equal") as AcquisitionLotAllocationMethod,
    supplierName: lot.supplierName ?? null,
    sourceLocationId: null,
    sourcingTripId: null,
    sourcingTripStopId: null,
    externalReference: lot.externalReference ?? null,
    notes: lot.notes ?? null,
    items: [...existing, ...additions],
  };

  const result = await api.PUT("/api/acquisition-lots/{id}", { params: { path: { id } }, body });
  if (!result.data) return { ok: false, message: apiErrorMessage(result.error, "Items could not be attached. Manual-allocation lots need per-item costs — set them in the lot editor.") };

  revalidateLot(id);
  return { ok: true, message: `Attached ${additions.length} item${additions.length === 1 ? "" : "s"} and re-allocated.` };
}

const nonNegative = (value: unknown): number | null => {
  const parsed = typeof value === "number" ? value : Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
};
const trimmed = (value: unknown): string | null => {
  const text = typeof value === "string" ? value.trim() : "";
  return text.length > 0 ? text : null;
};

/**
 * Itemize a lot: create N new draft inventory items directly inside it, then re-allocate.
 * Rows arrive as a single structured JSON payload (not positional form fields) so parsing is robust.
 */
export async function addLotItems(id: string, _previous: LotActionResult, formData: FormData): Promise<LotActionResult> {
  const raw = textField(formData, "itemsJson");
  if (!raw) return { ok: false, message: "Add at least one item with a title." };

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return { ok: false, message: "The item rows could not be read. Reload and try again." };
  }
  if (!Array.isArray(parsed)) return { ok: false, message: "The item rows could not be read. Reload and try again." };

  const items: NewLotItemDto[] = [];
  for (const entry of parsed) {
    if (!entry || typeof entry !== "object") continue;
    const row = entry as Record<string, unknown>;
    const title = trimmed(row.title);
    if (!title) continue;
    items.push({
      title,
      category: trimmed(row.category),
      condition: trimmed(row.condition),
      estimatedSalePrice: nonNegative(row.estimatedSalePrice),
      manualAllocatedCost: nonNegative(row.manualAllocatedCost),
      allocationWeight: nonNegative(row.allocationWeight),
    });
  }

  if (items.length === 0) return { ok: false, message: "Add at least one item with a title." };

  const api = await getApiClient();
  const result = await api.POST("/api/acquisition-lots/{id}/items", { params: { path: { id } }, body: { items } });
  if (!result.data) return { ok: false, message: apiErrorMessage(result.error, "Items could not be added to the lot.") };

  revalidateLot(id);
  return { ok: true, message: `Added ${items.length} item${items.length === 1 ? "" : "s"} and re-allocated cost basis.` };
}
