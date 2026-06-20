import { storefrontApi, liveCatalogEnabled, STORE } from "./store";

// One catalog item, projected from the org's real inventory via /api/storefront/*.
export type Specimen = {
  id: string;
  slug: string;
  sku?: string;
  title: string;
  brand?: string;
  category: string;
  condition: string;
  price: number;
  compareAt?: number;
  era?: string;
  blurb: string;
  /** Absolute URL of the item's hero photo (served by the API), if any. */
  image?: string;
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
      category: data.category ?? "",
      condition: data.condition ?? "Good",
      price: data.listPrice ?? 0,
      compareAt: data.compareAtPrice ?? undefined,
      blurb: data.description ?? data.subtitle ?? "",
      image: data.imageUrl ? STORE.apiBaseUrl + data.imageUrl : undefined,
    };
  } catch {
    return null;
  }
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
