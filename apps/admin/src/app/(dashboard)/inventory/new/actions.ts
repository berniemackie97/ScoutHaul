"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { failedFormState, type FailedFormState } from "@/lib/form-state";
import { readItemForm } from "@/features/inventory/item-form/read-form";
import { errorMessage } from "@/lib/api-error";

type UpsertInventoryItemRequest = components["schemas"]["UpsertInventoryItemRequest"];
type InventoryStatus = components["schemas"]["InventoryStatus"];
export type NewInventoryResult = { ok: boolean; message: string } & FailedFormState;

const createStatuses = new Set([
  "Draft",
  "Researching",
  "Watchlist",
  "Purchased",
  "NeedsCleaning",
  "NeedsTesting",
  "NeedsRepair",
  "NeedsPhotos",
  "ReadyToList",
]);

export async function createInventoryItem(_previous: NewInventoryResult, formData: FormData): Promise<NewInventoryResult> {
  const { fields, errors } = readItemForm(formData);
  const status = formData.get("status");
  const stage = typeof status === "string" && status ? status : "Draft";
  if (!createStatuses.has(stage)) errors.status = "Choose a valid pre-sale starting stage.";
  if (Object.keys(errors).length > 0) {
    return failedFormState("Fix the highlighted fields.", formData, errors);
  }

  const body: UpsertInventoryItemRequest = {
    productIdentityId: null,
    sourcingTripId: null,
    sourcingTripStopId: null,
    sourceLocationId: null,
    ...fields,
    status: stage as InventoryStatus,
  };

  const api = await getApiClient();
  const result = await api.POST("/api/inventory", { body });
  if (!result.data?.id) return failedFormState(errorMessage(result.error, "The item could not be created."), formData);

  revalidatePath("/inventory");
  // Land on the new item so photos, listing generation, and full edit are one step away.
  redirect(`/inventory/${result.data.id}`);
}
