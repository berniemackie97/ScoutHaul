"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { failedFormState, type FailedFormState, type FieldErrors } from "@/lib/form-state";
import { moneyField, purchasedAtIso, textField } from "./form-helpers";
import { errorMessage } from "@/lib/api-error";

type UpsertAcquisitionLotRequest = components["schemas"]["UpsertAcquisitionLotRequest"];
type AcquisitionLotAllocationMethod = components["schemas"]["AcquisitionLotAllocationMethod"];

export type PurchaseActionResult = { ok: boolean; message: string } & FailedFormState;

const costFieldLabels: Record<string, string> = {
  purchaseSubtotal: "Purchase subtotal",
  shippingCost: "Inbound shipping",
  taxCost: "Tax",
  feeCost: "Buyer fees",
  otherCost: "Other landed cost",
};

function selectedInventoryIds(formData: FormData) {
  return formData.getAll("inventoryItemId").filter((value): value is string => typeof value === "string" && value.length > 0);
}

function failure(message: string, formData: FormData, fieldErrors: FieldErrors = {}): PurchaseActionResult {
  const result = failedFormState(message, formData, fieldErrors);
  return {
    ...result,
    values: {
      ...(result.values ?? {}),
      selectedInventoryItemIds: selectedInventoryIds(formData).join("|"),
    },
  };
}

export async function createPurchaseLot(_previous: PurchaseActionResult, formData: FormData): Promise<PurchaseActionResult> {
  const name = textField(formData, "name");
  const purchaseKind = textField(formData, "purchaseKind") === "Single" ? "Single" : "Lot";
  const fieldErrors: FieldErrors = {};
  if (!name) fieldErrors.name = purchaseKind === "Single" ? "Purchase name is required." : "Lot name is required.";

  // Items are optional: you can record a costs-only lot and itemize it on the lot page afterward.
  const selectedIds = selectedInventoryIds(formData);
  if (purchaseKind === "Single" && selectedIds.length !== 1) {
    fieldErrors.inventoryItemId = "A single-item purchase must be attached to exactly one inventory item.";
  }

  const costs = ["purchaseSubtotal", "shippingCost", "taxCost", "feeCost", "otherCost"].map((field) => {
    const value = moneyField(formData, field);
    if (!Number.isFinite(value)) fieldErrors[field] = `${costFieldLabels[field]} must be a valid non-negative amount.`;
    return value;
  });
  const [purchaseSubtotal, shippingCost, taxCost, feeCost, otherCost] = costs;

  const purchasedAt = purchasedAtIso(textField(formData, "purchasedAt"));
  if (!purchasedAt) fieldErrors.purchasedAt = "Enter a valid purchase date that is not in the future.";

  const allocationMethod = (textField(formData, "allocationMethod") ?? "Equal") as AcquisitionLotAllocationMethod;
  const itemInputs = selectedIds.map((id) => {
    const manualAllocatedCost = textField(formData, `manualCost:${id}`) ? moneyField(formData, `manualCost:${id}`) : null;
    const allocationWeight = textField(formData, `weight:${id}`) ? moneyField(formData, `weight:${id}`) : null;
    if (manualAllocatedCost !== null && !Number.isFinite(manualAllocatedCost)) {
      fieldErrors[`manualCost:${id}`] = "Manual cost must be a valid non-negative amount.";
    }
    if (allocationWeight !== null && !Number.isFinite(allocationWeight)) {
      fieldErrors[`weight:${id}`] = "Weight must be a valid non-negative amount.";
    }
    return {
      inventoryItemId: id,
      manualAllocatedCost,
      allocationWeight,
      notes: textField(formData, `notes:${id}`),
    };
  });
  if (Object.keys(fieldErrors).length > 0) {
    return failure("Fix the highlighted fields.", formData, fieldErrors);
  }

  const body: UpsertAcquisitionLotRequest = {
    name: name!,
    purchasedAt: purchasedAt!,
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
    items: itemInputs,
  };

  const api = await getApiClient();
  const result = await api.POST("/api/acquisition-lots", { body });
  if (!result.data?.id) return failure(errorMessage(result.error, "The acquisition lot could not be saved."), formData);

  revalidatePath("/purchases");
  revalidatePath("/inventory");
  revalidatePath("/orders");
  // Land on the lot so the operator can itemize it and verify allocation immediately.
  redirect(`/purchases/${result.data.id}`);
}
