"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import type { InventoryStatus } from "@/features/inventory/inventory-operations";
import {
  getApiAccessToken,
  getApiBaseUrl,
  getApiClient,
} from "@/lib/api";
import { failedFormState, type FailedFormState, type FieldErrors } from "@/lib/form-state";

type UpsertInventoryItemRequest = components["schemas"]["UpsertInventoryItemRequest"];
type OwnershipType = components["schemas"]["InventoryOwnershipType"];
type FunctionalStatus = components["schemas"]["InventoryFunctionalStatus"];
type CompletenessStatus = components["schemas"]["InventoryCompletenessStatus"];
type AuthenticityStatus = components["schemas"]["InventoryAuthenticityStatus"];
type PackagingStatus = components["schemas"]["InventoryPackagingStatus"];
type DimensionUnit = components["schemas"]["DimensionUnit"];
type WeightUnit = components["schemas"]["WeightUnit"];

export type InventoryActionResult = {
  ok: boolean;
  message: string;
} & FailedFormState;

type NumberOptions = {
  min?: number;
  max?: number;
  integer?: boolean;
};

const initialFailure = (message: string): InventoryActionResult => ({ ok: false, message });

function textValue(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : null;
}

function numberValue(
  formData: FormData,
  name: string,
  label: string,
  fieldErrors: FieldErrors,
  options: NumberOptions = {},
): number | null {
  const value = textValue(formData, name);
  if (value === null) return null;
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    fieldErrors[name] = `${label} must be a valid number.`;
    return null;
  }
  if (options.integer && !Number.isInteger(parsed)) {
    fieldErrors[name] = `${label} must be a whole number.`;
    return null;
  }
  if (options.min !== undefined && parsed < options.min) {
    fieldErrors[name] = `${label} cannot be less than ${options.min}.`;
    return null;
  }
  if (options.max !== undefined && parsed > options.max) {
    fieldErrors[name] = `${label} cannot be greater than ${options.max}.`;
    return null;
  }
  return parsed;
}

function errorMessage(error: unknown, fallback: string): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return fallback;
}

export async function updateInventoryItem(
  id: string,
  _previous: InventoryActionResult,
  formData: FormData,
): Promise<InventoryActionResult> {
  const title = textValue(formData, "title");
  const fieldErrors: FieldErrors = {};
  if (!title) fieldErrors.title = "Title is required.";

  const sellerAskingPrice = numberValue(formData, "sellerAskingPrice", "Seller asking price", fieldErrors, { min: 0 });
  const purchasePrice = numberValue(formData, "purchasePrice", "Acquisition cost", fieldErrors, { min: 0 });
  const estimatedSalePrice = numberValue(formData, "estimatedSalePrice", "Estimated sale price", fieldErrors, { min: 0 });
  const manufactureYear = numberValue(formData, "manufactureYear", "Manufacture year", fieldErrors, { min: 1800, max: 2200, integer: true });
  const length = numberValue(formData, "length", "Length", fieldErrors, { min: 0 });
  const width = numberValue(formData, "width", "Width", fieldErrors, { min: 0 });
  const height = numberValue(formData, "height", "Height", fieldErrors, { min: 0 });
  const weight = numberValue(formData, "weight", "Weight", fieldErrors, { min: 0 });
  if (Object.keys(fieldErrors).length > 0) {
    return failedFormState("Fix the highlighted fields.", formData, fieldErrors);
  }

  const api = await getApiClient();
  const current = await api.GET("/api/inventory/{id}", { params: { path: { id } } });
  if (!current.data) {
    return failedFormState(errorMessage(current.error, "Inventory item could not be loaded."), formData);
  }

  const body: UpsertInventoryItemRequest = {
    productIdentityId: current.data.productIdentityId ?? null,
    sourcingTripId: current.data.sourcingTripId ?? null,
    sourcingTripStopId: current.data.sourcingTripStopId ?? null,
    sourceLocationId: current.data.sourceLocationId ?? null,
    title,
    description: textValue(formData, "description"),
    category: textValue(formData, "category"),
    brand: textValue(formData, "brand"),
    model: textValue(formData, "model"),
    color: textValue(formData, "color"),
    condition: textValue(formData, "condition"),
    sizeSystem: textValue(formData, "sizeSystem"),
    sizeValue: textValue(formData, "sizeValue"),
    attributesJson: textValue(formData, "attributesJson"),
    measurementsJson: textValue(formData, "measurementsJson"),
    storageLocation: textValue(formData, "storageLocation"),
    accessories: textValue(formData, "accessories"),
    missingParts: textValue(formData, "missingParts"),
    testingNotes: textValue(formData, "testingNotes"),
    cleaningNotes: textValue(formData, "cleaningNotes"),
    listingNotes: textValue(formData, "listingNotes"),
    sellerAskingPrice,
    purchasePrice,
    estimatedSalePrice,
    barcode: textValue(formData, "barcode"),
    notes: textValue(formData, "notes"),
    ownershipType: textValue(formData, "ownershipType") as OwnershipType,
    functionalStatus: textValue(formData, "functionalStatus") as FunctionalStatus,
    completenessStatus: textValue(formData, "completenessStatus") as CompletenessStatus,
    authenticityStatus: textValue(formData, "authenticityStatus") as AuthenticityStatus,
    packagingStatus: textValue(formData, "packagingStatus") as PackagingStatus,
    manufacturerPartNumber: textValue(formData, "manufacturerPartNumber"),
    serialNumber: textValue(formData, "serialNumber"),
    countryOfOrigin: textValue(formData, "countryOfOrigin"),
    manufactureYear,
    length,
    width,
    height,
    dimensionUnit: textValue(formData, "dimensionUnit") as DimensionUnit,
    weight,
    weightUnit: textValue(formData, "weightUnit") as WeightUnit,
  };

  const result = await api.PUT("/api/inventory/{id}", {
    params: { path: { id } },
    body,
  });
  if (!result.data) {
    return failedFormState(errorMessage(result.error, "Inventory changes could not be saved."), formData);
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: "Inventory record saved." };
}

