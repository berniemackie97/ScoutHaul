"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type UpdateStoreSettingsRequest = components["schemas"]["UpdateStoreSettingsRequest"];

export type SettingsActionResult = {
  ok: boolean;
  message: string;
};

function textValue(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : null;
}


export async function updateStoreSettings(
  _previous: SettingsActionResult,
  formData: FormData,
): Promise<SettingsActionResult> {
  const name = textValue(formData, "name");
  if (!name) return { ok: false, message: "Business name is required." };

  const body: UpdateStoreSettingsRequest = {
    name,
    // Blank disconnects the storefront; the API normalizes + validates the domain.
    storeDomain: textValue(formData, "storeDomain"),
    timeZoneId: textValue(formData, "timeZoneId"),
  };

  const api = await getApiClient();
  const result = await api.PUT("/api/store/settings", { body });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "Store settings could not be saved.") };
  }

  revalidatePath("/settings");
  return {
    ok: true,
    message: result.data.storeDomain
      ? `Saved. Storefront is live on ${result.data.storeDomain}; days are counted in ${result.data.timeZoneId}.`
      : `Saved. Days are counted in ${result.data.timeZoneId}; no storefront is connected.`,
  };
}
