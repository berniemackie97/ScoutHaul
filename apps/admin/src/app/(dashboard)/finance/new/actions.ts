"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type RecordSaleRequest = components["schemas"]["RecordSaleRequest"];

export type CreateSaleResult = { ok: boolean; message: string };

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

export async function createSale(_previous: CreateSaleResult, formData: FormData): Promise<CreateSaleResult> {
  const inventoryItemId = text(formData, "inventoryItemId");
  const platform = text(formData, "platform");
  const soldPrice = money(formData, "soldPrice");
  if (!inventoryItemId) return { ok: false, message: "Select an inventory item." };
  if (!platform) return { ok: false, message: "Select a sales channel." };
  if (soldPrice === null || soldPrice <= 0) return { ok: false, message: "Sold price must be greater than zero." };

  const platformFees = money(formData, "platformFees");
  const paymentFees = money(formData, "paymentFees");
  const shippingCost = money(formData, "shippingCost");
  const suppliesCost = money(formData, "suppliesCost");
  const otherCosts = money(formData, "otherCosts");
  const salesTaxCollected = money(formData, "salesTaxCollected");
  if ([platformFees, paymentFees, shippingCost, suppliesCost, otherCosts, salesTaxCollected].some((value) => value === null)) {
    return { ok: false, message: "Fees, costs, and tax must be valid non-negative amounts." };
  }

  const soldDate = text(formData, "soldAt");
  const soldAt = soldDate ? new Date(`${soldDate}T12:00:00Z`) : new Date();
  if (Number.isNaN(soldAt.getTime()) || (soldDate !== null && soldDate > new Date().toISOString().slice(0, 10))) {
    return { ok: false, message: "Enter a valid sale date that is not in the future." };
  }
  const body: RecordSaleRequest = {
    inventoryItemId,
    listingId: text(formData, "listingId"),
    platform,
    soldPrice,
    platformFees: platformFees!,
    paymentFees: paymentFees!,
    shippingCost: shippingCost!,
    suppliesCost: suppliesCost!,
    otherCosts: otherCosts!,
    salesTaxCollected: salesTaxCollected!,
    taxRemittedByPlatform: formData.get("taxRemittedByPlatform") === "on",
    soldAt: soldAt.toISOString(),
    notes: text(formData, "notes"),
  };

  const api = await getApiClient();
  const result = await api.POST("/api/sales", { body });
  if (!result.data?.id) return { ok: false, message: errorMessage(result.error) };

  revalidatePath("/finance");
  revalidatePath("/inventory");
  redirect(`/finance/${result.data.id}`);
}
