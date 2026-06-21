"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

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

function errorMessage(error: unknown, fallback: string): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return fallback;
}

export async function updateStoreSettings(
  _previous: SettingsActionResult,
  formData: FormData,
): Promise<SettingsActionResult> {
  const name = textValue(formData, "name");
  if (!name) return { ok: false, message: "Store name is required." };

  const body: UpdateStoreSettingsRequest = {
    name,
    // Blank disconnects the storefront; the API normalizes + validates the domain.
    storeDomain: textValue(formData, "storeDomain"),
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
      ? `Saved. Storefront is live on ${result.data.storeDomain}.`
      : "Saved. Storefront is disconnected.",
  };
}
