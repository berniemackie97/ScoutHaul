import { sampleSpecimens, sampleCategories, type Specimen } from "./sample-data";

// Storefront data access. Renders from representative sample data today; the seams
// below swap to the typed @overhaul/api-client (/api/storefront/*) once the API with
// the commerce migration is deployed and items are published.

function toSlug(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
}

export async function getListings(opts?: { category?: string; search?: string }): Promise<Specimen[]> {
  let items = sampleSpecimens;

  if (opts?.category) {
    items = items.filter((item) => toSlug(item.category) === opts.category);
  }

  if (opts?.search) {
    const query = opts.search.toLowerCase();
    items = items.filter(
      (item) =>
        item.title.toLowerCase().includes(query) ||
        (item.brand?.toLowerCase().includes(query) ?? false) ||
        item.category.toLowerCase().includes(query),
    );
  }

  return items;
}

export async function getListing(slug: string): Promise<Specimen | null> {
  return sampleSpecimens.find((item) => item.slug === slug) ?? null;
}

export async function getRelated(slug: string, limit = 4): Promise<Specimen[]> {
  const current = sampleSpecimens.find((item) => item.slug === slug);
  return sampleSpecimens
    .filter((item) => item.slug !== slug && (!current || item.category === current.category))
    .slice(0, limit);
}

export async function getCategories() {
  return sampleCategories;
}
