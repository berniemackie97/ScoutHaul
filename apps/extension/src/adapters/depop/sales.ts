import type { FulfillmentStage, IngestOrder, IngestOrderLine, RunIssue } from "../../agent/types";
import { allocate, cents } from "../amounts";
import { decodeEntities, textsOf } from "../html";

/** A sale's own link in the sold list; its label names the order and the buyer. */
export const soldRowSelector = 'a[aria-label^="View receipt"]';

/** The receipt a sale's own page opens over the list. */
export const receiptSelector = 'aside[role="dialog"] table';

export interface ShipTo {
  line1: string | null;
  city: string | null;
  region: string | null;
  postalCode: string | null;
  country: string | null;
}

/** One sale in Seller Hub's sold list. */
export interface SoldRow {
  id: string;
  buyer: string;
  items: number;
  /** As Depop prints it, MM/DD/YYYY in the seller's own time. */
  soldOn: string | null;
  method: string | null;
  carrier: string | null;
  status: string | null;
  total: number | null;
  shipTo: ShipTo;
}

export interface ReceiptProduct {
  slug: string;
  title: string;
  price: number | null;
}

/** What a sale's receipt says about its money, its parcel and what was in it. */
export interface Receipt {
  buyerName: string | null;
  status: string | null;
  deliveredOn: string | null;
  carrier: string | null;
  tracking: string | null;
  itemsPrice: number | null;
  /** Every line between the items price and the total: Depop's fee, the payment fee, and any other. */
  fees: Record<string, number>;
  total: number | null;
  paidOut: boolean;
  buyerShipping: number | null;
  products: ReceiptProduct[];
}

/**
 * The sold list, or null when the page carries no list at all — which is a changed site, not a seller with nothing
 * sold: the list's own column headings are there either way.
 */
export function readSoldList(html: string): SoldRow[] | null {
  const rows: SoldRow[] = [];
  for (const block of html.matchAll(/<li\b[\s\S]*?<\/li>/gi)) {
    const chunk = block[0];
    const label = /aria-label="View receipt (\d+) to buyer ([^"]+)"/.exec(chunk);
    if (!label) {
      continue;
    }

    const texts = textsOf(chunk);
    const count = texts.findIndex((text) => /^items? sold to$/i.test(text));
    const soldOn = texts.find((text, index) => index > 0 && /^Sold on$/i.test(texts[index - 1]!) && /^\d{2}\/\d{2}\/\d{4}$/.test(text)) ?? null;
    const totalAt = texts.findIndex((text) => /^US\$[\d,]+(\.\d+)?$/.test(text));
    const agoAt = texts.findIndex((text) => /\bago$/i.test(text));
    // Between how long ago it sold and the total: the shipping method, its carrier when there is one, and the status.
    const between = agoAt >= 0 && totalAt > agoAt ? texts.slice(agoAt + 1, totalAt) : [];
    rows.push({
      id: label[1]!,
      buyer: decodeEntities(label[2]!),
      items: count > 0 ? Number(texts[count - 1]) || 1 : 1,
      soldOn,
      method: between.length >= 2 ? between[0]! : null,
      carrier: between.length === 3 ? between[1]! : null,
      status: between.at(-1) ?? null,
      total: totalAt >= 0 ? money(texts[totalAt]!) : null,
      shipTo: shipTo(chunk),
    });
  }

  return rows.length > 0 || /Buyer info/i.test(html) ? rows : null;
}

