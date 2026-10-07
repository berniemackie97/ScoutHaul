import type { components } from "@overhaul/api-client";
import type { FieldErrors } from "@/lib/form-state";
import { readListInput } from "@/lib/list-input";
import { attributesFromForm } from "../item-profiles";

type Schemas = components["schemas"];
type Request = Schemas["UpsertInventoryItemRequest"];

/** Everything an item form posts. The ids that tie an item to a trip or a product, and its stage, are the caller's. */
export type ItemFormFields = Omit<Request, "productIdentityId" | "sourcingTripId" | "sourcingTripStopId" | "sourceLocationId" | "status">;

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function choice<T extends string>(formData: FormData, name: string, fallback: T): T {
  return (text(formData, name) as T | null) ?? fallback;
}

function number(
  formData: FormData,
  name: string,
  label: string,
  errors: FieldErrors,
  { min = 0, max, integer = false }: { min?: number; max?: number; integer?: boolean } = {},
): number | null {
  const value = text(formData, name);
  if (value === null) return null;
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) errors[name] = `${label} must be a number.`;
  else if (integer && !Number.isInteger(parsed)) errors[name] = `${label} must be a whole number.`;
  else if (parsed < min) errors[name] = `${label} cannot be less than ${min}.`;
  else if (max !== undefined && parsed > max) errors[name] = `${label} cannot be more than ${max}.`;
  else return parsed;
  return null;
}

/** Reads and checks an item form. Field errors are keyed by input name, so each lands under its own input. */
export function readItemForm(formData: FormData): { fields: ItemFormFields; errors: FieldErrors } {
  const errors: FieldErrors = {};
  const title = text(formData, "title");
  if (!title) errors.title = "Title is required.";

  const fields: ItemFormFields = {
    title: title ?? "",
    description: text(formData, "description"),
    category: text(formData, "category"),
    brand: text(formData, "brand"),
    model: text(formData, "model"),
    colors: readListInput(formData.get("colors")),
    condition: text(formData, "condition"),
    sizeSystem: text(formData, "sizeSystem"),
    sizeValue: text(formData, "sizeValue"),
    attributesJson: attributesFromForm(formData),
    measurementsJson: text(formData, "measurementsJson"),
    storageLocation: text(formData, "storageLocation"),
    accessories: text(formData, "accessories"),
    missingParts: text(formData, "missingParts"),
    testingNotes: text(formData, "testingNotes"),
    cleaningNotes: text(formData, "cleaningNotes"),
    listingNotes: text(formData, "listingNotes"),
    sellerAskingPrice: number(formData, "sellerAskingPrice", "Seller asking price", errors),
    purchasePrice: number(formData, "purchasePrice", "Cost", errors),
    estimatedSalePrice: number(formData, "estimatedSalePrice", "Expected price", errors),
    barcode: text(formData, "barcode"),
    notes: text(formData, "notes"),
    ownershipType: choice<Schemas["InventoryOwnershipType"]>(formData, "ownershipType", "Owned"),
    functionalStatus: choice<Schemas["InventoryFunctionalStatus"]>(formData, "functionalStatus", "Unknown"),
    completenessStatus: choice<Schemas["InventoryCompletenessStatus"]>(formData, "completenessStatus", "Unknown"),
    authenticityStatus: choice<Schemas["InventoryAuthenticityStatus"]>(formData, "authenticityStatus", "NotAssessed"),
    packagingStatus: choice<Schemas["InventoryPackagingStatus"]>(formData, "packagingStatus", "NotApplicable"),
    manufacturerPartNumber: text(formData, "manufacturerPartNumber"),
    serialNumber: text(formData, "serialNumber"),
    countryOfOrigin: text(formData, "countryOfOrigin"),
    manufactureYear: number(formData, "manufactureYear", "Year made", errors, { min: 1800, max: 2200, integer: true }),
    length: number(formData, "length", "Length", errors),
    width: number(formData, "width", "Width", errors),
    height: number(formData, "height", "Height", errors),
    dimensionUnit: choice<Schemas["DimensionUnit"]>(formData, "dimensionUnit", "Inches"),
    weight: number(formData, "weight", "Weight", errors),
    weightUnit: choice<Schemas["WeightUnit"]>(formData, "weightUnit", "Ounces"),
  };
  return { fields, errors };
}
