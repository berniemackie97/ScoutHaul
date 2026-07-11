"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type RecordSaleRequest = components["schemas"]["RecordSaleRequest"];

export type SellResult = { ok: boolean; message: string };

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function money(formData: FormData, name: string): number | null {
  const parsed = Number(text(formData, name) ?? 0);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

function errorMessage(error: unknown): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return "The sale could not be recorded.";
}

/** Record a sale for one item, in place — this is the item's "Sold" transition (no page hop). */
export async function recordSaleForItem(itemId: string, _previous: SellResult, formData: FormData): Promise<SellResult> {
  const platform = text(formData, "platform");
  const soldPrice = money(formData, "soldPrice");
  if (!platform) return { ok: false, message: "Select a sales channel." };
  if (soldPrice === null || soldPrice <= 0) return { ok: false, message: "Sold price must be greater than zero." };

  const amounts = ["shippingCharged", "platformFees", "paymentFees", "shippingCost", "suppliesCost", "otherCosts", "salesTaxCollected"].map((field) => money(formData, field));
  if (amounts.some((value) => value === null)) {
    return { ok: false, message: "Revenue, fees, costs, and tax must be valid non-negative amounts." };
  }
  const [shippingCharged, platformFees, paymentFees, shippingCost, suppliesCost, otherCosts, salesTaxCollected] = amounts as number[];

  const soldDate = text(formData, "soldAt");
  if (soldDate && soldDate > new Date().toISOString().slice(0, 10)) {
    return { ok: false, message: "The sale date cannot be in the future." };
  }
  const soldAt = soldDate ? new Date(`${soldDate}T12:00:00Z`) : new Date();

  const body: RecordSaleRequest = {
    inventoryItemId: itemId,
    listingId: null,
    platform,
    soldPrice,
    shippingCharged,
    platformFees,
    paymentFees,
    shippingCost,
    suppliesCost,
    otherCosts,
    salesTaxCollected,
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
  if (!result.data?.id) return { ok: false, message: errorMessage(result.error) };

  revalidatePath(`/inventory/${itemId}`);
  revalidatePath("/inventory");
  revalidatePath("/orders");
  return { ok: true, message: "Sale recorded — item marked sold." };
}
