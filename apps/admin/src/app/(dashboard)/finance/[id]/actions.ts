"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type UpdateSaleRequest = components["schemas"]["UpdateSaleRequest"];

export type SaleActionResult = { ok: boolean; message: string };

function textValue(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : null;
}

function numberValue(formData: FormData, name: string): number {
  const value = textValue(formData, name);
  if (value === null) return 0;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : 0;
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

export async function updateSale(
  id: string,
  _previous: SaleActionResult,
  formData: FormData,
): Promise<SaleActionResult> {
  const reason = textValue(formData, "reason");
  if (!reason) return { ok: false, message: "A reason is required to correct a sale." };

  const platform = textValue(formData, "platform");
  if (!platform) return { ok: false, message: "Channel / platform is required." };

  const soldPrice = numberValue(formData, "soldPrice");
  if (soldPrice <= 0) return { ok: false, message: "Sold price must be greater than zero." };

  const soldAtRaw = textValue(formData, "soldAt");
  const soldAt = soldAtRaw ? new Date(`${soldAtRaw}T12:00:00Z`).toISOString() : new Date().toISOString();

  const body: UpdateSaleRequest = {
    platform,
    soldPrice,
    platformFees: numberValue(formData, "platformFees"),
    paymentFees: numberValue(formData, "paymentFees"),
    shippingCost: numberValue(formData, "shippingCost"),
    suppliesCost: numberValue(formData, "suppliesCost"),
    otherCosts: numberValue(formData, "otherCosts"),
    salesTaxCollected: numberValue(formData, "salesTaxCollected"),
    taxRemittedByPlatform: formData.get("taxRemittedByPlatform") === "on",
    soldAt,
    notes: textValue(formData, "notes"),
    reason,
  };

  const api = await getApiClient();
  const result = await api.PUT("/api/sales/{id}", { params: { path: { id } }, body });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "Could not save the correction.") };
  }

  revalidatePath("/finance");
  revalidatePath(`/finance/${id}`);
  return { ok: true, message: `Saved. True net is now ${money(result.data.netProfit)}.` };
}
