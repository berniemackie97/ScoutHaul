import { storefrontApi, liveCatalogEnabled, STORE } from "./store";
import { sampleSpecimens, sampleCategories, type Specimen } from "./sample-data";

// Storefront data access. Calls the live public API (/api/storefront/*) when a store
// is configured; falls back to representative sample data on error or before the API
// is deployed / items are published — so the store never renders empty.

function toSlug(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
}

function filterSample(items: Specimen[], opts?: { category?: string; search?: string }): Specimen[] {
  let result = items;
  if (opts?.category) {
    result = result.filter((item) => toSlug(item.category) === opts.category);
  }
  if (opts?.search) {
    const query = opts.search.toLowerCase();
    result = result.filter(
      (item) =>
        item.title.toLowerCase().includes(query) ||
        (item.brand?.toLowerCase().includes(query) ?? false) ||
        item.category.toLowerCase().includes(query),
    );
  }
  return result;
}

export async function getListings(opts?: { category?: string; search?: string }): Promise<Specimen[]> {
  if (liveCatalogEnabled) {
    try {
      const { data } = await storefrontApi().GET("/api/storefront/listings", {
        params: { query: { category: opts?.category, search: opts?.search, page: 1, pageSize: 48 } },
      });
      const items = data?.items ?? [];
      if (items.length > 0) {
        return items.map(
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
      }
    } catch {
      // fall through to sample data
    }
  }
  return filterSample(sampleSpecimens, opts);
}

export async function getListing(slug: string): Promise<Specimen | null> {
  if (liveCatalogEnabled) {
    try {
      const { data } = await storefrontApi().GET("/api/storefront/listings/{slug}", {
        params: { path: { slug } },
      });
      if (data) {
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
      }
    } catch {
      // fall through to sample data
    }
  }
  return sampleSpecimens.find((item) => item.slug === slug) ?? null;
}

export async function getRelated(slug: string, limit = 4): Promise<Specimen[]> {
  const all = await getListings();
  const current = all.find((item) => item.slug === slug);
  return all
    .filter((item) => item.slug !== slug && (!current || !current.category || item.category === current.category))
    .slice(0, limit);
}

export async function getCategories(): Promise<{ name: string; slug: string; count: number }[]> {
  if (liveCatalogEnabled) {
    try {
      const { data } = await storefrontApi().GET("/api/storefront/categories", {});
      if (data && data.length > 0) {
        return data.map((category) => ({ name: category.name ?? "", slug: category.slug ?? "", count: 0 }));
      }
    } catch {
      // fall through to sample data
    }
  }
  return sampleCategories;
}
