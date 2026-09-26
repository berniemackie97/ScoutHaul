import type { IngestListing } from "../../agent/types";
import { dig } from "../embedded-json";
import type { PageResponse } from "../types";
import { readState } from "./state";

/** One page of a closet, and how to ask for the next. */
export interface ClosetPage {
  listings: IngestListing[];
  more: boolean;
  /** Poshmark's own cursor for the next page. Opaque: it is echoed back, never constructed. */
  nextMaxId: string | null;
  /** Whether the page carried a list at all. An empty list is a closet with nothing in it; no list is a changed site. */
  found: boolean;
}

/**
 * A closet page as Poshmark serves it. Every listing on it arrives whole — title, brand, colours, size, condition,
 * category, description, prices and how many are left — so one page read carries what other marketplaces make you
 * open each listing for.
 */
export function readClosetPage(response: PageResponse): ClosetPage {
  const state = readState(response, "closet");
  const posts = dig(state, "$_closet", "listingsPostData");
  const data = dig(posts, "data");
  if (!Array.isArray(data)) {
    return { listings: [], more: false, nextMaxId: null, found: false };
  }

  const cursor = dig(posts, "more", "next_max_id");
  return {
    listings: data.flatMap((post) => {
      const listing = toIngestListing(post);
      return listing ? [listing] : [];
    }),
    more: dig(posts, "more", "is_next_max_id_present") === true,
    nextMaxId: typeof cursor === "string" && cursor.length > 0 ? cursor : null,
    found: true,
  };
}

export function toIngestListing(post: unknown): IngestListing | null {
  const id = dig(post, "id");
  const title = dig(post, "title");
  const price = money(dig(post, "price_amount")) ?? money(dig(post, "price"));
  if (typeof id !== "string" || typeof title !== "string" || price === null) {
    return null;
  }

  const sizes = dig(post, "inventory", "size_quantities");
  const available = Array.isArray(sizes)
    ? sizes.reduce((total, size) => total + (typeof dig(size, "quantity_available") === "number" ? (dig(size, "quantity_available") as number) : 0), 0)
    : 1;
  const sold = Array.isArray(sizes)
    ? sizes.reduce((total, size) => total + (typeof dig(size, "quantity_sold") === "number" ? (dig(size, "quantity_sold") as number) : 0), 0)
    : 0;

  const cover = dig(post, "cover_shot", "url_large") ?? dig(post, "cover_shot", "url") ?? dig(post, "picture_url");
  return {
    externalListingId: id,
    title,
    price,
    quantity: available,
    quantitySold: sold,
    status: status(dig(post, "inventory", "status"), dig(post, "status")),
    platform: "Poshmark",
    currency: "USD",
    brand: text(dig(post, "brand")),
    size: text(dig(post, "size")),
    color: colors(dig(post, "colors")),
    condition: condition(dig(post, "condition")),
    categoryName: text(dig(post, "category_v2")) ?? text(dig(post, "category")),
    description: text(dig(post, "description")) ?? undefined,
    url: `https://poshmark.com/listing/${id}`,
    primaryImageUrl: typeof cover === "string" ? cover : null,
    // One picture, which is not a gallery: the rest live on the listing's own page.
    photoUrls: undefined,
    listedAt: date(dig(post, "first_published_at") ?? dig(post, "created_at")),
  };
}

/**
 * Poshmark keeps two states: whether the listing is published, and whether any of it is left. A sold-out listing is
 * still published, so the inventory decides.
 */
function status(inventory: unknown, published: unknown): IngestListing["status"] {
  if (inventory === "sold_out") {
    return "Sold";
  }

  if (published === "draft" || published === "not_for_sale") {
    return "Draft";
  }

  return published === "published" ? "Active" : "Ended";
}

/** "uf" is how Poshmark writes "used, in good condition"; "nwt" is new with tags. */
function condition(value: unknown): string | null {
  return value === "nwt" ? "New with tags" : value === "ret" ? "Retail" : value === "uf" ? "Used" : text(value);
}

function colors(value: unknown): string | null {
  if (!Array.isArray(value)) {
    return null;
  }

  const named = value.flatMap((color) => {
    const name = dig(color, "name");
    return typeof name === "string" && name.trim().length > 0 ? [name.trim()] : [];
  });
  return named.length === 0 ? null : named.join(", ");
}

function money(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }

  const parsed = typeof value === "string" ? Number(value.replace(/[^0-9.]/g, "")) : Number.NaN;
  return Number.isFinite(parsed) ? parsed : null;
}

function text(value: unknown): string | null {
  if (typeof value === "string") {
    return value.trim().length === 0 ? null : value.trim();
  }

  const display = dig(value, "display");
  return typeof display === "string" && display.trim().length > 0 ? display.trim() : null;
}

function date(value: unknown): string | null {
  const at = typeof value === "string" ? Date.parse(value) : Number.NaN;
  return Number.isFinite(at) ? new Date(at).toISOString() : null;
}
