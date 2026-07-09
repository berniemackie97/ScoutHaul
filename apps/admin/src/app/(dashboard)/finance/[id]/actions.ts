"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { failedFormState, type FailedFormState, type FieldErrors } from "@/lib/form-state";

type UpdateSaleRequest = components["schemas"]["UpdateSaleRequest"];
type SaleStatusTransitionRequest = components["schemas"]["SaleStatusTransitionRequest"];
type SaleStatus = components["schemas"]["SaleStatus"];
type RecordRefundRequest = components["schemas"]["RecordRefundRequest"];

export type SaleActionResult = { ok: boolean; message: string } & FailedFormState;

type NumberOptions = {
  required?: boolean;
  positive?: boolean;
};

function textValue(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : null;
}

function failure(message: string, formData: FormData, fieldErrors: FieldErrors = {}): SaleActionResult {
  const result = failedFormState(message, formData, fieldErrors);
  return {
    ...result,
    values: {
      ...(result.values ?? {}),
      taxRemittedByPlatform: formData.get("taxRemittedByPlatform") === "on" ? "on" : "off",
    },
  };
}

function numberValue(formData: FormData, name: string, label: string, fieldErrors: FieldErrors, options: NumberOptions = {}): number | null {
  const value = textValue(formData, name);
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

const money = (value: number | null | undefined) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value ?? 0);

export async function updateSale(id: string, _previous: SaleActionResult, formData: FormData): Promise<SaleActionResult> {
  const fieldErrors: FieldErrors = {};
  const reason = textValue(formData, "reason");
  if (!reason) fieldErrors.reason = "A reason is required to correct a sale.";

  const platform = textValue(formData, "platform");
  if (!platform) fieldErrors.platform = "Channel / platform is required.";

  const soldPrice = numberValue(formData, "soldPrice", "Sold price", fieldErrors, { required: true, positive: true });
  const shippingCharged = numberValue(formData, "shippingCharged", "Buyer shipping charged", fieldErrors);
  const platformFees = numberValue(formData, "platformFees", "Platform fees", fieldErrors);
  const paymentFees = numberValue(formData, "paymentFees", "Payment fees", fieldErrors);
  const shippingCost = numberValue(formData, "shippingCost", "Shipping label cost", fieldErrors);
  const suppliesCost = numberValue(formData, "suppliesCost", "Supplies cost", fieldErrors);
  const otherCosts = numberValue(formData, "otherCosts", "Other costs", fieldErrors);
  const salesTaxCollected = numberValue(formData, "salesTaxCollected", "Sales tax collected", fieldErrors);

  const soldAtRaw = textValue(formData, "soldAt");
  const soldAtDate = soldAtRaw ? new Date(`${soldAtRaw}T12:00:00Z`) : new Date();
  if (Number.isNaN(soldAtDate.getTime()) || (soldAtRaw !== null && soldAtRaw > new Date().toISOString().slice(0, 10))) {
    fieldErrors.soldAt = "Enter a valid sale date that is not in the future.";
  }
  if (Object.keys(fieldErrors).length > 0) {
    return failure("Fix the highlighted fields.", formData, fieldErrors);
  }
  const soldAt = soldAtDate.toISOString();

  const body: UpdateSaleRequest = {
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
    soldAt,
    notes: textValue(formData, "notes"),
    reason: reason!,
  };

  const api = await getApiClient();
  const result = await api.PUT("/api/sales/{id}", { params: { path: { id } }, body });
  if (!result.data) {
    return failure(errorMessage(result.error, "Could not save the correction."), formData);
  }

  revalidatePath("/finance");
  revalidatePath(`/finance/${id}`);
  return { ok: true, message: `Saved. True net is now ${money(result.data.netProfit)}.` };
}

export async function transitionSale(id: string, _previous: SaleActionResult, formData: FormData): Promise<SaleActionResult> {
  const status = textValue(formData, "status") as SaleStatus | null;
  if (!status) return { ok: false, message: "Select the next sale status." };
  const occurredAtRaw = textValue(formData, "occurredAt");
  const occurredAtDate = occurredAtRaw ? new Date(occurredAtRaw) : new Date();
  if (Number.isNaN(occurredAtDate.getTime())) {
    return { ok: false, message: "Enter a valid transition date and time." };
  }
  const body: SaleStatusTransitionRequest = {
    status,
    occurredAt: occurredAtDate.toISOString(),
    reason: textValue(formData, "reason"),
    carrier: textValue(formData, "carrier"),
    trackingNumber: textValue(formData, "trackingNumber"),
  };
  const api = await getApiClient();
  const result = await api.PATCH("/api/sales/{id}/status", { params: { path: { id } }, body });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "Could not update sale status.") };
  revalidatePath("/finance");
  revalidatePath(`/finance/${id}`);
  revalidatePath("/inventory");
  return { ok: true, message: `Sale moved to ${result.data.status}.` };
}

export async function recordRefund(id: string, _previous: SaleActionResult, formData: FormData): Promise<SaleActionResult> {
  const fieldErrors: FieldErrors = {};
  const reason = textValue(formData, "reason");
  if (!reason) fieldErrors.reason = "A reason is required for a refund.";

  const itemAmount = numberValue(formData, "itemAmount", "Item amount refunded", fieldErrors) ?? 0;
  const shippingRefunded = numberValue(formData, "shippingRefunded", "Shipping refunded", fieldErrors) ?? 0;
  const taxRefunded = numberValue(formData, "taxRefunded", "Tax refunded", fieldErrors) ?? 0;
  const feeCredit = numberValue(formData, "feeCredit", "Fee credit", fieldErrors) ?? 0;
  if (itemAmount + shippingRefunded + taxRefunded + feeCredit <= 0) {
    fieldErrors.itemAmount = "Enter at least one non-zero refund amount.";
  }

  const refundedAtRaw = textValue(formData, "refundedAt");
  const refundedAtDate = refundedAtRaw ? new Date(`${refundedAtRaw}T12:00:00Z`) : new Date();
  if (Number.isNaN(refundedAtDate.getTime())) fieldErrors.refundedAt = "Enter a valid refund date.";
  if (Object.keys(fieldErrors).length > 0) return failure("Fix the highlighted fields.", formData, fieldErrors);

  const itemReturned = formData.get("itemReturned") === "on";
  const body: RecordRefundRequest = {
    type: itemReturned || formData.get("full") === "on" ? "Full" : "Partial",
    itemAmount,
    shippingRefunded,
    taxRefunded,
    feeCredit,
    itemReturned,
    reason: reason!,
    refundedAt: refundedAtDate.toISOString(),
    notes: textValue(formData, "notes"),
  };

  const api = await getApiClient();
  const result = await api.POST("/api/sales/{id}/refunds", { params: { path: { id } }, body });
  if (!result.data) return failure(errorMessage(result.error, "Could not record the refund."), formData);

  revalidatePath("/finance");
  revalidatePath(`/finance/${id}`);
  revalidatePath("/inventory");
  return { ok: true, message: `Refund recorded. True net is now ${money(result.data.netProfit)}.` };
}

export async function voidRefund(id: string, refundId: string): Promise<void> {
  const api = await getApiClient();
  await api.DELETE("/api/sales/{id}/refunds/{refundId}", { params: { path: { id, refundId } } });
  revalidatePath("/finance");
  revalidatePath(`/finance/${id}`);
  revalidatePath("/inventory");
}
