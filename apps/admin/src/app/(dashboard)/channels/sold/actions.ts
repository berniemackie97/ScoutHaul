"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

export type SoldResult = { ok: boolean; message: string };

// Settles one without a sale. Recording the sale itself happens on the item, where the fees are entered.
export async function acknowledgeSold(_previous: SoldResult, formData: FormData): Promise<SoldResult> {
  const id = formData.get("platformListingId");
  if (typeof id !== "string" || !id) {
    return { ok: false, message: "Missing listing." };
  }

  const api = await getApiClient();
  const result = await api.POST("/api/platform-listings/{id}/sold-acknowledged", { params: { path: { id } } });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "That listing could not be updated.") };
  }

  revalidatePath("/channels/sold");
  revalidatePath("/");
  return { ok: true, message: "Cleared — no sale recorded for it." };
}
