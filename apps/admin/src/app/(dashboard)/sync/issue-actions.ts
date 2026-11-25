"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

export type IssueActionResult = { ok: boolean; message: string };

export async function resolveIssue(issueId: string, dismiss: boolean, note: string): Promise<IssueActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/sync/issues/{id}/resolve", { params: { path: { id: issueId } }, body: { dismiss, note: note.trim() || null } });
  if (!result.response.ok) return { ok: false, message: errorMessage((result as { error?: unknown }).error, "The issue could not be closed.") };
  revalidatePath("/sync");
  return { ok: true, message: dismiss ? "Dismissed." : "Resolved." };
}

export async function resolveIssueGroup(platform: string, code: string, dismiss: boolean): Promise<IssueActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/sync/issues/resolve-group", { body: { platform, code, dismiss } });
  if (result.data === undefined) return { ok: false, message: errorMessage(result.error, "The issues could not be closed.") };
  revalidatePath("/sync");
  return { ok: true, message: `${result.data} ${result.data === 1 ? "issue" : "issues"} closed.` };
}

export async function retryIssue(issueId: string): Promise<IssueActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/sync/issues/{id}/retry", { params: { path: { id: issueId } } });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The retry could not be requested.") };
  revalidatePath("/sync");
  const queued = result.data.requests?.length ?? 0;
  const why = (result.data.skipped ?? []).map((item) => item.reason).join(" ");
  return queued > 0
    ? { ok: true, message: "Scan queued. The issue closes on its own once a scan shows it fixed." }
    : { ok: false, message: why || "Nothing could be queued." };
}

export async function loadIssueSnapshot(issueId: string): Promise<{ detail: string | null; snapshot: string | null }> {
  const api = await getApiClient();
  const result = await api.GET("/api/sync/issues/{id}/snapshot", { params: { path: { id: issueId } } });
  return { detail: result.data?.detail ?? null, snapshot: result.data?.snapshot ?? null };
}
