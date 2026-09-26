import { NextResponse } from "next/server";
import { getApiAssetUrl, getApiClient } from "@/lib/api";

/** One inventory item as the picker shows it — just what choosing needs. */
export type PickerItem = {
  id: string;
  title: string;
  isSold: boolean;
  status: string | null;
  code: string | null;
  price: number | null;
  cost: number | null;
  expected: number | null;
  category: string | null;
  location: string | null;
  thumbnailUrl: string | null;
};

/**
 * Inventory search for the item pickers: the first matches for what was typed. For matching an order, sold and unsold
 * items alike — a sold one is how an already-recorded sale gets its exact figures. With `for=lot`, only items that can
 * still join a purchase lot. Server-side, so a picker never loads the whole inventory into the page.
 */
export async function GET(request: Request) {
  const params = new URL(request.url).searchParams;
  const query = params.get("q")?.trim() ?? "";
  const forLot = params.get("for") === "lot";
  const api = await getApiClient();
  const { data, error, response } = await api.GET("/api/inventory/browse", {
    params: {
      query: {
        search: query || undefined,
        includeArchived: false,
        page: 1,
        pageSize: 12,
        ...(forLot ? { lifecycle: "Active" as const, lotCandidates: true } : {}),
      },
    },
    signal: request.signal,
  });
  if (!data) {
    return NextResponse.json(error ?? { detail: "Inventory could not be searched." }, { status: response.status });
  }

  const items: PickerItem[] = (data.items ?? [])
    .filter((item) => item.id && item.lifecycleGroup !== "Removed" && (!item.isBundle || item.bundleComponentCount === 0))
    .map((item) => ({
      id: item.id!,
      title: item.title ?? "Untitled item",
      isSold: item.isSold ?? false,
      status: item.status ?? null,
      code: item.barcode ?? null,
      price: item.estimatedSalePrice ?? item.sellerAskingPrice ?? null,
      cost: item.purchasePrice ?? null,
      expected: item.estimatedSalePrice ?? null,
      category: item.category ?? null,
      location: item.storageLocation ?? null,
      thumbnailUrl: getApiAssetUrl(item.heroPhotoThumbnailUrl),
    }));
  return NextResponse.json(items);
}
