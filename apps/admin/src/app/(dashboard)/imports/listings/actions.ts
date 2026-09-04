"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

export type ListingActionResult = { ok: boolean; message: string };


function pathsToRevalidate() {
  revalidatePath("/imports");
  revalidatePath("/imports/listings");
  revalidatePath("/inventory");
  revalidatePath("/channels");
}

export async function reconcileListing(
  _previous: ListingActionResult,
  formData: FormData
): Promise<ListingActionResult> {
  const listingId = formData.get("listingId");
  if (typeof listingId !== "string" || !listingId)
    return { ok: false, message: "Missing listing." };

  const intent = formData.get("intent");
  const inventoryItemId = (formData.get("inventoryItemId") as string | null)?.trim() || null;
  const createNewItem = intent === "create";
  if (!createNewItem && !inventoryItemId) {
    return { ok: false, message: "Choose an inventory item to link, or create a new item." };
  }

  const api = await getApiClient();
  const result = await api.POST("/api/platform-listings/{id}/reconcile", {
    params: { path: { id: listingId } },
    body: createNewItem ? { createNewItem: true } : { inventoryItemId },
  });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "The listing could not be linked.") };
  }

  pathsToRevalidate();
  return {
    ok: true,
    message: result.data.createdInventoryItem
      ? "Created a new inventory item and linked the listing."
      : "Listing linked to inventory.",
  };
}

export async function ignoreListing(
  _previous: ListingActionResult,
  formData: FormData
): Promise<ListingActionResult> {
  const listingId = formData.get("listingId");
  if (typeof listingId !== "string" || !listingId)
    return { ok: false, message: "Missing listing." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-listings/{id}/ignore", {
    params: { path: { id: listingId } },
  });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "The listing could not be ignored.") };
  }

  pathsToRevalidate();
  return { ok: true, message: "Listing ignored." };
}

export async function unlinkListing(
  _previous: ListingActionResult,
  formData: FormData
): Promise<ListingActionResult> {
  const listingId = formData.get("listingId");
  if (typeof listingId !== "string" || !listingId)
    return { ok: false, message: "Missing listing." };

  const api = await getApiClient();
  const result = await api.POST("/api/platform-listings/{id}/unlink", {
    params: { path: { id: listingId } },
  });
  if (!result.data) {
    return { ok: false, message: errorMessage(result.error, "The listing could not be unlinked.") };
  }

  pathsToRevalidate();
  return { ok: true, message: "Listing returned to the reconcile queue." };
}
