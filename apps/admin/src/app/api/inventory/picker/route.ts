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
  thumbnailUrl: string | null;
};

/**
 * Inventory search for the item picker: the first matches for what was typed, over sold and unsold
 * items alike — a sold one is how an already-recorded sale gets its exact figures. Server-side, so
 * the picker never loads the whole inventory into the page.
 */
export async function GET(request: Request) {
  const query = new URL(request.url).searchParams.get("q")?.trim() ?? "";
  const api = await getApiClient();
  const { data, error, response } = await api.GET("/api/inventory/browse", {
    params: { query: { search: query || undefined, includeArchived: false, page: 1, pageSize: 12 } },
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
      thumbnailUrl: getApiAssetUrl(item.heroPhotoThumbnailUrl),
    }));
  return NextResponse.json(items);
}
