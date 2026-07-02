"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type CreateInventoryBundleRequest = components["schemas"]["CreateInventoryBundleRequest"];

export type BundleActionResult = { ok: boolean; message: string };

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function number(formData: FormData, name: string): number | null {
  const value = text(formData, name);
  if (!value) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

function errorMessage(error: unknown): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return "The bundle could not be created.";
}

export async function createBundle(_previous: BundleActionResult, formData: FormData): Promise<BundleActionResult> {
  const title = text(formData, "title");
  if (!title) return { ok: false, message: "Bundle title is required." };

  const componentInventoryItemIds = formData.getAll("componentInventoryItemId").filter((value): value is string => typeof value === "string" && value.length > 0);
  if (new Set(componentInventoryItemIds).size < 2) return { ok: false, message: "Select at least two distinct inventory items." };

  const estimatedSalePrice = number(formData, "estimatedSalePrice");
  if (estimatedSalePrice === null && text(formData, "estimatedSalePrice")) {
    return { ok: false, message: "Estimated sale price must be a valid non-negative amount." };
  }

  const body: CreateInventoryBundleRequest = {
    title,
    description: text(formData, "description"),
    category: text(formData, "category"),
    estimatedSalePrice,
    status: (text(formData, "status") ?? "ReadyToList") as CreateInventoryBundleRequest["status"],
    componentInventoryItemIds: [...new Set(componentInventoryItemIds)],
    notes: text(formData, "notes"),
  };

  const api = await getApiClient();
  const result = await api.POST("/api/inventory/bundles", { body });
  if (!result.data?.id) return { ok: false, message: errorMessage(result.error) };

  revalidatePath("/inventory");
  redirect(`/inventory/${result.data.id}`);
}
