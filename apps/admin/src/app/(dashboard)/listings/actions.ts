"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";

export async function reconcileListing(
  listingId: string,
  inventoryItemId: string | null
): Promise<void> {
  const api = await getApiClient();
  await api.POST("/api/platform-listings/{id}/reconcile", {
    params: { path: { id: listingId } },
    body: inventoryItemId ? { inventoryItemId } : { createNewItem: true },
  });
  revalidatePath("/listings");
  revalidatePath("/inventory");
}

export async function ignoreListing(listingId: string): Promise<void> {
  const api = await getApiClient();
  await api.POST("/api/platform-listings/{id}/ignore", { params: { path: { id: listingId } } });
  revalidatePath("/listings");
}
