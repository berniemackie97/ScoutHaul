"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type ListingDifference = components["schemas"]["ListingDifferenceDto"];
type Field = components["schemas"]["ListingDetailField"];

export type TakeDifferenceResult = { ok: boolean; message: string; differences?: ListingDifference[] };

export async function takeListingDifference(itemId: string, listingId: string, field: Field): Promise<TakeDifferenceResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/inventory/{id}/listing-differences", { params: { path: { id: itemId } }, body: { listingId, field } });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "Could not take that value. Reload and try again.") };
  }
  revalidatePath(`/inventory/${itemId}`);
  return { ok: true, message: "Updated.", differences: result.data };
}
