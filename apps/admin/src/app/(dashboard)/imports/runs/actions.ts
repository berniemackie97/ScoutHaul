"use server";

import { redirect } from "next/navigation";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

export type RerunState = { error: string | null };

/** Reads a past import's exact file again, then opens the new run. */
export async function rerunImport(runId: string, _previous: RerunState): Promise<RerunState> {
  const api = await getApiClient();
  const { data, error } = await api.POST("/api/imports/runs/{id}/rerun", { params: { path: { id: runId } } });
  if (!data?.id) return { error: errorMessage(error, "The import could not be run again.") };
  redirect(`/imports/runs/${data.id}`);
}
