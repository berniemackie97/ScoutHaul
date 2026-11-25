"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type WorkWait = components["schemas"]["WorkWait"];

export type WorkActionResult = { ok: boolean; message: string };

export async function setAsideTask(taskKey: string, title: string, wait: WorkWait, until: string | null, note: string): Promise<WorkActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/work/set-aside", { body: { taskKey, title, wait, until, note: note.trim() || null } });
  if (!result.response.ok) return { ok: false, message: errorMessage((result as { error?: unknown }).error, "The task could not be set aside.") };
  revalidatePath("/work");
  return { ok: true, message: wait === "Snoozed" ? "Snoozed. It comes back on its date." : `Waiting on ${wait.toLowerCase()}.` };
}

export async function bringBackTask(taskKey: string): Promise<WorkActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/work/bring-back", { params: { query: { key: taskKey } } });
  if (!result.response.ok) return { ok: false, message: "The task could not be brought back." };
  revalidatePath("/work");
  return { ok: true, message: "Back in its lane." };
}

function refreshWork() {
  revalidatePath("/work");
  revalidatePath("/");
}

/** Records that the listing was ended on the marketplace. Nothing is changed on the marketplace itself. */
export async function confirmDelisted(channelListingId: string): Promise<WorkActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/channel-listings/{channelListingId}/delisted", { params: { path: { channelListingId } } });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "That listing could not be updated.") };
  refreshWork();
  return { ok: true, message: "Marked as taken down." };
}

/** Keeps the listing up on purpose; it is not asked about again. */
export async function keepListing(channelListingId: string): Promise<WorkActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/channel-listings/{channelListingId}/keep-listed", { params: { path: { channelListingId } } });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "That listing could not be updated.") };
  refreshWork();
  return { ok: true, message: "Left listed — not asked about again." };
}

/** Clears a sold listing with no sale recorded for it — a sale recorded some other way, or one that fell through. */
export async function clearSoldListing(platformListingId: string): Promise<WorkActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/platform-listings/{id}/sold-acknowledged", { params: { path: { id: platformListingId } } });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "That listing could not be updated.") };
  refreshWork();
  return { ok: true, message: "Cleared — no sale recorded for it." };
}
