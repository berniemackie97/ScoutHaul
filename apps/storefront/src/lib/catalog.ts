import { storefrontApi, liveCatalogEnabled, STORE } from "./store";

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

export async function getListings(opts?: { category?: string; search?: string }): Promise<Specimen[]> {
  if (!liveCatalogEnabled) return [];
  try {
    const { data } = await storefrontApi().GET("/api/storefront/listings", {
      params: { query: { category: opts?.category, search: opts?.search, page: 1, pageSize: 48 } },
    });
    return (data?.items ?? []).map(
      (dto): Specimen => ({
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
        image: dto.imageUrl ? STORE.apiBaseUrl + dto.imageUrl : undefined,
      }),
    );
  } catch {
    return [];
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
      color: data.color ?? undefined,
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
      image: data.imageUrl ? STORE.apiBaseUrl + data.imageUrl : undefined,
      images: (data.imageUrls ?? []).map((url) => STORE.apiBaseUrl + url),
      availability: data.availability ?? "Available",
    };
  } catch {
    return null;
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

export async function getRelated(slug: string, limit = 4): Promise<Specimen[]> {
  const all = await getListings();
  const current = all.find((item) => item.slug === slug);
  return all
    .filter((item) => item.slug !== slug && (!current || !current.category || item.category === current.category))
    .slice(0, limit);
}

export async function getCategories(): Promise<{ name: string; slug: string; count: number }[]> {
  if (!liveCatalogEnabled) return [];
  try {
    const { data } = await storefrontApi().GET("/api/storefront/categories", {});
    return (data ?? []).map((category) => ({ name: category.name ?? "", slug: category.slug ?? "", count: 0 }));
  } catch {
    return [];
  }
}