export async function changeInventoryStatus(
  id: string,
  status: InventoryStatus,
): Promise<InventoryActionResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/inventory/{id}/status", {
    params: { path: { id } },
    body: { status: status as components["schemas"]["InventoryStatus"] },
  });
  if (!result.data) {
    return initialFailure(errorMessage(result.error, "Inventory status could not be changed."));
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: `Status changed to ${status}.` };
}

export async function uploadInventoryPhoto(
  id: string,
  formData: FormData,
): Promise<InventoryActionResult> {
  const file = formData.get("file");
  if (!(file instanceof File) || file.size === 0) {
    return initialFailure("Choose an image to upload.");
  }
  if (file.size > 10_000_000) {
    return initialFailure("Photos must be smaller than 10 MB.");
  }

  const token = await getApiAccessToken();
  if (!token) return initialFailure("Your session expired. Sign in again.");

  const payload = new FormData();
  payload.set("File", file);
  payload.set("IsHero", formData.get("isHero") === "on" ? "true" : "false");

  const response = await fetch(`${getApiBaseUrl()}/api/inventory/${id}/photos`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: payload,
    cache: "no-store",
  });
  if (!response.ok) {
    const body = await response.text();
    return initialFailure(body || `Photo upload failed (${response.status}).`);
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: "Photo uploaded." };
}

export async function setInventoryHeroPhoto(
  id: string,
  url: string,
): Promise<InventoryActionResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/inventory/{id}/photos/hero", {
    params: { path: { id }, query: { url } },
  });
  if (!result.data) {
    return initialFailure(errorMessage(result.error, "Hero photo could not be changed."));
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: "Hero photo updated." };
}

export async function deleteInventoryPhoto(
  id: string,
  photoId: string,
): Promise<InventoryActionResult> {
  const api = await getApiClient();
  const result = await api.DELETE("/api/inventory/{id}/photos/{photoId}", {
    params: { path: { id, photoId } },
  });
  if (!result.data) {
    return initialFailure(errorMessage(result.error, "Photo could not be deleted."));
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: "Photo deleted." };
}

export type InventoryDeleteResult = InventoryActionResult & {
  deleted?: boolean;
};

export async function deleteInventoryItem(
  id: string,
  hard: boolean,
): Promise<InventoryDeleteResult> {
  const token = await getApiAccessToken();
  if (!token) return { ok: false, message: "Your session expired. Sign in again." };

  const response = await fetch(`${getApiBaseUrl()}/api/inventory/${id}?hard=${hard ? "true" : "false"}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (!response.ok) {
    const body = await response.text();
    return {
      ok: false,
      message: body || (hard ? "Inventory item could not be permanently deleted." : "Inventory item could not be archived."),
    };
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return {
    ok: true,
    deleted: hard,
    message: hard ? "Inventory item permanently deleted." : "Inventory item archived.",
  };
}
