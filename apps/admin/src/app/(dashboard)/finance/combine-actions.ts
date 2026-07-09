"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";

export type CombineResult = { ok: boolean; message: string };

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

export async function combineSales(_previous: CombineResult, formData: FormData): Promise<CombineResult> {
  let saleIds: string[] = [];
  try {
    const parsed = JSON.parse((formData.get("saleIds") as string) ?? "[]");
    if (Array.isArray(parsed)) saleIds = parsed.filter((id): id is string => typeof id === "string" && id.length > 0);
  } catch {
    return { ok: false, message: "Could not read the selected sales." };
  }
  if (saleIds.length < 2) return { ok: false, message: "Select at least two sales to combine." };

  const orderReference = (formData.get("orderReference") as string)?.trim() || null;
  const api = await getApiClient();
  const result = await api.POST("/api/sales/group", { body: { saleIds, orderReference } });
  if (!result.data?.saleGroupId) return { ok: false, message: errorMessage(result.error, "Could not combine the sales.") };

  revalidatePath("/finance");
  return { ok: true, message: `Combined ${result.data.sales?.length ?? saleIds.length} sales into one order.` };
}

export async function ungroupSale(saleId: string): Promise<void> {
  const api = await getApiClient();
  await api.POST("/api/sales/ungroup", { body: { saleIds: [saleId] } });
  revalidatePath("/finance");
}

export async function deleteSale(saleId: string): Promise<void> {
  const api = await getApiClient();
  await api.DELETE("/api/sales/{id}", { params: { path: { id: saleId } } });
  revalidatePath("/finance");
  revalidatePath("/inventory");
}