/** The receipt a sale's page opens, or null when the page shows none. */
export function readReceipt(html: string): Receipt | null {
  const marker = html.indexOf('aria-label="View Receipt modal"');
  if (marker < 0) {
    return null;
  }

  const start = html.lastIndexOf("<aside", marker);
  const end = html.indexOf("</aside>", marker);
  const aside = html.slice(start < 0 ? marker : start, end < 0 ? html.length : end);
  const texts = textsOf(aside);
  const after = (label: RegExp) => {
    const at = texts.findIndex((text) => label.test(text));
    return at >= 0 ? texts[at + 1] ?? null : null;
  };

  const handleAt = texts.findIndex((text) => /^@[A-Za-z0-9_.-]+$/.test(text));
  const name = handleAt > 0 ? texts[handleAt - 1]! : null;
  const reference = texts.findIndex((text) => /^\S+ Ref$/.test(text));
  const tracked = /origTrackNum=([A-Za-z0-9]+)/.exec(aside)?.[1] ?? (reference >= 0 ? texts[reference + 1] ?? null : null);

  const table: [string, string][] = [];
  for (const row of aside.matchAll(/<tr\b[\s\S]*?<\/tr>/gi)) {
    const cells = textsOf(row[0]);
    if (cells.length >= 2) {
      table.push([cells[0]!, cells.at(-1)!]);
    }
  }

  const fees: Record<string, number> = {};
  for (const [label, value] of table) {
    if (!/^(Items Price|Total)$/i.test(label)) {
      fees[label] = money(value) ?? 0;
    }
  }

  return {
    // The avatar's initials sit where a name would be when the buyer gave none.
    buyerName: name && !/^[A-Z]{1,3}$/.test(name) ? name : null,
    status: after(/^Status$/i),
    deliveredOn: after(/^Delivered on$/i),
    carrier: reference >= 0 ? texts[reference]!.replace(/ Ref$/, "") : /tools\.usps\.com/.test(aside) ? "USPS" : null,
    tracking: tracked,
    itemsPrice: money(table.find(([label]) => /^Items Price$/i.test(label))?.[1] ?? ""),
    fees,
    total: money(table.find(([label]) => /^Total$/i.test(label))?.[1] ?? ""),
    paidOut: /sent to your bank account/i.test(aside),
    buyerShipping: money(/Buyer paid (US\$[\d,.]+) for shipping/i.exec(texts.join(" "))?.[1] ?? ""),
    products: products(aside),
  };
}

/** Refunded and cancelled sales did not happen, as far as the money goes. */
export function cancelled(status: string | null): boolean {
  return status !== null && /refund|cancel/i.test(status);
}

/** Everything about a sale in the list that, when it changes, means its receipt is read again. */
export function saleStamp(row: SoldRow): string {
  return [row.status, row.total, row.method, row.carrier].join("|");
}

/** A printed MM/DD/YYYY as midday that day in this browser's time — the day is all Depop prints. */
export function dayOf(printed: string | null): Date | null {
  const match = printed ? /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(printed) : null;
  return match ? new Date(Number(match[3]), Number(match[1]) - 1, Number(match[2]), 12) : null;
}

export function fulfillment(status: string | null): FulfillmentStage {
  if (status === null) {
    return "Unknown";
  }

  if (/delivered|completed/i.test(status)) {
    return "Delivered";
  }

  if (/shipped|in transit/i.test(status)) {
    return "Shipped";
  }

  return /to ship/i.test(status) ? "NeedsShipping" : "Unknown";
}

/**
 * One Depop sale, described the way the Depop sales report describes it, so a sale that arrives from both lands as one
 * order. With Depop's own shipping the buyer pays for the label and Depop charges the seller exactly that, so what the
 * buyer paid for shipping is both a charge and a cost. Depop collects and remits sales tax, and the receipt does not
 * show it, so tax is left to the report.
 */
