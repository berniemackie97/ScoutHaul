"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";

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

export async function reconcilePlatformOrder(
  _previous: ActionResult,
  formData: FormData
): Promise<ActionResult> {
  const orderId = formData.get("orderId");
  if (typeof orderId !== "string" || !orderId) return { ok: false, message: "Missing order." };

  let lines: { platformOrderLineId: string; inventoryItemId: string }[] = [];
  try {
    const parsed = JSON.parse((formData.get("mappingJson") as string) ?? "[]");
    if (Array.isArray(parsed)) {
      lines = parsed
        .filter(
          (row) =>
            row &&
            typeof row.platformOrderLineId === "string" &&
            typeof row.inventoryItemId === "string" &&
            row.inventoryItemId
        )
        .map((row) => ({
          platformOrderLineId: row.platformOrderLineId as string,
          inventoryItemId: row.inventoryItemId as string,
        }));
    }
  } catch {
    return { ok: false, message: "Line mapping could not be read. Reload and try again." };
  }
  if (lines.length === 0)
    return { ok: false, message: "Match every line to an inventory item first." };

  const uniqueItems = new Set(lines.map((line) => line.inventoryItemId));
  if (uniqueItems.size !== lines.length)
    return { ok: false, message: "Each inventory item can be matched to only one line." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/{id}/reconcile", {
    params: { path: { id: orderId } },
    body: { lines, notes: (formData.get("notes") as string)?.trim() || null },
  });
  if (!result.data?.saleGroupId)
    return { ok: false, message: errorMessage(result.error, "The order could not be reconciled.") };

  revalidatePath("/imports/orders");
  revalidatePath("/finance");
  revalidatePath("/inventory");
  const created = result.data.createdSaleCount ?? 0;
  const corrected = result.data.correctedSaleCount ?? 0;
  const parts = [
    created > 0 ? `${created} new sale${created === 1 ? "" : "s"}` : null,
    corrected > 0 ? `${corrected} corrected` : null,
  ].filter(Boolean);
  return { ok: true, message: `Reconciled: ${parts.join(", ") || "done"}.` };
}

export async function ignorePlatformOrder(
  _previous: ActionResult,
  formData: FormData
): Promise<ActionResult> {
  const orderId = formData.get("orderId");
  if (typeof orderId !== "string" || !orderId) return { ok: false, message: "Missing order." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/{id}/ignore", {
    params: { path: { id: orderId } },
  });
  if (!result.data?.id)
    return { ok: false, message: errorMessage(result.error, "The order could not be ignored.") };

  revalidatePath("/imports/orders");
  return { ok: true, message: "Order set aside." };
}
