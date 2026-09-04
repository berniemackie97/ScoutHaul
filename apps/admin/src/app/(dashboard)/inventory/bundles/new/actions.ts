"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { failedFormState, type FailedFormState, type FieldErrors } from "@/lib/form-state";
import { errorMessage } from "@/lib/api-error";

type CreateInventoryBundleRequest = components["schemas"]["CreateInventoryBundleRequest"];

export type BundleActionResult = { ok: boolean; message: string } & FailedFormState;

const bundleCreateStatuses = new Set(["Draft", "NeedsPhotos", "ReadyToList"]);

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function number(formData: FormData, name: string): number | null {
  const value = text(formData, name);
  if (!value) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}


function selectedComponentIds(formData: FormData) {
  return formData.getAll("componentInventoryItemId").filter((value): value is string => typeof value === "string" && value.length > 0);
}

function failure(message: string, formData: FormData, fieldErrors: FieldErrors = {}): BundleActionResult {
  const result = failedFormState(message, formData, fieldErrors);
  return {
    ...result,
    values: {
      ...(result.values ?? {}),
      selectedComponentIds: selectedComponentIds(formData).join("|"),
    },
  };
}

export async function createBundle(_previous: BundleActionResult, formData: FormData): Promise<BundleActionResult> {
  const title = text(formData, "title");
  const fieldErrors: FieldErrors = {};
  if (!title) fieldErrors.title = "Bundle title is required.";

  const componentInventoryItemIds = selectedComponentIds(formData);
  if (new Set(componentInventoryItemIds).size < 2) fieldErrors.componentInventoryItemId = "Select at least two distinct inventory items.";

  const estimatedSalePrice = number(formData, "estimatedSalePrice");
  if (estimatedSalePrice === null && text(formData, "estimatedSalePrice")) {
    fieldErrors.estimatedSalePrice = "Estimated sale price must be a valid non-negative amount.";
  }
  const status = text(formData, "status") ?? "ReadyToList";
  if (!bundleCreateStatuses.has(status)) {
    fieldErrors.status = "Choose a valid bundle starting stage.";
  }
  if (Object.keys(fieldErrors).length > 0) {
    return failure("Fix the highlighted fields.", formData, fieldErrors);
  }

  const body: CreateInventoryBundleRequest = {
    title: title!,
    description: text(formData, "description"),
    category: text(formData, "category"),
    estimatedSalePrice,
    status: status as CreateInventoryBundleRequest["status"],
    componentInventoryItemIds: [...new Set(componentInventoryItemIds)],
    notes: text(formData, "notes"),
  };

  const api = await getApiClient();
  const result = await api.POST("/api/inventory/bundles", { body });
  if (!result.data?.id) return failure(errorMessage(result.error, "The bundle could not be created."), formData);

  revalidatePath("/inventory");
  redirect(`/inventory/${result.data.id}`);
}
