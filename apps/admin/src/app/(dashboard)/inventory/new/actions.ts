"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { failedFormState, type FailedFormState, type FieldErrors } from "@/lib/form-state";

type UpsertInventoryItemRequest = components["schemas"]["UpsertInventoryItemRequest"];
type OwnershipType = components["schemas"]["InventoryOwnershipType"];
type FunctionalStatus = components["schemas"]["InventoryFunctionalStatus"];
type CompletenessStatus = components["schemas"]["InventoryCompletenessStatus"];
type AuthenticityStatus = components["schemas"]["InventoryAuthenticityStatus"];
type PackagingStatus = components["schemas"]["InventoryPackagingStatus"];
type DimensionUnit = components["schemas"]["DimensionUnit"];
type WeightUnit = components["schemas"]["WeightUnit"];

export type NewInventoryResult = { ok: boolean; message: string } & FailedFormState;

type NumberOptions = {
  min?: number;
  max?: number;
  integer?: boolean;
};

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function optionalNumber(
  formData: FormData,
  name: string,
  label: string,
  fieldErrors: FieldErrors,
  options: NumberOptions = {},
): number | null {
  const value = text(formData, name);
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

export async function createInventoryItem(_previous: NewInventoryResult, formData: FormData): Promise<NewInventoryResult> {
  const title = text(formData, "title");
  const fieldErrors: FieldErrors = {};
  if (!title) fieldErrors.title = "Title is required.";

  const purchasePrice = optionalNumber(formData, "purchasePrice", "Purchase price", fieldErrors, { min: 0 });
  const sellerAskingPrice = optionalNumber(formData, "sellerAskingPrice", "Seller asking price", fieldErrors, { min: 0 });
  const estimatedSalePrice = optionalNumber(formData, "estimatedSalePrice", "Estimated sale price", fieldErrors, { min: 0 });
  const manufactureYear = optionalNumber(formData, "manufactureYear", "Manufacture year", fieldErrors, { min: 1800, max: 2200, integer: true });
  const weight = optionalNumber(formData, "weight", "Weight", fieldErrors, { min: 0 });
  if (Object.keys(fieldErrors).length > 0) {
    return failedFormState("Fix the highlighted fields.", formData, fieldErrors);
  }

  const body: UpsertInventoryItemRequest = {
    productIdentityId: null,
    sourcingTripId: null,
    sourcingTripStopId: null,
    sourceLocationId: null,
    title,
    description: text(formData, "description"),
    category: text(formData, "category"),
    brand: text(formData, "brand"),
    model: text(formData, "model"),
    color: text(formData, "color"),
    condition: text(formData, "condition"),
    sizeSystem: text(formData, "sizeSystem"),
    sizeValue: text(formData, "sizeValue"),
    attributesJson: null,
    measurementsJson: null,
    storageLocation: text(formData, "storageLocation"),
    accessories: text(formData, "accessories"),
    missingParts: text(formData, "missingParts"),
    testingNotes: text(formData, "testingNotes"),
    cleaningNotes: null,
    listingNotes: text(formData, "listingNotes"),
    sellerAskingPrice,
    purchasePrice,
    estimatedSalePrice,
    barcode: text(formData, "barcode"),
    notes: text(formData, "notes"),
    ownershipType: (text(formData, "ownershipType") ?? "Owned") as OwnershipType,
    functionalStatus: (text(formData, "functionalStatus") ?? "Unknown") as FunctionalStatus,
    completenessStatus: (text(formData, "completenessStatus") ?? "Unknown") as CompletenessStatus,
    authenticityStatus: (text(formData, "authenticityStatus") ?? "NotAssessed") as AuthenticityStatus,
    packagingStatus: (text(formData, "packagingStatus") ?? "NotApplicable") as PackagingStatus,
    manufacturerPartNumber: text(formData, "manufacturerPartNumber"),
    serialNumber: text(formData, "serialNumber"),
    countryOfOrigin: text(formData, "countryOfOrigin"),
    manufactureYear,
    length: null,
    width: null,
    height: null,
    dimensionUnit: (text(formData, "dimensionUnit") ?? "Inches") as DimensionUnit,
    weight,
    weightUnit: (text(formData, "weightUnit") ?? "Ounces") as WeightUnit,
  };

  const api = await getApiClient();
  const result = await api.POST("/api/inventory", { body });
  if (!result.data?.id) return failedFormState(errorMessage(result.error, "The item could not be created."), formData);

  revalidatePath("/inventory");
  // Land on the new item so photos, listing generation, and full edit are one step away.
  redirect(`/inventory/${result.data.id}`);
}
