"use server";

import { revalidatePath } from "next/cache";
import { getApiAccessToken, getApiBaseUrl } from "@/lib/api";

export type BulkInventoryDeleteResult = {
  ok: boolean;
  message: string;
  archivedCount?: number;
  failed?: Array<{ id: string; message: string }>;
};

export async function archiveInventoryItems(ids: string[]): Promise<BulkInventoryDeleteResult> {
  const token = await getApiAccessToken();
  if (!token) return { ok: false, message: "Your session expired. Sign in again." };

  const uniqueIds = [...new Set(ids.map((id) => id.trim()).filter(Boolean))];
  if (uniqueIds.length === 0) return { ok: false, message: "Select at least one inventory item." };
  if (uniqueIds.length > 100) return { ok: false, message: "Archive at most 100 items at a time." };

  const failed: Array<{ id: string; message: string }> = [];
  let archivedCount = 0;

  for (const id of uniqueIds) {
    const response = await fetch(`${getApiBaseUrl()}/api/inventory/${id}?hard=false`, {
      method: "DELETE",
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-store",
    });

    if (response.ok) {
      archivedCount += 1;
      continue;
    }

    const body = await response.text();
    failed.push({ id, message: body || "Inventory item could not be archived." });
  }

  revalidatePath("/inventory");
  if (failed.length > 0) {
    return {
      ok: false,
      archivedCount,
      failed,
      message: archivedCount > 0
        ? `Archived ${archivedCount} item${archivedCount === 1 ? "" : "s"}; ${failed.length} failed.`
        : `No items archived. ${failed[0]?.message ?? "One or more items failed."}`,
    };
  }

  return {
    ok: true,
    archivedCount,
    message: `Archived ${archivedCount} item${archivedCount === 1 ? "" : "s"}.`,
  };
}
