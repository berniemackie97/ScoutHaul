"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import type { InventoryStatus } from "@/features/inventory/inventory-operations";
import {
  getApiAccessToken,
  getApiBaseUrl,
  getApiClient,
} from "@/lib/api";
import { failedFormState, type FailedFormState } from "@/lib/form-state";
import { readItemForm } from "@/features/inventory/item-form/read-form";
import { errorMessage } from "@/lib/api-error";

type UpsertInventoryItemRequest = components["schemas"]["UpsertInventoryItemRequest"];
export type InventoryActionResult = {
  ok: boolean;
  message: string;
} & FailedFormState;

const initialFailure = (message: string): InventoryActionResult => ({ ok: false, message });

export async function updateInventoryItem(
  id: string,
  _previous: InventoryActionResult,
  formData: FormData,
): Promise<InventoryActionResult> {
  const { fields, errors } = readItemForm(formData);
  if (Object.keys(errors).length > 0) {
    return failedFormState("Fix the highlighted fields.", formData, errors);
  }

  const api = await getApiClient();
  const current = await api.GET("/api/inventory/{id}", { params: { path: { id } } });
  if (!current.data) {
    return failedFormState(errorMessage(current.error, "Inventory item could not be loaded."), formData);
  }

  const body: UpsertInventoryItemRequest = {
    productIdentityId: current.data.productIdentityId ?? null,
    sourcingTripId: current.data.sourcingTripId ?? null,
    sourcingTripStopId: current.data.sourcingTripStopId ?? null,
    sourceLocationId: current.data.sourceLocationId ?? null,
    ...fields,
  };

  const result = await api.PUT("/api/inventory/{id}", {
    params: { path: { id } },
    body,
  });
  if (!result.data) {
    return failedFormState(errorMessage(result.error, "Inventory changes could not be saved."), formData);
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: "Saved." };
}

export async function changeInventoryStatus(
  id: string,
  status: InventoryStatus,
): Promise<InventoryActionResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/inventory/{id}/status", {
    params: { path: { id } },
    body: { status: status as components["schemas"]["InventoryStatus"] },
  });
  if (!result.data) {
    return initialFailure(errorMessage(result.error, "Inventory status could not be changed."));
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: `Status changed to ${status}.` };
}

export async function uploadInventoryPhoto(
  id: string,
  formData: FormData,
): Promise<InventoryActionResult> {
  const file = formData.get("file");
  if (!(file instanceof File) || file.size === 0) {
    return initialFailure("Choose an image to upload.");
  }
  if (file.size > 10_000_000) {
    return initialFailure("Photos must be smaller than 10 MB.");
  }

  const token = await getApiAccessToken();
  if (!token) return initialFailure("Your session expired. Sign in again.");

  const payload = new FormData();
  payload.set("File", file);
  payload.set("IsHero", formData.get("isHero") === "on" ? "true" : "false");

  const response = await fetch(`${getApiBaseUrl()}/api/inventory/${id}/photos`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: payload,
    cache: "no-store",
  });
  if (!response.ok) {
    const body = await response.text();
    return initialFailure(body || `Photo upload failed (${response.status}).`);
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: "Photo uploaded." };
}

/**
 * Attach photos by URL — for images that live on a live listing (eBay, Vinted, a supplier page)
 * rather than on this device. Accepts several links at once (one per line) because a listing's
 * gallery is normally pasted as a set. Each is imported in order; the first failure is reported but
 * the ones that already succeeded are kept.
 */
export async function importInventoryPhotosFromUrls(
  id: string,
  urlsRaw: string,
  makeFirstHero: boolean,
): Promise<InventoryActionResult> {
  const urls = urlsRaw
    .split(/[\s,]+/)
    .map((value) => value.trim())
    .filter(Boolean);
  if (urls.length === 0) return initialFailure("Paste at least one image address.");
  if (urls.length > 12) return initialFailure("Import up to 12 images at a time.");

  const api = await getApiClient();
  let imported = 0;
  for (const [index, url] of urls.entries()) {
    const result = await api.POST("/api/inventory/{id}/photos/from-url", {
      params: { path: { id } },
      body: { url, isHero: makeFirstHero && index === 0 },
    });
    if (!result.data) {
      const detail = errorMessage(result.error, `That image could not be imported (${url.slice(0, 60)}).`);
      revalidatePath(`/inventory/${id}`);
      return imported > 0
        ? initialFailure(`Imported ${imported} of ${urls.length}. ${detail}`)
        : initialFailure(detail);
    }
    imported += 1;
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: imported === 1 ? "Photo imported." : `${imported} photos imported.` };
}

export async function setInventoryHeroPhoto(
  id: string,
  url: string,
): Promise<InventoryActionResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/inventory/{id}/photos/hero", {
    params: { path: { id }, query: { url } },
  });
  if (!result.data) {
    return initialFailure(errorMessage(result.error, "Hero photo could not be changed."));
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: "Hero photo updated." };
}

export async function deleteInventoryPhoto(
  id: string,
  photoId: string,
): Promise<InventoryActionResult> {
  const api = await getApiClient();
  const result = await api.DELETE("/api/inventory/{id}/photos/{photoId}", {
    params: { path: { id, photoId } },
  });
  if (!result.data) {
    return initialFailure(errorMessage(result.error, "Photo could not be deleted."));
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return { ok: true, message: "Photo deleted." };
}

export type InventoryDeleteResult = InventoryActionResult & {
  deleted?: boolean;
};

export async function deleteInventoryItem(
  id: string,
  hard: boolean,
): Promise<InventoryDeleteResult> {
  const token = await getApiAccessToken();
  if (!token) return { ok: false, message: "Your session expired. Sign in again." };

  const response = await fetch(`${getApiBaseUrl()}/api/inventory/${id}?hard=${hard ? "true" : "false"}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (!response.ok) {
    const body = await response.text();
    return {
      ok: false,
      message: body || (hard ? "Inventory item could not be permanently deleted." : "Inventory item could not be archived."),
    };
  }

  revalidatePath("/inventory");
  revalidatePath(`/inventory/${id}`);
  return {
    ok: true,
    deleted: hard,
    message: hard ? "Inventory item permanently deleted." : "Inventory item archived.",
  };
}
