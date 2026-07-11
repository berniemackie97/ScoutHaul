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
