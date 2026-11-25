"use server";

import type { components } from "@overhaul/api-client";
import { getApiAssetUrl, getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

export type MatchCandidate = components["schemas"]["ListingMatchCandidateDto"];
export type MatchResult = { ok: boolean; message: string; candidates: MatchCandidate[] };

export async function loadMatchCandidates(listingId: string, search: string): Promise<MatchResult> {
  const api = await getApiClient();
  const term = search.trim();
  const result = await api.GET("/api/platform-listings/{id}/match-candidates", {
    params: { path: { id: listingId }, query: term ? { search: term } : {} },
  });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "Could not load matches. Try again."), candidates: [] };
  }
  // Thumbnails are private; the admin's own photo route serves them to a signed-in operator.
  const candidates = (result.data.candidates ?? []).map((candidate) => ({ ...candidate, thumbnailUrl: getApiAssetUrl(candidate.thumbnailUrl) }));
  return { ok: true, message: "", candidates };
}
