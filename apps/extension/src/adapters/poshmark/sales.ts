import type { FulfillmentStage, IngestOrder, IngestOrderLine, RunIssue } from "../../agent/types";
import { cents } from "../amounts";
import { dig } from "../embedded-json";
import { readState } from "./state";

/** One page of Seller Hub's sales list, and how to ask for the next. */
export interface SalesPage {
  sales: unknown[];
  nextMaxId: string | null;
  found: boolean;
}

export function readSalesPage(html: string): SalesPage {
  const data = dig(readState(html, "sales"), "$_order_list", "userSales", "data");
  const sales = dig(data, "sales_summary");
  if (!Array.isArray(sales)) {
    return { sales: [], nextMaxId: null, found: false };
  }

  const cursor = dig(data, "more", "next_max_id");
  return { sales, nextMaxId: typeof cursor === "string" && cursor.length > 0 ? cursor : null, found: true };
}

export type SaleState = "open" | "completed" | "cancelled";

export function saleState(sale: unknown): SaleState {
  const words = `${text(dig(sale, "display_status")) ?? ""} ${text(dig(sale, "state")) ?? ""}`;
  // A return Poshmark accepts refunds the buyer in full and the seller is never paid: the sale did not happen.
  if (/cancel|refunded|returned(?! requested)/i.test(words)) {
    return "cancelled";
  }

  return /order complete|completed/i.test(words) ? "completed" : "open";
}

export function fulfillment(sale: unknown): { stage: FulfillmentStage; detail: string | null } {
  const detail = text(dig(sale, "display_status"));
  const words = detail ?? "";
  if (/order complete|delivered/i.test(words)) {
    return { stage: "Delivered", detail };
  }

  if (/shipped|in transit/i.test(words)) {
    return { stage: "Shipped", detail };
  }

  if (/^sold$|ship|label/i.test(words)) {
    return { stage: "NeedsShipping", detail };
  }

  return { stage: "Unknown", detail };
}

/** Everything in the list that, when it changes, means the sale must be sent again. */
export function saleStamp(sale: unknown): string {
  return [dig(sale, "state"), dig(sale, "display_status"), dig(sale, "total_price_amount", "val"), dig(sale, "seller_earning_amount", "val")].join("|");
}

export function saleId(sale: unknown): string | null {
  const id = dig(sale, "id");
  return typeof id === "string" && id.length > 0 ? id : null;
}

export function bookedAt(sale: unknown): number {
  const at = dig(sale, "inventory_booked_at");
  return typeof at === "string" ? Date.parse(at) : Number.NaN;
}

export function bundleSize(sale: unknown): number {
  if (dig(sale, "is_bundle") !== true) {
    return 1;
  }

  const size = Number(dig(sale, "bundle_size"));
  return Number.isInteger(size) && size > 1 ? size : 1;
}

