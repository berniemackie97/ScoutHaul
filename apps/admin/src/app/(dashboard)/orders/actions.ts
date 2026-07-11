"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";

export type MergeOrdersResult = { ok: boolean; message: string };

export async function mergeOrders(orderIds: string[]): Promise<MergeOrdersResult> {
  if (orderIds.length < 2) {
    return { ok: false, message: "Select at least two orders to merge." };
  }

  const api = await getApiClient();
  const result = await api.POST("/api/orders/merge", { body: { orderIds } });
  if (result.error || !result.data) {
    return { ok: false, message: "The selected orders could not be merged." };
  }

  revalidatePath("/orders");
  return {
    ok: true,
    message: `Merged ${orderIds.length} orders into ${result.data.orderNumber}.`,
  };
}
