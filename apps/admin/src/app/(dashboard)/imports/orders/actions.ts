"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";
import type { MappingRow } from "@/features/imports/reconcile-model";

export type ActionResult = { ok: boolean; message: string };


export async function reconcilePlatformOrder(
  _previous: ActionResult,
  formData: FormData
): Promise<ActionResult> {
  const orderId = formData.get("orderId");
  if (typeof orderId !== "string" || !orderId) return { ok: false, message: "Missing order." };

  let lines: MappingRow[] = [];
  try {
    const parsed = JSON.parse((formData.get("mappingJson") as string) ?? "[]");
    if (Array.isArray(parsed)) {
      lines = parsed.flatMap<MappingRow>((row) => {
        if (!row || typeof row.platformOrderLineId !== "string") return [];
        if (row.createItem === true) return [{ platformOrderLineId: row.platformOrderLineId as string, createItem: true }];
        return typeof row.inventoryItemId === "string" && row.inventoryItemId
          ? [{ platformOrderLineId: row.platformOrderLineId as string, inventoryItemId: row.inventoryItemId as string }]
          : [];
      });
    }
  } catch {
    return { ok: false, message: "Line mapping could not be read. Reload and try again." };
  }
  if (lines.length === 0)
    return { ok: false, message: "Match every line to an inventory item, or create one from the sale, first." };

  const itemIds = lines.flatMap((line) => (line.inventoryItemId ? [line.inventoryItemId] : []));
  if (new Set(itemIds).size !== itemIds.length)
    return { ok: false, message: "Each inventory item can be matched to only one line." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/{id}/reconcile", {
    params: { path: { id: orderId } },
    body: { lines, notes: (formData.get("notes") as string)?.trim() || null },
  });
  if (!result.data?.saleGroupId)
    return { ok: false, message: errorMessage(result.error, "The order could not be reconciled.") };

  revalidatePath("/imports/orders");
  revalidatePath("/orders");
  revalidatePath("/inventory");
  const created = result.data.createdSaleCount ?? 0;
  const corrected = result.data.correctedSaleCount ?? 0;
  const items = result.data.createdItemCount ?? 0;
  const parts = [
    created > 0 ? `${created} new sale${created === 1 ? "" : "s"}` : null,
    corrected > 0 ? `${corrected} corrected` : null,
    items > 0 ? `${items} item${items === 1 ? "" : "s"} created from the sale — add ${items === 1 ? "its" : "their"} cost in Inventory` : null,
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

/**
 * Settles every waiting order that leaves nothing to decide. The server rebuilds the plan rather than
 * trusting whatever the preview showed, so a page left open for a while cannot write something that
 * has since stopped being true.
 */
export async function autoMatchWaitingOrders(): Promise<{ ok: boolean; message: string }> {
  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/auto-match", {});
  if (!result.data) {
    return { ok: false, message: "Could not match the waiting orders." };
  }

  revalidatePath("/imports/orders");
  revalidatePath("/imports");
  revalidatePath("/orders");
  revalidatePath("/inventory");

  const settled = result.data.settled ?? 0;
  return {
    ok: true,
    message:
      settled === 0
        ? "Nothing could be matched without a decision."
        : `${settled} order${settled === 1 ? "" : "s"} matched and recorded.`,
  };
}

/**
 * Removes an imported order that should not exist — the wrong file, a test order. Refused by the API
 * once the order has been matched, because sales were recorded from it; ignoring is the right tool
 * there, and the refusal says so.
 */
export async function deleteImportedOrder(id: string): Promise<{ ok: boolean; message: string }> {
  const api = await getApiClient();
  const result = await api.DELETE("/api/platform-orders/{id}", { params: { path: { id } } });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "That import row could not be deleted.") };
  }

  revalidatePath("/imports/orders");
  revalidatePath("/imports");
  return { ok: true, message: "Import row deleted." };
}


/**
 * Settles an order the marketplace cancelled after it was recorded as a sale. Reversing undoes the sale — the item
 * goes back into stock and the money comes off the books — so the reason is kept either way.
 */
export async function resolveCancellation(
  _previous: ActionResult,
  formData: FormData
): Promise<ActionResult> {
  const orderId = formData.get("orderId");
  const outcome = formData.get("outcome");
  const note = formData.get("note");
  if (typeof orderId !== "string" || !orderId) return { ok: false, message: "Missing order." };
  if (outcome !== "Reversed" && outcome !== "Kept") return { ok: false, message: "Choose whether the sale is reversed or kept." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-orders/{id}/cancellation", {
    params: { path: { id: orderId } },
    body: { outcome, note: typeof note === "string" && note.trim() ? note.trim() : undefined },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "That cancellation could not be settled.") };

  revalidatePath("/imports/orders");
  return {
    ok: true,
    message: outcome === "Reversed"
      ? "Sale reversed. The item is back in stock."
      : "Sale kept, with your reason on the order.",
  };
}
