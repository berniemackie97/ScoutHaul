"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type RecordBundledSaleRequest = components["schemas"]["RecordBundledSaleRequest"];

export type BundledResult = { ok: boolean; message: string };

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function money(formData: FormData, name: string): number {
  const raw = text(formData, name);
  if (raw === null) return 0;
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : Number.NaN;
}

function errorMessage(error: unknown): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return "The bundled sale could not be recorded.";
}

export async function recordBundledSale(_previous: BundledResult, formData: FormData): Promise<BundledResult> {
  const platform = text(formData, "platform");
  const totalSoldPrice = money(formData, "totalSoldPrice");
  const shippingCharged = money(formData, "shippingCharged");
  if (!platform) return { ok: false, message: "Select a sales channel." };
  if (!Number.isFinite(totalSoldPrice) || totalSoldPrice <= 0) return { ok: false, message: "Enter a positive order total." };
  if (!Number.isFinite(shippingCharged)) return { ok: false, message: "Buyer shipping charged must be a valid non-negative amount." };

  let items: { inventoryItemId: string; manualPrice: number | null }[] = [];
  try {
    const parsed = JSON.parse(text(formData, "itemsJson") ?? "[]");
    if (Array.isArray(parsed)) {
      items = parsed
        .filter((row) => row && typeof row.inventoryItemId === "string")
        .map((row) => ({
          inventoryItemId: row.inventoryItemId as string,
          manualPrice: typeof row.manualPrice === "number" && row.manualPrice >= 0 ? row.manualPrice : null,
        }));
    }
  } catch {
    return { ok: false, message: "Item selection could not be read. Reload and try again." };
  }
  if (items.length < 2) return { ok: false, message: "Pick at least two items for a bundled sale." };

  const fees = ["platformFees", "paymentFees", "shippingCost", "suppliesCost", "otherCosts", "salesTaxCollected"].map((f) => money(formData, f));
  if (fees.some((value) => !Number.isFinite(value))) return { ok: false, message: "Fees, costs, and tax must be valid non-negative amounts." };
  const [platformFees, paymentFees, shippingCost, suppliesCost, otherCosts, salesTaxCollected] = fees;

  const soldDate = text(formData, "soldAt");
  if (soldDate && soldDate > new Date().toISOString().slice(0, 10)) return { ok: false, message: "The sale date cannot be in the future." };

  const body: RecordBundledSaleRequest = {
    items,
    platform,
    totalSoldPrice,
    shippingCharged,
    platformFees,
    paymentFees,
    shippingCost,
    suppliesCost,
    otherCosts,
    salesTaxCollected,
    taxRemittedByPlatform: formData.get("taxRemittedByPlatform") === "on",
    soldAt: soldDate ? new Date(`${soldDate}T12:00:00Z`).toISOString() : new Date().toISOString(),
    orderReference: text(formData, "orderReference"),
    notes: text(formData, "notes"),
  };

  const api = await getApiClient();
  const result = await api.POST("/api/sales/bundled", { body });
  if (!result.data?.saleGroupId) return { ok: false, message: errorMessage(result.error) };

  revalidatePath("/orders");
  revalidatePath("/inventory");
  redirect("/orders");
}
