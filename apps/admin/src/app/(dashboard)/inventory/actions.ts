"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type InventoryStatus = components["schemas"]["InventoryStatus"];

export type BulkInventoryResult = { ok: boolean; message: string };

const MAX_ITEMS = 200;

function clean(ids: string[]) {
  return [...new Set(ids.map((id) => id.trim()).filter(Boolean))];
}

/** Each item answers for itself, so one refusal never blocks the rest; the first few reasons are reported. */
function summarize(verb: string, done: number, refusals: Array<{ title: string; message: string }>): BulkInventoryResult {
  const head = `${verb} ${done} item${done === 1 ? "" : "s"}.`;
  if (refusals.length === 0) return { ok: true, message: head };
  const detail = refusals.slice(0, 2).map((refusal) => `${refusal.title}: ${refusal.message}`).join(" ");
  const more = refusals.length > 2 ? ` …and ${refusals.length - 2} more.` : "";
  return { ok: done > 0, message: `${head} ${refusals.length} refused. ${detail}${more}` };
}

export async function archiveInventoryItems(items: Array<{ id: string; title: string }>): Promise<BulkInventoryResult> {
  const targets = items.filter((item) => item.id).slice(0, MAX_ITEMS);
  if (targets.length === 0) return { ok: false, message: "Select at least one item." };

  const api = await getApiClient();
  const refusals: Array<{ title: string; message: string }> = [];
  let done = 0;
  for (const item of targets) {
    const result = await api.DELETE("/api/inventory/{id}", { params: { path: { id: item.id }, query: { hard: false } } });
    if (result.response.ok) done += 1;
    else refusals.push({ title: item.title, message: errorMessage(result.error, "could not be archived") });
  }

  revalidatePath("/inventory");
  return summarize("Archived", done, refusals);
}

export async function moveInventoryItems(ids: string[], location: string): Promise<BulkInventoryResult> {
  const inventoryItemIds = clean(ids);
  if (inventoryItemIds.length === 0) return { ok: false, message: "Select at least one item." };

  const api = await getApiClient();
  const { data, error } = await api.POST("/api/inventory/location", {
    body: { inventoryItemIds, storageLocation: location.trim() || null },
  });
  if (!data) return { ok: false, message: errorMessage(error, "The items could not be moved.") };

  revalidatePath("/inventory");
  const where = location.trim() ? `to ${location.trim()}` : "out of their location";
  return { ok: true, message: `Moved ${data.updated} item${data.updated === 1 ? "" : "s"} ${where}.` };
}

export async function setInventoryStage(items: Array<{ id: string; title: string }>, status: InventoryStatus): Promise<BulkInventoryResult> {
  const targets = items.filter((item) => item.id).slice(0, MAX_ITEMS);
  if (targets.length === 0) return { ok: false, message: "Select at least one item." };

  const api = await getApiClient();
  const refusals: Array<{ title: string; message: string }> = [];
  let done = 0;
  for (const item of targets) {
    const result = await api.PATCH("/api/inventory/{id}/status", { params: { path: { id: item.id } }, body: { status } });
    if (result.data) done += 1;
    else refusals.push({ title: item.title, message: errorMessage(result.error, "could not be moved") });
  }

  revalidatePath("/inventory");
  return summarize("Updated", done, refusals);
}
