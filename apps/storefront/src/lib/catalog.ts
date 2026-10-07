import type { components } from "@overhaul/api-client";
import { imageUrl } from "./commerce";
import { storefrontApi, liveCatalogEnabled } from "./store";

type StorefrontListing = components["schemas"]["StorefrontListingDto"];

// One catalog item, projected from the org's real inventory via /api/storefront/*.
export type Specimen = {
  id: string;
  slug: string;
  sku?: string;
  title: string;
  brand?: string;
  model?: string;
  color?: string;
  category: string;
  condition: string;
  size?: string;
  functionalStatus?: string;
  completenessStatus?: string;
  packagingStatus?: string;
  countryOfOrigin?: string;
  manufactureYear?: number;
  dimensions?: string;
  weight?: string;
  price: number;
  compareAt?: number;
  acceptsOffers?: boolean;
  minimumOfferAmount?: number | null;
  maximumOfferAmount?: number | null;
  shippingPolicy?: string;
  shippingAmountOverride?: number | null;
  era?: string;
  blurb: string;
  /** Absolute URL of the item's hero photo (served by the API), if any. */
  image?: string;
  /** All photos for the gallery, hero first. */
  images?: string[];
  availability?: string;
};

// Storefront data access — the live API only. No sample/fallback data: the store shows
// the org's real listings or an honest empty state, never placeholder products.

/**
 * The catalog could not be reached.
 *
 * Every read here used to answer a failure the same way it answers an absence — `null` for one item,
 * `[]` for a list. So a backend blip made a real, in-stock, one-of-a-kind piece disappear exactly as
 * if someone had just bought it, and turned a full shop into an empty one, with nothing anywhere
 * saying a request had failed. Throwing lets the page tell a shopper the truth: we cannot see the
 * shelf right now, not "it is gone".
 */
export class CatalogUnavailableError extends Error {
  constructor(cause: unknown) {
    super("The catalog could not be reached.");
    this.name = "CatalogUnavailableError";
    this.cause = cause;
  }
}

/** Items per catalogue page. The API serves at most 60, so this must stay at or under that. */
export const CATALOG_PAGE_SIZE = 48;

export type CatalogPage = { items: Specimen[]; page: number; pageCount: number; total: number };

/** The category part of a catalogue address, made the way the API makes it: letters and digits, hyphen-joined. */
export function categorySlug(name: string): string {
  return name
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, "-")
    .replace(/^-+|-+$/g, "");
}

function toSpecimen(dto: StorefrontListing): Specimen {
  return {
    id: dto.id ?? "",
    slug: dto.slug ?? "",
    title: dto.title ?? "",
    brand: dto.brand ?? undefined,
    category: dto.category ?? "",
    condition: dto.condition ?? "Good",
    price: dto.listPrice ?? 0,
    compareAt: dto.compareAtPrice ?? undefined,
    acceptsOffers: dto.acceptsOffers ?? true,
    minimumOfferAmount: dto.minimumOfferAmount ?? null,
    maximumOfferAmount: dto.maximumOfferAmount ?? null,
    shippingPolicy: dto.shippingPolicy,
    shippingAmountOverride: dto.shippingAmountOverride ?? null,
    blurb: dto.subtitle ?? "",
    image: imageUrl(dto.imageUrl),
  };
}

/** One page of the live shelf, with the shelf's real size, so nothing past the first page is out of reach. */
export async function getListings(
  opts: { category?: string; search?: string; page?: number; pageSize?: number } = {},
): Promise<CatalogPage> {
  const pageSize = opts.pageSize ?? CATALOG_PAGE_SIZE;
  const page = Math.max(1, Math.floor(opts.page ?? 1));
  if (!liveCatalogEnabled) return { items: [], page, pageCount: 1, total: 0 };
  try {
    const { data } = await storefrontApi().GET("/api/storefront/listings", {
      params: { query: { category: opts.category, search: opts.search, page, pageSize } },
    });
    const items = (data?.items ?? []).map(toSpecimen);
    const total = typeof data?.total === "number" ? data.total : items.length;
    return { items, page, total, pageCount: Math.max(1, Math.ceil(total / pageSize)) };
  } catch (error) {
    throw new CatalogUnavailableError(error);
  }
}

/**
 * The real number of items on the floor right now — the storefront never advertises a made-up count.
 *
 * This one still answers a failure with null, and deliberately: the count is a flourish on the home
 * page, and null already means "say nothing". Taking the whole home page down because a decorative
 * number could not be fetched would be the wrong trade.
 */
export async function getShelfCount(): Promise<number | null> {
  if (!liveCatalogEnabled) return null;
  try {
    const { data } = await storefrontApi().GET("/api/storefront/listings", {
      params: { query: { page: 1, pageSize: 1 } },
    });
    return typeof data?.total === "number" ? data.total : null;
  } catch {
    return null;
  }
}

