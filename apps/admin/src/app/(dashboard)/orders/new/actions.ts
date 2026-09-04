"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { failedFormState, type FailedFormState, type FieldErrors } from "@/lib/form-state";
import { errorMessage } from "@/lib/api-error";

type RecordSaleRequest = components["schemas"]["RecordSaleRequest"];

export type CreateSaleResult = { ok: boolean; message: string } & FailedFormState;

type MoneyOptions = {
  required?: boolean;
  positive?: boolean;
};

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function failure(message: string, formData: FormData, fieldErrors: FieldErrors = {}): CreateSaleResult {
  const result = failedFormState(message, formData, fieldErrors);
  return {
    ...result,
    values: {
      ...(result.values ?? {}),
      taxRemittedByPlatform: formData.get("taxRemittedByPlatform") === "on" ? "on" : "off",
    },
  };
}

function moneyField(formData: FormData, name: string, label: string, fieldErrors: FieldErrors, options: MoneyOptions = {}): number | null {
  const value = text(formData, name);
  if (value === null) {
    if (options.required) fieldErrors[name] = `${label} is required.`;
    return options.required ? null : 0;
  }

  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    fieldErrors[name] = `${label} must be a valid amount.`;
    return null;
  }
  if (parsed < 0) {
    fieldErrors[name] = `${label} cannot be negative.`;
    return null;
  }
  if (options.positive && parsed <= 0) {
    fieldErrors[name] = `${label} must be greater than zero.`;
    return null;
  }
  return parsed;
}


export async function createSale(_previous: CreateSaleResult, formData: FormData): Promise<CreateSaleResult> {
  const inventoryItemId = text(formData, "inventoryItemId");
  const platform = text(formData, "platform");
  const fieldErrors: FieldErrors = {};
  if (!inventoryItemId) fieldErrors.inventoryItemId = "Select an inventory item.";
  if (!platform) fieldErrors.platform = "Select a sales channel.";

  const soldPrice = moneyField(formData, "soldPrice", "Sold price", fieldErrors, { required: true, positive: true });
  const shippingCharged = moneyField(formData, "shippingCharged", "Buyer shipping charged", fieldErrors);
  const platformFees = moneyField(formData, "platformFees", "Platform fees", fieldErrors);
  const paymentFees = moneyField(formData, "paymentFees", "Payment fees", fieldErrors);
  const shippingCost = moneyField(formData, "shippingCost", "Shipping label cost", fieldErrors);
  const suppliesCost = moneyField(formData, "suppliesCost", "Packing supplies", fieldErrors);
  const otherCosts = moneyField(formData, "otherCosts", "Other costs", fieldErrors);
  const salesTaxCollected = moneyField(formData, "salesTaxCollected", "Sales tax collected", fieldErrors);

  const soldDate = text(formData, "soldAt");
  const soldAt = soldDate ? new Date(`${soldDate}T12:00:00Z`) : new Date();
  if (Number.isNaN(soldAt.getTime()) || (soldDate !== null && soldDate > new Date().toISOString().slice(0, 10))) {
    fieldErrors.soldAt = "Enter a valid sale date that is not in the future.";
  }
  if (Object.keys(fieldErrors).length > 0) {
    return failure("Fix the highlighted fields.", formData, fieldErrors);
  }

  const body: RecordSaleRequest = {
    inventoryItemId: inventoryItemId!,
    listingId: text(formData, "listingId"),
    platform: platform!,
    soldPrice: soldPrice!,
    shippingCharged: shippingCharged!,
    platformFees: platformFees!,
    paymentFees: paymentFees!,
    shippingCost: shippingCost!,
    suppliesCost: suppliesCost!,
    otherCosts: otherCosts!,
    salesTaxCollected: salesTaxCollected!,
    taxRemittedByPlatform: formData.get("taxRemittedByPlatform") === "on",
    soldAt: soldAt.toISOString(),
    notes: text(formData, "notes"),
    externalOrderId: text(formData, "externalOrderId"),
    buyerReference: text(formData, "buyerReference"),
    buyerName: text(formData, "buyerName"),
    shippingCarrier: text(formData, "shippingCarrier"),
    trackingNumber: text(formData, "trackingNumber"),
  };

  const api = await getApiClient();
  const result = await api.POST("/api/sales", { body });
  if (!result.data?.id) return failure(errorMessage(result.error, "The sale could not be recorded."), formData);

  revalidatePath("/orders");
  revalidatePath("/inventory");
  // Land on the order the sale belongs to — the sale is managed inline there now.
  redirect(result.data.orderId ? `/orders/${result.data.orderId}` : "/orders");
}
