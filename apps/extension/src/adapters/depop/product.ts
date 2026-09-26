import type { IngestListing } from "../../agent/types";
import { siteChanged } from "./pages";

/**
 * A listing, from its own page. Depop publishes the whole of it as structured data — title, description, every
 * photo, brand, price and whether it is still for sale — so nothing here is scraped out of markup that changes.
 */
export function readProduct(slug: string, html: string): IngestListing {
  const product = structuredProduct(html);
  const offers = (product.offers ?? {}) as Record<string, unknown>;
  const price = Number(typeof offers.price === "string" ? offers.price : offers.price);
  if (typeof product.name !== "string" || !Number.isFinite(price)) {
    throw siteChanged("listing");
  }

  const sold = typeof offers.availability === "string" && /SoldOut|OutOfStock/i.test(offers.availability);
  const images = Array.isArray(product.image) ? product.image.filter((url): url is string => typeof url === "string") : [];
  return {
    externalListingId: slug,
    title: product.name.trim(),
    price,
    quantity: sold ? 0 : 1,
    quantitySold: sold ? 1 : 0,
    status: sold ? "Sold" : "Active",
    platform: "Depop",
    currency: typeof offers.priceCurrency === "string" ? offers.priceCurrency.toUpperCase() : "USD",
    brand: name(product.brand),
    size: size(html),
    condition: condition(offers.itemCondition, html),
    description: typeof product.description === "string" && product.description.trim().length > 0
      ? decode(product.description).trim()
      : undefined,
    url: `https://www.depop.com/products/${slug}/`,
    primaryImageUrl: images[0] ?? null,
    photoUrls: images.length > 0 ? images : undefined,
  };
}

/** The `Product` block Depop publishes for every listing. */
function structuredProduct(html: string): Record<string, unknown> {
  for (const block of html.matchAll(/<script type="application\/ld\+json">(.*?)<\/script>/gs)) {
    let data: unknown;
    try {
      data = JSON.parse(block[1]!);
    } catch {
      continue;
    }

    if (typeof data === "object" && data !== null && (data as { "@type"?: unknown })["@type"] === "Product") {
      return data as Record<string, unknown>;
    }
  }

  throw siteChanged("listing");
}

/**
 * Depop states the size on the page rather than in its structured data, on a line with the condition and brand:
 * "Size 26 • Excellent condition • Lane Bryant".
 */
function size(html: string): string | null {
  const match = /Size\s+([^<•|]{1,24}?)\s*(?:<|•)/i.exec(html);
  return match ? match[1]!.trim() || null : null;
}

/** The seller's own words for the condition when the page gives them, and schema.org's when it does not. */
function condition(structured: unknown, html: string): string | null {
  const stated = /(Brand new|Like new|Excellent|Good|Fair)\s+condition/i.exec(html);
  if (stated) {
    return `${stated[1]![0]!.toUpperCase()}${stated[1]!.slice(1).toLowerCase()} condition`;
  }

  if (typeof structured !== "string") {
    return null;
  }

  return /NewCondition/i.test(structured) ? "New" : /UsedCondition/i.test(structured) ? "Used" : null;
}

function name(value: unknown): string | null {
  const brand = typeof value === "object" && value !== null ? (value as { name?: unknown }).name : value;
  return typeof brand === "string" && brand.trim().length > 0 ? brand.trim() : null;
}

const entities: Record<string, string> = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: " ", "#39": "'" };

function decode(text: string): string {
  return text.replace(/&(#\d+|[a-z]+);/gi, (whole, code: string) => {
    const known = entities[code.toLowerCase()];
    if (known !== undefined) {
      return known;
    }

    const numeric = code.startsWith("#") ? Number(code.slice(1)) : Number.NaN;
    return Number.isFinite(numeric) ? String.fromCodePoint(numeric) : whole;
  });
}
