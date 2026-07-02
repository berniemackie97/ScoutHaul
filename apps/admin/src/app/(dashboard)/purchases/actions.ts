"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";

type AcquisitionLotAllocationMethod = "Equal" | "WeightedByExpectedValue" | "Manual";
type UpsertAcquisitionLotRequest = {
  name: string;
  purchasedAt: string;
  purchaseSubtotal: number;
  shippingCost: number;
  taxCost: number;
  feeCost: number;
  otherCost: number;
  allocationMethod: AcquisitionLotAllocationMethod;
  supplierName: string | null;
  sourceLocationId: string | null;
  sourcingTripId: string | null;
  sourcingTripStopId: string | null;
  externalReference: string | null;
  notes: string | null;
  items: Array<{
    inventoryItemId: string;
    manualAllocatedCost: number | null;
    allocationWeight: number | null;
    notes: string | null;
  }>;
};
type UntypedApi = {
  POST: (path: string, init: { body: UpsertAcquisitionLotRequest }) => Promise<{ data?: { id?: string; name?: string }; error?: unknown }>;
};

export type PurchaseActionResult = { ok: boolean; message: string };

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function money(formData: FormData, name: string): number {
  const parsed = Number(text(formData, name) ?? 0);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : Number.NaN;
}

function errorMessage(error: unknown): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return "The acquisition lot could not be saved.";
}

export async function createPurchaseLot(_previous: PurchaseActionResult, formData: FormData): Promise<PurchaseActionResult> {
  const name = text(formData, "name");
  if (!name) return { ok: false, message: "Lot name is required." };

  const selectedIds = formData.getAll("inventoryItemId").filter((value): value is string => typeof value === "string" && value.length > 0);
  if (selectedIds.length === 0) return { ok: false, message: "Select at least one inventory item." };

  const purchaseSubtotal = money(formData, "purchaseSubtotal");
  const shippingCost = money(formData, "shippingCost");
  const taxCost = money(formData, "taxCost");
  const feeCost = money(formData, "feeCost");
  const otherCost = money(formData, "otherCost");
  if ([purchaseSubtotal, shippingCost, taxCost, feeCost, otherCost].some((value) => !Number.isFinite(value))) {
    return { ok: false, message: "Costs must be valid non-negative amounts." };
  }

  const allocationMethod = (text(formData, "allocationMethod") ?? "Equal") as AcquisitionLotAllocationMethod;
  const items = selectedIds.map((id) => ({
    inventoryItemId: id,
    manualAllocatedCost: text(formData, `manualCost:${id}`) ? money(formData, `manualCost:${id}`) : null,
    allocationWeight: text(formData, `weight:${id}`) ? money(formData, `weight:${id}`) : null,
    notes: text(formData, `notes:${id}`),
  }));

  const purchasedDate = text(formData, "purchasedAt");
  const purchasedAt = purchasedDate ? new Date(`${purchasedDate}T12:00:00Z`) : new Date();
  if (Number.isNaN(purchasedAt.getTime()) || (purchasedDate !== null && purchasedDate > new Date().toISOString().slice(0, 10))) {
    return { ok: false, message: "Enter a valid purchase date that is not in the future." };
  }

  const body: UpsertAcquisitionLotRequest = {
    name,
    purchasedAt: purchasedAt.toISOString(),
    purchaseSubtotal,
    shippingCost,
    taxCost,
    feeCost,
    otherCost,
    allocationMethod,
    supplierName: text(formData, "supplierName"),
    sourceLocationId: null,
    sourcingTripId: null,
    sourcingTripStopId: null,
    externalReference: text(formData, "externalReference"),
    notes: text(formData, "notes"),
    items,
  };

  const api = (await getApiClient()) as unknown as UntypedApi;
  const result = await api.POST("/api/acquisition-lots", { body });
  if (!result.data?.id) return { ok: false, message: errorMessage(result.error) };

  revalidatePath("/purchases");
  revalidatePath("/inventory");
  revalidatePath("/finance");
  return { ok: true, message: `Saved acquisition lot ${result.data.name}.` };
}
