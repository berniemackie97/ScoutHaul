"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

export type DelistResult = { ok: boolean; message: string };

/**
 * Records that the operator has taken a listing down on the marketplace, or that it should stay up.
 *
 * Neither call touches the marketplace. Removing a listing is done by a person, on the site — doing it with
 * software is against every marketplace's terms, and the extension never does it either.
 */
export async function markDelisted(_previous: DelistResult, formData: FormData): Promise<DelistResult> {
  return settle(formData, "delisted");
}

export async function keepListed(_previous: DelistResult, formData: FormData): Promise<DelistResult> {
  return settle(formData, "keep-listed");
}

async function settle(formData: FormData, verb: "delisted" | "keep-listed"): Promise<DelistResult> {
  const id = formData.get("channelListingId");
  if (typeof id !== "string" || !id) {
    return { ok: false, message: "Missing listing." };
  }

  const api = await getApiClient();
  const result = verb === "delisted"
    ? await api.POST("/api/channel-listings/{channelListingId}/delisted", { params: { path: { channelListingId: id } } })
    : await api.POST("/api/channel-listings/{channelListingId}/keep-listed", { params: { path: { channelListingId: id } } });

  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "That listing could not be updated.") };
  }

  revalidatePath("/channels/delist");
  revalidatePath("/channels");
  revalidatePath("/");
  return {
    ok: true,
    message: verb === "delisted" ? "Marked as taken down." : "Left listed — it will not be asked about again.",
  };
}
