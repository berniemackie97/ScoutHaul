"use server";

import { revalidatePath } from "next/cache";
import { getApiAccessToken, getApiBaseUrl } from "@/lib/api";

export async function decideOffer(formData: FormData) {
  const token = await getApiAccessToken();
  if (!token) {
    throw new Error("Operator sign-in required.");
  }

  const offerId = String(formData.get("offerId") ?? "");
  const decision = String(formData.get("decision") ?? "");
  const responseMessage = trimToNull(String(formData.get("responseMessage") ?? ""));
  const rawAmount = trimToNull(String(formData.get("responseAmount") ?? ""));
  const responseAmount: number | null = rawAmount == null ? null : Number(rawAmount);

  if (!offerId || !decision) {
    throw new Error("Offer and decision are required.");
  }
  if (responseAmount != null && (!Number.isFinite(responseAmount) || responseAmount <= 0)) {
    throw new Error("Counter amount must be greater than zero.");
  }

  const response = await fetch(`${getApiBaseUrl()}/api/admin/storefront/offers/${offerId}/decision`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
      Accept: "application/json",
    },
    body: JSON.stringify({
      decision,
      responseAmount,
      responseMessage,
    }),
  });

  if (!response.ok) {
    throw new Error(await response.text());
  }

  revalidatePath("/offers");
}

function trimToNull(value: string) {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}
