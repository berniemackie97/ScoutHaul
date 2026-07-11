"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";

export type ImportResult = { ok: boolean; message: string };

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

async function readCsv(formData: FormData): Promise<string> {
  const file = formData.get("file");
  if (file instanceof File && file.size > 0) return file.text();
  const pasted = (formData.get("csv") as string)?.trim();
  return pasted ?? "";
}

/** eBay orders/sales report → platform orders (the endpoint auto-detects All Orders vs the Sales report). */
export async function importEbayOrders(
  _previous: ImportResult,
  formData: FormData
): Promise<ImportResult> {
  const csv = await readCsv(formData);
  if (!csv) return { ok: false, message: "Choose an eBay report CSV (or paste its contents)." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/import/ebay-csv", {
    body: { platform: "eBay", csv },
  });
  if (!result.data)
    return {
      ok: false,
      message: errorMessage(result.error, "That did not look like an eBay orders report."),
    };

  revalidatePath("/imports");
  revalidatePath("/orders");
  revalidatePath("/finance");
  revalidatePath("/imports/orders");
  const { created, updated, skipped } = result.data;
  return {
    ok: true,
    message: `${created} new · ${updated} updated · ${skipped} skipped. Reconcile below.`,
  };
}

/** eBay Active Listings export → platform listings (seeds/links inventory). */
export async function importEbayListings(
  _previous: ImportResult,
  formData: FormData
): Promise<ImportResult> {
  const csv = await readCsv(formData);
  if (!csv)
    return { ok: false, message: "Choose the eBay Active Listings CSV (or paste its contents)." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-listings/import/ebay-csv", {
    body: { platform: "eBay", csv },
  });
  if (!result.data)
    return {
      ok: false,
      message: errorMessage(result.error, "That did not look like an eBay Active Listings report."),
    };

  revalidatePath("/imports");
  revalidatePath("/imports/listings");
  revalidatePath("/inventory");
  const { created, updated, skipped } = result.data;
  return {
    ok: true,
    message: `${created} new · ${updated} updated · ${skipped} skipped. Reconcile below.`,
  };
}
