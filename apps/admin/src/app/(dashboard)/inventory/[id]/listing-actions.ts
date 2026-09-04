"use server";

import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type GeneratedListing = components["schemas"]["GeneratedListingDto"];
type GenerateListingRequest = components["schemas"]["GenerateListingRequest"];

export type ListingGenResult = { ok: boolean; message: string; listing?: GeneratedListing };


export async function generateListing(request: GenerateListingRequest): Promise<ListingGenResult> {
  if (!request.title?.trim()) {
    return { ok: false, message: "The item needs a title before a listing can be generated." };
  }

  const api = await getApiClient();
  const result = await api.POST("/api/listing/generate", { body: request });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "Could not generate a listing. Try again in a moment.") };
  }
  return { ok: true, message: "", listing: result.data };
}
