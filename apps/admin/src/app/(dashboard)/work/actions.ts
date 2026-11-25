"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type WorkWait = components["schemas"]["WorkWait"];

export type WorkActionResult = { ok: boolean; message: string };

export async function setAsideTask(taskKey: string, title: string, wait: WorkWait, until: string | null, note: string): Promise<WorkActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/work/set-aside", { body: { taskKey, title, wait, until, note: note.trim() || null } });
  if (!result.response.ok) return { ok: false, message: errorMessage((result as { error?: unknown }).error, "The task could not be set aside.") };
  revalidatePath("/work");
  return { ok: true, message: wait === "Snoozed" ? "Snoozed. It comes back on its date." : `Waiting on ${wait.toLowerCase()}.` };
}

export async function bringBackTask(taskKey: string): Promise<WorkActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/work/bring-back", { params: { query: { key: taskKey } } });
  if (!result.response.ok) return { ok: false, message: "The task could not be brought back." };
  revalidatePath("/work");
  return { ok: true, message: "Back in its lane." };
}
