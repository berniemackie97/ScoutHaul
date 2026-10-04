"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

export type MergeOrdersResult = { ok: boolean; message: string };
export type BulkStep = "markPacked" | "markShipped" | "markDelivered";

export async function mergeOrders(orderIds: string[]): Promise<MergeOrdersResult> {
  if (orderIds.length < 2) {
    return { ok: false, message: "Select at least two orders to merge." };
  }

  const api = await getApiClient();
  const { data, error } = await api.POST("/api/orders/merge", { body: { orderIds } });
  if (!data) {
    return { ok: false, message: errorMessage(error, "The selected orders could not be merged.") };
  }

  revalidatePath("/orders");
  return {
    ok: true,
    message: `Merged ${orderIds.length} orders into ${data.orderNumber}.`,
  };
}

const STEP_LABEL: Record<BulkStep, string> = {
  markPacked: "packed",
  markShipped: "shipped",
  markDelivered: "delivered",
};

/** One step for many orders. Each order is gated on its own, so one refusal never blocks the rest. */
export async function advanceOrders(
  orders: Array<{ id: string; orderNumber: string }>,
  step: BulkStep,
): Promise<MergeOrdersResult> {
  const api = await getApiClient();
  const refusals: string[] = [];
  let moved = 0;
  for (const order of orders) {
    const result = await api.PATCH("/api/orders/{id}/tracking", {
      params: { path: { id: order.id } },
      body: { markPacked: step === "markPacked", markShipped: step === "markShipped", markDelivered: step === "markDelivered" },
    });
    if (result.data) moved += 1;
    else refusals.push(`${order.orderNumber}: ${errorMessage(result.error, "could not be updated")}`);
  }

  revalidatePath("/orders");
  const done = `${moved} of ${orders.length} marked ${STEP_LABEL[step]}.`;
  return refusals.length === 0
    ? { ok: true, message: done }
    : { ok: moved > 0, message: `${done} ${refusals.slice(0, 3).join(" ")}${refusals.length > 3 ? ` …and ${refusals.length - 3} more.` : ""}` };
}
