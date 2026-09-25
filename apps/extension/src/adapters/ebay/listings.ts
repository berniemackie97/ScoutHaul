import type { IngestListing, RunIssue } from "../../agent/types";
import { dig, jsonAfter, spans } from "../embedded-json";
import { money } from "./text";

const currencies: Record<string, string> = {
  EBAY_US: "USD", EBAY_MOTORS_US: "USD", EBAY_GB: "GBP", EBAY_CA: "CAD", EBAY_AU: "AUD",
  EBAY_DE: "EUR", EBAY_FR: "EUR", EBAY_IT: "EUR", EBAY_ES: "EUR", EBAY_IE: "EUR",
};

/**
 * eBay serves one picture at many sizes, named in the address: `s-l140.jpg` is the grid's thumbnail, and the same
 * picture at `s-l1600.jpg` is the one worth keeping. An address that is not named that way is left alone.
 */
function fullSize(url: string): string {
  return url.replace(/\/s-l\d+(\.\w+)(?=$|\?)/i, "/s-l1600$1");
}

/**
 * Reads Manage active listings (`/sh/lst/active`), whose grid the page embeds as JSON under
 * `gridData`. Null when it does not — the page changed.
 */
export function readListingsPage(html: string): { listings: IngestListing[]; issues: RunIssue[]; total: number | null } | null {
  const members = dig(jsonAfter(html, '"gridData":'), "model", "members");
  if (!Array.isArray(members)) {
    return null;
  }

  const heading = spans(dig(jsonAfter(html, '"pageHeader":'), "model", "subTitle")) ?? "";
  const total = /\(([\d,]+)\)/.exec(heading)?.[1];
  const listings: IngestListing[] = [];
  const issues: RunIssue[] = [];
  for (const member of members) {
    const id = dig(member, "entityId");
    if (typeof id !== "string") {
      continue;
    }

    const info = dig(member, "additionalInfo");
    const texts = dig(member, "title", "texts");
    const title = Array.isArray(texts) ? spans(texts[0]) : null;
    const priceNumber = dig(info, "currentPrice");
    const price = typeof priceNumber === "number" ? priceNumber : money(`$${String(dig(member, "price", "content", "currentValue") ?? "")}`);
    if (!title || price === null) {
      issues.push({ message: "A listing's title or price could not be read.", externalReference: id, rejected: true });
      continue;
    }

    const quantity = dig(info, "availableQuantity");
    const marketplace = String(dig(info, "marketplace") ?? "EBAY_US");
    const format = dig(info, "format");
    const startDate = dig(info, "listingStartDate");
    const sku = dig(member, "listingSKU", "content");
    const image = dig(member, "title", "image", "URL");
    listings.push({
      externalListingId: id,
      title,
      price,
      quantity: typeof quantity === "number" && quantity >= 0 ? quantity : 1,
      status: "Active",
      platform: "eBay",
      currency: currencies[marketplace] ?? "USD",
      format: format === "AUCTION" ? "Auction" : format === "FIXED_PRICE" ? "FixedPrice" : null,
      sku: Array.isArray(sku) ? spans(sku[0]) : null,
      url: `https://www.ebay.com/itm/${id}`,
      primaryImageUrl: typeof image === "string" ? fullSize(image) : null,
      // Manage active listings shows one picture per listing, so that is what a scan can bring back; the rest
      // of a listing's gallery lives on its own page, which a scan does not open.
      photoUrls: typeof image === "string" ? [fullSize(image)] : [],
      listedAt: typeof startDate === "string" && !Number.isNaN(Date.parse(startDate)) ? new Date(startDate).toISOString() : null,
    });
  }

  return { listings, issues, total: total ? Number(total.replace(/,/g, "")) : null };
}
