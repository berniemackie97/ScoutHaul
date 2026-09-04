"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type Status = components["schemas"]["ChannelListingStatus"];

export type ListingActionResult = { ok: boolean; message: string };

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function number(formData: FormData, name: string): number | null {
  const value = text(formData, name);
  if (value === null) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}


export async function upsertChannelListing(itemId: string, _previous: ListingActionResult, formData: FormData): Promise<ListingActionResult> {
  const channelAccountId = text(formData, "channelAccountId");
  const status = text(formData, "status") as Status | null;
  if (!channelAccountId || !status) return { ok: false, message: "Pick a channel and status." };

  const api = await getApiClient();
  const result = await api.PUT("/api/inventory/{itemId}/channel-listings", {
    params: { path: { itemId } },
    body: {
      channelAccountId,
      status,
      listedPrice: number(formData, "listedPrice"),
      quantity: null,
      externalListingId: text(formData, "externalListingId"),
      externalUrl: text(formData, "externalUrl"),
      imageHash: null,
    },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "Could not save the listing.") };

  revalidatePath(`/inventory/${itemId}`);
  return { ok: true, message: "Channel listing saved." };
}

export async function removeChannelListing(itemId: string, id: string): Promise<ListingActionResult> {
  const api = await getApiClient();
  const result = (await api.DELETE("/api/inventory/{itemId}/channel-listings/{id}", {
    params: { path: { itemId, id } },
  })) as { error?: unknown };
  if (result.error) return { ok: false, message: errorMessage(result.error, "Could not remove the listing.") };

  revalidatePath(`/inventory/${itemId}`);
  return { ok: true, message: "Channel listing removed." };
}
