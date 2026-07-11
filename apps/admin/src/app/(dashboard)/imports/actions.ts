"use server";

import { revalidatePath } from "next/cache";
import { getApiAccessToken, getApiBaseUrl, getApiClient } from "@/lib/api";

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
  revalidatePath("/imports/orders");
  const { created, updated, skipped } = result.data;
  return {
    ok: true,
    message: `${created} new · ${updated} updated · ${skipped} skipped. Reconcile below.`,
  };
}

/** Marketplace refund exports → posted refunds on exact sale matches. Ambiguous rows are skipped. */
export async function importMarketplaceRefunds(
  _previous: ImportResult,
  formData: FormData
): Promise<ImportResult> {
  const csv = await readCsv(formData);
  if (!csv)
    return { ok: false, message: "Choose a Depop Sales or eBay Listing Sales CSV." };

  const token = await getApiAccessToken();
  const response = await fetch(`${getApiBaseUrl()}/api/platform-orders/import/refunds-csv`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ platform: "Marketplace", csv }),
  });

  if (!response.ok) {
    const text = await response.text();
    return { ok: false, message: text || "That did not look like a supported refund export." };
  }

  const result = (await response.json()) as {
    imported: number;
    skipped: number;
    warnings?: string[];
  };

  revalidatePath("/imports");
  revalidatePath("/orders");
  revalidatePath("/analytics");
  const warningText = result.warnings?.length ? ` ${result.warnings.length} warning(s).` : "";
  return {
    ok: true,
    message: `${result.imported} refund(s) imported · ${result.skipped} skipped.${warningText}`,
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
