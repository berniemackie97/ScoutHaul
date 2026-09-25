/**
 * What a listing says the thing is — brand, colour, size — from the item page's structured data, where eBay
 * publishes its item specifics as `additionalProperty`.
 *
 * Read from the page the gallery pass already fetched, so it costs no extra request. Only specifics eBay names
 * plainly are taken: a listing's specifics run to dozens of rows, and inventory wants the few a person would
 * recognise rather than everything a category happens to define.
 */

export interface ListingSpecifics {
  brand: string | null;
  color: string | null;
  size: string | null;
}

const blank: ListingSpecifics = { brand: null, color: null, size: null };

export function readSpecifics(html: string): ListingSpecifics {
  for (const block of html.matchAll(/<script type="application\/ld\+json">(.*?)<\/script>/gs)) {
    let data: unknown;
    try {
      data = JSON.parse(block[1]!);
    } catch {
      continue;
    }

    if (!isProduct(data)) {
      continue;
    }

    const named = new Map<string, string>();
    for (const property of data.additionalProperty ?? []) {
      const name = typeof property?.name === "string" ? property.name.trim().toLowerCase() : null;
      const value = typeof property?.value === "string" ? property.value.trim() : null;
      if (name && value && !named.has(name)) {
        named.set(name, value);
      }
    }

    return {
      brand: named.get("brand") ?? null,
      color: named.get("color") ?? named.get("colour") ?? null,
      size: named.get("size") ?? pick(named, /\bsize\b/) ?? null,
    };
  }

  return blank;
}

function pick(named: Map<string, string>, pattern: RegExp): string | null {
  for (const [name, value] of named) {
    if (pattern.test(name)) {
      return value;
    }
  }

  return null;
}

function isProduct(data: unknown): data is { additionalProperty?: { name?: unknown; value?: unknown }[] } {
  return typeof data === "object" && data !== null && (data as { "@type"?: unknown })["@type"] === "Product";
}
