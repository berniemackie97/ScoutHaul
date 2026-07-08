"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type IngestPlatformOrdersRequest = components["schemas"]["IngestPlatformOrdersRequest"];

export type ActionResult = { ok: boolean; message: string };

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

export async function reconcilePlatformOrder(_previous: ActionResult, formData: FormData): Promise<ActionResult> {
  const orderId = formData.get("orderId");
  if (typeof orderId !== "string" || !orderId) return { ok: false, message: "Missing order." };

  let lines: { platformOrderLineId: string; inventoryItemId: string }[] = [];
  try {
    const parsed = JSON.parse((formData.get("mappingJson") as string) ?? "[]");
    if (Array.isArray(parsed)) {
      lines = parsed
        .filter((row) => row && typeof row.platformOrderLineId === "string" && typeof row.inventoryItemId === "string" && row.inventoryItemId)
        .map((row) => ({ platformOrderLineId: row.platformOrderLineId as string, inventoryItemId: row.inventoryItemId as string }));
    }
  } catch {
    return { ok: false, message: "Line mapping could not be read. Reload and try again." };
  }
  if (lines.length === 0) return { ok: false, message: "Match every line to an inventory item first." };

  const uniqueItems = new Set(lines.map((line) => line.inventoryItemId));
  if (uniqueItems.size !== lines.length) return { ok: false, message: "Each inventory item can be matched to only one line." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/{id}/reconcile", {
    params: { path: { id: orderId } },
    body: { lines, notes: (formData.get("notes") as string)?.trim() || null },
  });
  if (!result.data?.saleGroupId) return { ok: false, message: errorMessage(result.error, "The order could not be reconciled.") };

  revalidatePath("/finance/platform-orders");
  revalidatePath("/finance");
  revalidatePath("/inventory");
  const created = result.data.createdSaleCount ?? 0;
  const corrected = result.data.correctedSaleCount ?? 0;
  const parts = [created > 0 ? `${created} new sale${created === 1 ? "" : "s"}` : null, corrected > 0 ? `${corrected} corrected` : null].filter(Boolean);
  return { ok: true, message: `Reconciled: ${parts.join(", ") || "done"}.` };
}

export async function ignorePlatformOrder(_previous: ActionResult, formData: FormData): Promise<ActionResult> {
  const orderId = formData.get("orderId");
  if (typeof orderId !== "string" || !orderId) return { ok: false, message: "Missing order." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/{id}/ignore", { params: { path: { id: orderId } } });
  if (!result.data?.id) return { ok: false, message: errorMessage(result.error, "The order could not be ignored.") };

  revalidatePath("/finance/platform-orders");
  return { ok: true, message: "Order set aside." };
}

/**
 * Import eBay's own Sales report CSV (Seller Hub → Performance → Sales → Download) — the authoritative fee
 * source. Parsed server-side; same idempotent ingest as the extension. This is the primary, zero-setup path.
 */
export async function importEbayCsv(_previous: ActionResult, formData: FormData): Promise<ActionResult> {
  const file = formData.get("csvFile");
  const pasted = (formData.get("csvText") as string)?.trim();
  let csv = "";
  if (file instanceof File && file.size > 0) {
    csv = await file.text();
  } else if (pasted) {
    csv = pasted;
  }
  if (!csv) return { ok: false, message: "Choose the eBay report CSV file (or paste its contents)." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/import/ebay-csv", { body: { platform: "eBay", csv } });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The eBay report could not be imported.") };

  revalidatePath("/finance/platform-orders");
  const { created, updated, skipped } = result.data;
  return { ok: true, message: `Imported ${created} new, updated ${updated}, skipped ${skipped}. Reconcile them below.` };
}

/**
 * Manual fallback for importing scraped orders when the browser extension is not yet connected: the operator
 * pastes the order JSON exactly as the extension would push it. Same idempotent ingest endpoint either way.
 */
export async function importPlatformOrders(_previous: ActionResult, formData: FormData): Promise<ActionResult> {
  const platform = (formData.get("platform") as string)?.trim();
  const raw = (formData.get("ordersJson") as string)?.trim();
  if (!platform) return { ok: false, message: "Choose the platform these orders came from." };
  if (!raw) return { ok: false, message: "Paste the scraped order JSON." };

  let orders: unknown;
  try {
    const parsed = JSON.parse(raw);
    orders = Array.isArray(parsed) ? parsed : (parsed as { orders?: unknown })?.orders;
  } catch {
    return { ok: false, message: "That is not valid JSON. Copy the whole export and try again." };
  }
  if (!Array.isArray(orders) || orders.length === 0) return { ok: false, message: "No orders found in the pasted JSON." };

  const body = { platform, orders } as unknown as IngestPlatformOrdersRequest;
  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/ingest", { body });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The orders could not be imported.") };

  revalidatePath("/finance/platform-orders");
  const { created, updated, skipped } = result.data;
  return { ok: true, message: `Imported ${created} new, updated ${updated}, skipped ${skipped}.` };
}
