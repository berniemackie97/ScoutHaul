"use server";

import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type GeneratedListing = components["schemas"]["GeneratedListingDto"];
type GenerateListingRequest = components["schemas"]["GenerateListingRequest"];

export type ListingGenResult = { ok: boolean; message: string; listing?: GeneratedListing };

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
