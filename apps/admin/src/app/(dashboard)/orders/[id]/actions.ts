"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiAccessToken, getApiBaseUrl, getApiClient } from "@/lib/api";

type OrderStatus = components["schemas"]["OrderStatus"];

export type OrderActionResult = { ok: boolean; message: string };

export async function updateOrderStatus(
  id: string,
  status: OrderStatus
): Promise<OrderActionResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/orders/{id}/status", {
    params: { path: { id } },
    body: { status },
  });
  if (!result.data) return { ok: false, message: "The order status could not be updated." };

  revalidatePath("/orders");
  revalidatePath(`/orders/${id}`);
  return { ok: true, message: `Order marked ${status}.` };
}

export async function deleteOrder(id: string): Promise<OrderActionResult> {
  const api = await getApiClient();
  const result = await api.DELETE("/api/orders/{id}", { params: { path: { id } } });
  if (result.error || !result.response.ok) {
    return { ok: false, message: "The order could not be deleted." };
  }

  revalidatePath("/orders");
  return { ok: true, message: "Order deleted." };
}

function textValue(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : null;
}

function dateTimeValue(formData: FormData, name: string): string | null {
  const value = textValue(formData, name);
  if (!value) return null;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

export async function updateOrderTracking(
  id: string,
  _previous: OrderActionResult,
  formData: FormData
): Promise<OrderActionResult> {
  const token = await getApiAccessToken();
  if (!token) return { ok: false, message: "Your session expired. Sign in again." };

  const body = {
    shippingCarrier: textValue(formData, "shippingCarrier"),
    trackingNumber: textValue(formData, "trackingNumber"),
    trackingUrl: textValue(formData, "trackingUrl"),
    shippedAt: dateTimeValue(formData, "shippedAt"),
    deliveredAt: dateTimeValue(formData, "deliveredAt"),
    markShipped: formData.get("intent") === "markShipped",
    markDelivered: formData.get("intent") === "markDelivered",
  };

  const response = await fetch(`${getApiBaseUrl()}/api/orders/${id}/tracking`, {
    method: "PATCH",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
    cache: "no-store",
  });

  if (!response.ok) {
    const bodyText = await response.text();
    return { ok: false, message: bodyText || "Order tracking could not be saved." };
  }

  revalidatePath("/orders");
  revalidatePath(`/orders/${id}`);
  if (body.markDelivered || body.deliveredAt)
    return { ok: true, message: "Tracking saved and order marked delivered." };
  if (body.markShipped || body.shippedAt)
    return { ok: true, message: "Tracking saved and order marked shipped." };
  return { ok: true, message: "Tracking saved." };
}

// ── Returns (RMA lifecycle) ────────────────────────────────────────────────
async function revalidateOrder(orderId: string) {
  revalidatePath("/orders");
  revalidatePath(`/orders/${orderId}`);
}

export async function openReturn(saleId: string, orderId: string, reason: string): Promise<OrderActionResult> {
  const api = await getApiClient();
  const r = await api.POST("/api/sales/{id}/returns", { params: { path: { id: saleId } }, body: { reason } });
  if (r.error || !r.data) return { ok: false, message: "The return could not be opened." };
  await revalidateOrder(orderId);
  return { ok: true, message: "Return opened." };
}

export async function issueReturnLabel(returnId: string, orderId: string, returnShippingCost: number): Promise<OrderActionResult> {
  const api = await getApiClient();
  const r = await api.POST("/api/sales/returns/{returnId}/label", { params: { path: { returnId } }, body: { returnShippingCost } });
  if (r.error || !r.data) return { ok: false, message: "The label could not be issued." };
  await revalidateOrder(orderId);
  return { ok: true, message: "Return label issued." };
}

export async function receiveReturn(returnId: string, orderId: string): Promise<OrderActionResult> {
  const api = await getApiClient();
  const r = await api.POST("/api/sales/returns/{returnId}/receive", { params: { path: { returnId } } });
  if (r.error || !r.data) return { ok: false, message: "The return could not be marked received." };
  await revalidateOrder(orderId);
  return { ok: true, message: "Return received." };
}

export async function resolveReturn(returnId: string, orderId: string, itemAmount: number, restock: boolean): Promise<OrderActionResult> {
  const api = await getApiClient();
  const r = await api.POST("/api/sales/returns/{returnId}/resolve", {
    params: { path: { returnId } },
    body: { itemAmount, type: "Full", restock },
  });
  if (r.error || !r.data) return { ok: false, message: "The refund could not be issued." };
  await revalidateOrder(orderId);
  return { ok: true, message: restock ? "Refunded and restocked." : "Refunded." };
}

export async function rejectReturn(returnId: string, orderId: string, reason: string): Promise<OrderActionResult> {
  const api = await getApiClient();
  const r = await api.POST("/api/sales/returns/{returnId}/reject", { params: { path: { returnId } }, body: { reason } });
  if (r.error || !r.data) return { ok: false, message: "The return could not be rejected." };
  await revalidateOrder(orderId);
  return { ok: true, message: "Return rejected." };
}

export async function cancelReturn(returnId: string, orderId: string): Promise<OrderActionResult> {
  const api = await getApiClient();
  const r = await api.POST("/api/sales/returns/{returnId}/cancel", { params: { path: { returnId } } });
  if (r.error || !r.data) return { ok: false, message: "The return could not be cancelled." };
  await revalidateOrder(orderId);
  return { ok: true, message: "Return cancelled." };
}
