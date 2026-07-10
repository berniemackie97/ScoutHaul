"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type IngestPlatformListingsRequest = components["schemas"]["IngestPlatformListingsRequest"];

export type ListingActionResult = { ok: boolean; message: string };

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

export async function reconcileListing(listingId: string, inventoryItemId: string | null): Promise<void> {
  const api = await getApiClient();
  await api.POST("/api/platform-listings/{id}/reconcile", {
    params: { path: { id: listingId } },
    body: inventoryItemId ? { inventoryItemId } : { createNewItem: true },
  });
  revalidatePath("/listings");
  revalidatePath("/inventory");
}

export async function ignoreListing(listingId: string): Promise<void> {
  const api = await getApiClient();
  await api.POST("/api/platform-listings/{id}/ignore", { params: { path: { id: listingId } } });
  revalidatePath("/listings");
}

/** Manual fallback for importing intentionally captured or normalized listing records. */
export async function importListings(_previous: ListingActionResult, formData: FormData): Promise<ListingActionResult> {
  const raw = (formData.get("listingsJson") as string)?.trim();
  if (!raw) return { ok: false, message: "Paste the listing JSON." };

  let listings: unknown;
  try {
    const parsed = JSON.parse(raw);
    listings = Array.isArray(parsed) ? parsed : (parsed as { listings?: unknown })?.listings;
  } catch {
    return { ok: false, message: "That is not valid JSON. Copy the whole export and try again." };
  }
  if (!Array.isArray(listings) || listings.length === 0) return { ok: false, message: "No listings found in the pasted JSON." };

  const body = { platform: "eBay", listings } as unknown as IngestPlatformListingsRequest;
  const api = await getApiClient();
  const result = await api.POST("/api/platform-listings/ingest", { body });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The listings could not be imported.") };

  revalidatePath("/listings");
  const { created, updated, skipped } = result.data;
  return { ok: true, message: `Imported ${created} new, updated ${updated}, skipped ${skipped}. Reconcile them below.` };
}
