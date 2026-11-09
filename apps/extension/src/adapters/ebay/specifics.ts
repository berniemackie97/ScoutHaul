import { decodeEntities } from "../html";

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

/**
 * eBay keeps a listing's description in a document of its own, linked from the item page. The page carries no
 * description itself, so this is the only way to read one — and it is read from the page already in hand.
 */
export function descriptionUrl(html: string): string | null {
  const candidates = [
    /<iframe[^>]*\bid=["\']?desc_ifr["\']?[^>]*\bsrc=["\']?(https:\/\/[^"\'\s>]+)/i.exec(html)?.[1],
    /\bsrc=["\']?(https:\/\/[^"\'\s>]*ebaydesc\.com\/[^"\'\s>]+)/i.exec(html)?.[1],
  ];
  for (const candidate of candidates) {
    if (!candidate) continue;
    try {
      const address = new URL(candidate.replace(/&amp;/g, "&"));
      if (address.protocol === "https:" && !address.username && !address.password
          && (address.hostname === "ebaydesc.com" || address.hostname.endsWith(".ebaydesc.com"))) {
        return address.toString();
      }
    } catch {
      continue;
    }
  }
  return null;
}

/** The description's own words, with its markup and scripts stripped out. */
export function readDescription(html: string, limit = 4000): string | null {
  const body = html
    .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, " ")
    .replace(/<style\b[^>]*>[\s\S]*?<\/style>/gi, " ")
    .replace(/<br\s*\/?>(?=\s*\S)/gi, "\n")
    .replace(/<\/(p|div|li|tr|h\d)>/gi, "\n")
    .replace(/<[^>]+>/g, " ");
  const text = decodeEntities(body)
    .split("\n")
    .map((line) => line.replace(/[ \t\u00a0]+/g, " ").trim())
    .filter((line) => line.length > 0)
    .join("\n")
    .trim();
  return text.length === 0 ? null : text.slice(0, limit);
}


const blank: ListingSpecifics = { brand: null, color: null, size: null };

export function readSpecifics(html: string): ListingSpecifics {
  for (const block of html.matchAll(/<script\b[^>]*type=["']application\/ld\+json["'][^>]*>(.*?)<\/script>/gis)) {
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
