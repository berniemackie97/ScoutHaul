"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { failedFormState, type FailedFormState, type FieldErrors } from "@/lib/form-state";
import { errorMessage } from "@/lib/api-error";

type ShippingPolicy = components["schemas"]["StoreShippingPolicy"];
type UpdateStoreListingCommerceRequest = components["schemas"]["UpdateStoreListingCommerceRequest"];

export type StorefrontPolicyResult = {
  ok: boolean;
  message: string;
} & FailedFormState;

function textValue(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function moneyValue(formData: FormData, name: string, label: string, fieldErrors: FieldErrors): number | null {
  const value = textValue(formData, name);
  if (value === null) return null;
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    fieldErrors[name] = `${label} must be a valid number.`;
    return null;
  }
  if (parsed < 0) {
    fieldErrors[name] = `${label} cannot be negative.`;
    return null;
  }
  return parsed;
}


export async function updateStorefrontPolicy(
  itemId: string,
  listingId: string,
  _previous: StorefrontPolicyResult,
  formData: FormData,
): Promise<StorefrontPolicyResult> {
  const fieldErrors: FieldErrors = {};
  const acceptsOffers = formData.get("acceptsOffers") === "on";
  const minimumOfferAmount = moneyValue(formData, "minimumOfferAmount", "Minimum offer", fieldErrors);
  const maximumOfferAmount = moneyValue(formData, "maximumOfferAmount", "Maximum offer", fieldErrors);
  const shippingPolicy = (textValue(formData, "shippingPolicy") ?? "StoreDefault") as ShippingPolicy;
  const shippingAmountOverride = moneyValue(formData, "shippingAmountOverride", "Fixed shipping", fieldErrors);

  if (minimumOfferAmount != null && maximumOfferAmount != null && minimumOfferAmount > maximumOfferAmount) {
    fieldErrors.minimumOfferAmount = "Minimum offer cannot be greater than maximum offer.";
  }
  if (shippingPolicy === "FixedAmount" && shippingAmountOverride == null) {
    fieldErrors.shippingAmountOverride = "Fixed shipping requires an amount.";
  }
  if (!["StoreDefault", "Free", "FixedAmount"].includes(shippingPolicy ?? "")) {
    fieldErrors.shippingPolicy = "Choose a supported shipping policy.";
  }
  if (Object.keys(fieldErrors).length > 0) {
    return failedFormState("Fix the highlighted fields.", formData, fieldErrors);
  }

  const body: UpdateStoreListingCommerceRequest = {
    acceptsOffers,
    minimumOfferAmount: acceptsOffers ? minimumOfferAmount : null,
    maximumOfferAmount: acceptsOffers ? maximumOfferAmount : null,
    shippingPolicy,
    shippingAmountOverride: shippingPolicy === "FixedAmount" ? shippingAmountOverride : null,
  };

  const api = await getApiClient();
  const result = await api.PUT("/api/store/listings/{id}/commerce", {
    params: { path: { id: listingId } },
    body,
  });

  if (!result.data) {
    return failedFormState(errorMessage(result.error, "Storefront policy could not be saved."), formData);
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${itemId}`);
  return { ok: true, message: "Storefront policy saved." };
}
