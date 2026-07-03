"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { apiErrorMessage, moneyField, purchasedAtIso, textField } from "./form-helpers";

type UpsertAcquisitionLotRequest = components["schemas"]["UpsertAcquisitionLotRequest"];
type AcquisitionLotAllocationMethod = components["schemas"]["AcquisitionLotAllocationMethod"];

export type PurchaseActionResult = { ok: boolean; message: string };

export async function createPurchaseLot(_previous: PurchaseActionResult, formData: FormData): Promise<PurchaseActionResult> {
  const name = textField(formData, "name");
  const purchaseKind = textField(formData, "purchaseKind") === "Single" ? "Single" : "Lot";
  if (!name) return { ok: false, message: purchaseKind === "Single" ? "Purchase name is required." : "Lot name is required." };

  // Items are optional: you can record a costs-only lot and itemize it on the lot page afterward.
  const selectedIds = formData.getAll("inventoryItemId").filter((value): value is string => typeof value === "string" && value.length > 0);
  if (purchaseKind === "Single" && selectedIds.length !== 1) {
    return { ok: false, message: "A single-item purchase must be attached to exactly one inventory item." };
  }

  const costs = ["purchaseSubtotal", "shippingCost", "taxCost", "feeCost", "otherCost"].map((field) => moneyField(formData, field));
  if (costs.some((value) => !Number.isFinite(value))) {
    return { ok: false, message: "Costs must be valid non-negative amounts." };
  }
  const [purchaseSubtotal, shippingCost, taxCost, feeCost, otherCost] = costs;

  const purchasedAt = purchasedAtIso(textField(formData, "purchasedAt"));
  if (!purchasedAt) return { ok: false, message: "Enter a valid purchase date that is not in the future." };

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
    items: selectedIds.map((id) => ({
      inventoryItemId: id,
      manualAllocatedCost: textField(formData, `manualCost:${id}`) ? moneyField(formData, `manualCost:${id}`) : null,
      allocationWeight: textField(formData, `weight:${id}`) ? moneyField(formData, `weight:${id}`) : null,
      notes: textField(formData, `notes:${id}`),
    })),
  };

  const api = await getApiClient();
  const result = await api.POST("/api/acquisition-lots", { body });
  if (!result.data?.id) return { ok: false, message: apiErrorMessage(result.error, "The acquisition lot could not be saved.") };

  revalidatePath("/purchases");
  revalidatePath("/inventory");
  revalidatePath("/finance");
  // Land on the lot so the operator can itemize it and verify allocation immediately.
  redirect(`/purchases/${result.data.id}`);
}
