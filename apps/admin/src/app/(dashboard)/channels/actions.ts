"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type UpsertChannelAccountRequest = components["schemas"]["UpsertChannelAccountRequest"];

export type ChannelActionResult = { ok: boolean; message: string };

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}


export async function saveChannelAccount(_previous: ChannelActionResult, formData: FormData): Promise<ChannelActionResult> {
  const channel = text(formData, "channel");
  const displayName = text(formData, "displayName");
  if (!channel) return { ok: false, message: "Choose a channel." };
  if (!displayName) return { ok: false, message: "A display name is required." };

  const body: UpsertChannelAccountRequest = {
    channel,
    displayName,
    externalAccountId: text(formData, "externalAccountId"),
    defaultFeeProfileId: text(formData, "defaultFeeProfileId"),
    isActive: formData.get("isActive") !== "false",
    notes: text(formData, "notes"),
  };

  const id = text(formData, "id");
  const api = await getApiClient();
  const result = id
    ? await api.PUT("/api/channel-accounts/{id}", { params: { path: { id } }, body })
    : await api.POST("/api/channel-accounts", { body });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The channel account could not be saved.") };

  revalidatePath("/channels");
  return { ok: true, message: id ? "Channel account updated." : `Connected ${displayName}.` };
}

export async function setChannelActive(id: string, isActive: boolean): Promise<ChannelActionResult> {
  const api = await getApiClient();
  const current = await api.GET("/api/channel-accounts/{id}", { params: { path: { id } } });
  if (!current.data) return { ok: false, message: "Channel account not found." };

  const account = current.data;
  const result = await api.PUT("/api/channel-accounts/{id}", {
    params: { path: { id } },
    body: {
      channel: account.channel ?? "",
      displayName: account.displayName ?? "",
      externalAccountId: account.externalAccountId ?? null,
      defaultFeeProfileId: account.defaultFeeProfileId ?? null,
      isActive,
      notes: account.notes ?? null,
    },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "Could not update the account.") };

  revalidatePath("/channels");
  return { ok: true, message: isActive ? "Channel reactivated." : "Channel deactivated." };
}

export async function deleteChannelAccount(id: string): Promise<ChannelActionResult> {
  const api = await getApiClient();
  const result = await api.DELETE("/api/channel-accounts/{id}", { params: { path: { id } } });
  // 204 No Content → no data and no error; only a populated error means it failed (e.g. 409 with listings).
  if (result.error) return { ok: false, message: errorMessage(result, "The channel account could not be removed.") };

  revalidatePath("/channels");
  return { ok: true, message: "Channel account removed." };
}