export async function getListing(slug: string): Promise<Specimen | null> {
  if (!liveCatalogEnabled) return null;
  try {
    const { data } = await storefrontApi().GET("/api/storefront/listings/{slug}", {
      params: { path: { slug } },
    });
    if (!data) return null;
    return {
      id: data.id ?? "",
      slug: data.slug ?? "",
      title: data.title ?? "",
      brand: data.brand ?? undefined,
      model: data.model ?? undefined,
      color: data.colors?.length ? data.colors.join(", ") : undefined,
      category: data.category ?? "",
      condition: data.condition ?? "Good",
      size: [data.sizeSystem, data.sizeValue].filter(Boolean).join(" ") || undefined,
      functionalStatus: data.functionalStatus ? humanize(data.functionalStatus) : undefined,
      completenessStatus: data.completenessStatus ? humanize(data.completenessStatus) : undefined,
      packagingStatus: data.packagingStatus ? humanize(data.packagingStatus) : undefined,
      countryOfOrigin: data.countryOfOrigin ?? undefined,
      manufactureYear: data.manufactureYear ?? undefined,
      dimensions: formatDimensions(data.length, data.width, data.height, data.dimensionUnit),
      weight: data.weight == null ? undefined : `${data.weight} ${weightUnit(data.weightUnit)}`,
      price: data.listPrice ?? 0,
      compareAt: data.compareAtPrice ?? undefined,
      acceptsOffers: data.acceptsOffers ?? true,
      minimumOfferAmount: data.minimumOfferAmount ?? null,
      maximumOfferAmount: data.maximumOfferAmount ?? null,
      shippingPolicy: data.shippingPolicy,
      shippingAmountOverride: data.shippingAmountOverride ?? null,
      blurb: data.description ?? data.subtitle ?? "",
      image: imageUrl(data.imageUrl),
      images: (data.imageUrls ?? []).map(imageUrl).filter((url): url is string => Boolean(url)),
      availability: data.availability ?? "Available",
    };
  } catch (error) {
    // Not `null`: the caller turns null into notFound(), and a blip is not a missing product.
    throw new CatalogUnavailableError(error);
  }
}

function humanize(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, letter => letter.toUpperCase());
}

function formatDimensions(length?: number | null, width?: number | null, height?: number | null, unit?: string): string | undefined {
  const values = [length, width, height];
  if (values.every(value => value == null)) return undefined;
  const suffix = unit === "Centimeters" ? "cm" : "in";
  return `${values.map(value => value ?? "—").join(" × ")} ${suffix}`;
}

function weightUnit(value?: string): string {
  return ({ Ounces: "oz", Pounds: "lb", Grams: "g", Kilograms: "kg" } as Record<string, string>)[value ?? ""] ?? value ?? "";
}

/** Other live items from the same category, asked of the API rather than picked from one page of the shelf. */
export async function getRelated(item: { slug: string; category: string }, limit = 4): Promise<Specimen[]> {
  const category = item.category ? categorySlug(item.category) : undefined;
  const { items } = await getListings({ category, pageSize: limit + 1 });
  return items.filter((candidate) => candidate.slug !== item.slug).slice(0, limit);
}

/** One listing reduced to what the sitemap and category counts need. */
export type ListingSummary = { slug: string; category: string; image?: string };

/**
 * Every live listing, paged through in full, for the sitemap and the per-category counts. Returns what it has if a
 * page fails.
 */
export async function getAllListings(): Promise<ListingSummary[]> {
  if (!liveCatalogEnabled) return [];
  const pageSize = 100;
  const out: ListingSummary[] = [];
  try {
    for (let page = 1; page <= 100; page++) {
      const { data } = await storefrontApi().GET("/api/storefront/listings", {
        params: { query: { page, pageSize } },
      });
      const items = data?.items ?? [];
      for (const dto of items) {
        out.push({
          slug: dto.slug ?? "",
          category: dto.category ?? "",
          image: imageUrl(dto.imageUrl),
        });
      }
      const total = typeof data?.total === "number" ? data.total : out.length;
      if (items.length === 0 || out.length >= total) break;
    }
  } catch {
    return out.filter((listing) => listing.slug);
  }
  return out.filter((listing) => listing.slug);
}

export async function getCategories(): Promise<{ name: string; slug: string; count: number }[]> {
  if (!liveCatalogEnabled) return [];
  try {
    // The categories endpoint carries no per-category count, so derive real counts from the live shelf rather than
    // showing a made-up number. Matched on the human category name the listing projection exposes.
    const [{ data }, listings] = await Promise.all([
      storefrontApi().GET("/api/storefront/categories", {}),
      getAllListings(),
    ]);
    const counts = new Map<string, number>();
    for (const listing of listings) {
      const key = listing.category.toLowerCase();
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    return (data ?? []).map((category) => ({
      name: category.name ?? "",
      slug: category.slug ?? "",
      count: counts.get((category.name ?? "").toLowerCase()) ?? 0,
    }));
  } catch {
    return [];
  }
}