export function toIngestOrder(row: SoldRow, receipt: Receipt): { order: IngestOrder | null; issues: RunIssue[] } {
  const reject = (message: string) => ({ order: null, issues: [{ message, externalReference: row.id, rejected: true }] });
  const soldOn = dayOf(row.soldOn);
  if (!soldOn) {
    return reject(`The sale date "${row.soldOn ?? ""}" could not be read.`);
  }

  if (receipt.products.length === 0) {
    return reject("The receipt does not say which listing sold.");
  }

  const itemsPrice = receipt.itemsPrice ?? receipt.products.reduce((sum, product) => sum + (product.price ?? 0), 0);
  const shares = allocate(itemsPrice, receipt.products.map((product) => product.price));
  const lines: IngestOrderLine[] = receipt.products.map((product, index) => ({
    title: product.title,
    unitPrice: shares[index]!,
    quantity: 1,
    externalItemId: product.slug,
  }));

  const fees = cents(Object.values(receipt.fees).reduce((sum, fee) => sum + fee, 0));
  const shipping = receipt.buyerShipping ?? 0;
  const depopShipping = row.method !== null && /depop shipping/i.test(row.method);
  const delivered = dayOf(receipt.deliveredOn);
  const status = receipt.status ?? row.status;
  const issues: RunIssue[] = row.items > receipt.products.length
    ? [{ message: `Depop lists ${row.items} items in this sale but its receipt names ${receipt.products.length}.`, externalReference: row.id, rejected: false }]
    : [];

  return {
    order: {
      externalOrderId: row.id,
      orderedAt: soldOn.toISOString(),
      orderedOnDayOnly: true,
      itemsSubtotal: itemsPrice,
      shippingCharged: shipping,
      taxCollected: null,
      taxRemittedByPlatform: true,
      feeTotal: fees,
      shippingCost: depopShipping ? shipping : null,
      payoutAmount: receipt.paidOut ? receipt.total : null,
      feeBreakdownJson: JSON.stringify({ source: "Depop receipt", ...receipt.fees, shippingLabel: depopShipping ? shipping : null }),
      currency: "USD",
      platform: "Depop",
      lines,
      buyerName: receipt.buyerName,
      buyerReference: row.buyer,
      shipToName: receipt.buyerName,
      shipToLine1: row.shipTo.line1,
      shipToCity: row.shipTo.city,
      shipToRegion: row.shipTo.region,
      shipToPostalCode: row.shipTo.postalCode,
      shipToCountry: row.shipTo.country,
      carrier: receipt.carrier ?? row.carrier,
      trackingNumber: receipt.tracking,
      deliveredAt: delivered?.toISOString() ?? null,
      fulfillmentStage: fulfillment(status),
      fulfillmentDetail: status,
      rawJson: JSON.stringify({ status, method: row.method, items: row.items }),
    },
    issues,
  };
}

function shipTo(chunk: string): ShipTo {
  const parts = [...chunk.matchAll(/<span\b[^>]*class="[^"]*locationDetails[^"]*"[^>]*>([^<]*)<\/span>/gi)]
    .map((match) => decodeEntities(match[1]!).trim())
    .filter((part) => part.length > 0);
  const cityAt = parts.findIndex((part) => /^.+,\s*[A-Z]{2}$/.test(part));
  const city = cityAt >= 0 ? /^(.+),\s*([A-Z]{2})$/.exec(parts[cityAt]!) : null;
  return {
    line1: cityAt > 0 ? parts[0]! : null,
    city: city?.[1]?.trim() ?? null,
    region: city?.[2] ?? null,
    postalCode: parts.find((part) => /^\d{5}(-\d{4})?$/.test(part)) ?? null,
    country: parts.length > 0 && /^[A-Z]{2}$/.test(parts.at(-1)!) ? parts.at(-1)! : null,
  };
}

function products(aside: string): ReceiptProduct[] {
  const found: ReceiptProduct[] = [];
  for (const link of aside.matchAll(/<a\b([^>]*href="\/products\/([^/"]+)\/"[^>]*)>([\s\S]*?)<\/a>/gi)) {
    const inner = link[3]!;
    const alt = /\balt="([^"]*)"/.exec(inner)?.[1];
    const described = /data-testid="receipt-product-description"[^>]*>([^<]*)</.exec(inner)?.[1];
    const title = decodeEntities(alt ?? described ?? "").trim();
    found.push({
      slug: link[2]!,
      title: title.length > 0 ? title : "Untitled Depop item",
      price: money(/aria-description="Price"[^>]*>([^<]*)</.exec(inner)?.[1] ?? ""),
    });
  }

  return found;
}

function money(text: string): number | null {
  const digits = text.replace(/[^0-9.]/g, "");
  const value = digits.length > 0 ? Number(digits) : Number.NaN;
  return Number.isFinite(value) ? cents(value) : null;
}
