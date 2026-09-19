import type { IngestListing } from "../../agent/types";
import { amount, type VintedMoney } from "./orders";

/** One item in a member's wardrobe (`/api/v2/wardrobe/{userId}/items`), as far as a listing needs it. */
export interface VintedWardrobeItem {
  id?: number | string;
  title?: string | null;
  price?: VintedMoney | null;
  currency?: string | null;
  is_draft?: boolean | null;
  is_closed?: boolean | null;
  is_hidden?: boolean | null;
  is_reserved?: boolean | null;
  item_closing_action?: string | null;
  url?: string | null;
  status?: string | null;
  brand?: string | null;
  size?: string | null;
  view_count?: number | null;
  favourite_count?: number | null;
  photos?: { url?: string | null; is_main?: boolean | null }[] | null;
}

export interface VintedWardrobe {
  items?: VintedWardrobeItem[];
  pagination?: { current_page?: number; total_pages?: number; total_entries?: number };
}

/**
 * One wardrobe item as a listing. A sold item stays in the wardrobe, closed, and is recorded as sold;
 * a hidden one is off sale, so ended until it is shown again; a reserved one is still listed.
 */
export function toIngestListing(item: VintedWardrobeItem): IngestListing | null {
  const price = amount(item.price);
  if (item.id === null || item.id === undefined || price === null) {
    return null;
  }

  const sold = item.is_closed === true && item.item_closing_action === "sold";
  const status: IngestListing["status"] = item.is_draft
    ? "Draft"
    : sold
      ? "Sold"
      : item.is_closed || item.is_hidden
        ? "Ended"
        : "Active";
  const photo = item.photos?.find((candidate) => candidate.is_main)?.url ?? item.photos?.[0]?.url ?? null;
  return {
    externalListingId: String(item.id),
    title: item.title?.trim() || "Untitled Vinted item",
    price,
    status,
    quantity: 1,
    quantitySold: sold ? 1 : 0,
    platform: "Vinted",
    currency: (item.price?.currency_code ?? item.currency ?? "USD").toUpperCase(),
    condition: item.status ?? null,
    url: item.url ?? null,
    primaryImageUrl: photo,
    rawJson: JSON.stringify({
      reserved: item.is_reserved === true,
      hidden: item.is_hidden === true,
      brand: item.brand ?? null,
      size: item.size ?? null,
      views: item.view_count ?? null,
      favourites: item.favourite_count ?? null,
    }),
  };
}
