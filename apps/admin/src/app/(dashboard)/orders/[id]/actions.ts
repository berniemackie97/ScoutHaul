"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type OrderStatus = components["schemas"]["OrderStatus"];

export type OrderActionResult = { ok: boolean; message: string };

export async function updateOrderStatus(id: string, status: OrderStatus): Promise<OrderActionResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/orders/{id}/status", { params: { path: { id } }, body: { status } });
  if (!result.data) return { ok: false, message: "The order status could not be updated." };

  revalidatePath("/orders");
  revalidatePath(`/orders/${id}`);
  return { ok: true, message: `Order marked ${status}.` };
}