/** Poshmark names no listing on a sale; the listing id is the folder its pictures are kept in. */
export function listingIds(text: string): string[] {
  return [...new Set([...text.matchAll(/\/posts\/\d{4}\/\d{2}\/\d{2}\/([0-9a-f]{24})\//gi)].map((match) => match[1]!.toLowerCase()))];
}

/**
 * One Poshmark sale. The buyer pays shipping and tax to Poshmark, which prepays the label and remits the tax, so the
 * seller's side is the sale total less Poshmark's cut: what it keeps is exactly total minus seller earning, and that
 * earning is paid out only once the order completes.
 */
export function toIngestOrder(sale: unknown, bundleListingIds: string[] | null): { order: IngestOrder | null; issues: RunIssue[] } {
  const id = saleId(sale)!;
  const reject = (message: string) => ({ order: null, issues: [{ message, externalReference: id, rejected: true }] });

  const total = money(dig(sale, "total_price_amount"));
  if (total === null) {
    return reject("The sale total could not be read.");
  }

  const at = bookedAt(sale);
  if (!Number.isFinite(at)) {
    return reject(`The sale date "${String(dig(sale, "inventory_booked_at") ?? "")}" could not be read.`);
  }

  const title = text(dig(sale, "title")) ?? "Untitled Poshmark item";
  const size = bundleSize(sale);
  let lines: IngestOrderLine[];
  const issues: RunIssue[] = [];
  if (size > 1) {
    if (bundleListingIds === null || bundleListingIds.length !== size) {
      return reject(
        `Poshmark sold ${size} listings together as one bundle for $${total.toFixed(2)} and did not say which. `
        + "The next listings scan puts each of them under Sold, not recorded; record them there, splitting the total between them.");
    }

    const share = cents(total / size);
    lines = bundleListingIds.map((listingId, index) => ({
      title: `${title} (bundle item ${index + 1} of ${size})`,
      unitPrice: index === size - 1 ? cents(total - share * (size - 1)) : share,
      quantity: 1,
      externalItemId: listingId,
    }));
    issues.push({
      message: `A bundle of ${size} listings: Poshmark gives only the bundle's total, so it is split evenly between them. Adjust the split on the order if it matters.`,
      externalReference: id,
      rejected: false,
    });
  } else {
    const picture = dig(sale, "picture_url");
    lines = [{ title, unitPrice: total, quantity: 1, externalItemId: typeof picture === "string" ? listingIds(picture)[0] ?? null : null }];
  }

  const earning = money(dig(sale, "seller_earning_amount"));
  if (earning === null || earning > total) {
    issues.push({ message: "Poshmark's cut of this sale could not be read, so its fees are left open.", externalReference: id, rejected: false });
  }

  const known = earning !== null && earning <= total;
  const stage = fulfillment(sale);
  const buyer = dig(sale, "buyer");
  const address = dig(sale, "shipping_address");
  const full = text(dig(buyer, "full_name")) ?? ([text(dig(buyer, "first_name")), text(dig(buyer, "last_name"))].filter(Boolean).join(" ") || null);
  return {
    order: {
      externalOrderId: id,
      orderedAt: new Date(at).toISOString(),
      paidAt: new Date(at).toISOString(),
      itemsSubtotal: total,
      shippingCharged: 0,
      taxCollected: 0,
      taxRemittedByPlatform: true,
      feeTotal: known ? cents(total - earning) : null,
      feesPending: !known,
      shippingCost: 0,
      payoutAmount: known && saleState(sale) === "completed" ? earning : null,
      // Poshmark's cut includes any shipping discount the seller offered; the list does not separate the two.
      feeBreakdownJson: known ? JSON.stringify({ source: "Poshmark sales", poshmarkCut: cents(total - earning), sellerEarning: earning }) : null,
      currency: (text(dig(sale, "total_price_amount", "currency_code")) ?? "USD").toUpperCase(),
      platform: "Poshmark",
      lines,
      buyerName: full,
      buyerReference: text(dig(buyer, "username")),
      shipToName: text(dig(address, "name")),
      shipToLine1: text(dig(address, "street")),
      shipToLine2: text(dig(address, "street2")),
      shipToCity: text(dig(address, "city")),
      shipToRegion: text(dig(address, "state")),
      shipToPostalCode: text(dig(address, "zip")),
      shipToCountry: text(dig(address, "country")),
      fulfillmentStage: stage.stage,
      fulfillmentDetail: stage.detail,
      rawJson: JSON.stringify({ state: dig(sale, "state") ?? null, displayStatus: stage.detail, size: text(dig(sale, "size")), bundleSize: size }),
    },
    issues,
  };
}

function money(value: unknown): number | null {
  const raw = dig(value, "val");
  const number = typeof raw === "number" ? raw : typeof raw === "string" && raw.trim() !== "" ? Number(raw) : Number.NaN;
  return Number.isFinite(number) && number >= 0 ? cents(number) : null;
}

function text(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
}
