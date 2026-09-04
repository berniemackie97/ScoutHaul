"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

export type MenuResult = { ok: boolean; message: string };


function revalidate() {
  revalidatePath("/settings/menus");
  // Menus change everywhere items are edited/added.
  revalidatePath("/inventory", "layout");
}

export async function addMenuValue(kind: string, value: string): Promise<MenuResult> {
  const trimmed = value.trim();
  if (!trimmed) return { ok: false, message: "Enter a value." };
  const api = await getApiClient();
  const result = await api.POST("/api/reference-overrides", {
    body: { kind, value: trimmed, parentValue: null, label: null, isCustom: true, isDisabled: false },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "Could not add the value.") };
  revalidate();
  return { ok: true, message: `Added “${trimmed}”.` };
}

export async function disableMenuValue(kind: string, value: string): Promise<MenuResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/reference-overrides", {
    body: { kind, value, parentValue: null, label: null, isCustom: false, isDisabled: true },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "Could not disable the value.") };
  revalidate();
  return { ok: true, message: `Hid “${value}” from menus.` };
}

export async function removeMenuOverride(id: string): Promise<MenuResult> {
  const api = await getApiClient();
  const result = (await api.DELETE("/api/reference-overrides/{id}", { params: { path: { id } } })) as { error?: unknown };
  if (result.error) return { ok: false, message: errorMessage(result.error, "Could not update the menu.") };
  revalidate();
  return { ok: true, message: "Menu updated." };
}
