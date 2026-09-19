import type { IngestOrder, IngestOrderLine, RunIssue } from "../../agent/types";
import { dig, jsonAfter, spans } from "../embedded-json";
import type { LedgerOrder } from "./ledger";
import { cents } from "../amounts";
import { money, saleTime } from "./text";

/** One order as Seller Hub's order list carries it. */
export interface EbayOrderRow {
  orderId: string;
  created: string | null;
  status: string | null;
  subStatus: string | null;
  soldFor: number | null;
  shipping: number | null;
  total: number | null;
  buyerUsername: string | null;
  buyerName: string | null;
  street1: string | null;
  street2: string | null;
  city: string | null;
  region: string | null;
  postalCode: string | null;
  lines: EbayOrderLine[];
}

export interface EbayOrderLine {
  listingId: string | null;
  title: string | null;
  quantity: number;
  unitPrice: number | null;
  tracking: string | null;
}

/**
 * Reads Seller Hub's order list (`/sh/ord/`). The page embeds its grid as JSON under `ordersData`;
 * null when it does not — the page changed, and the caller says so rather than reading nothing.
 */
export function readOrdersPage(html: string): { rows: EbayOrderRow[]; total: number | null } | null {
  const data = jsonAfter(html, '"ordersData":');
  const members = dig(data, "_initialValue", "orders", "members");
  if (!Array.isArray(members)) {
    return null;
  }

  const summary = spans(dig(data, "_initialValue", "pagination", "summary")) ?? "";
  const total = /of\s+([\d,]+)/i.exec(summary)?.[1];
  return {
    rows: members.flatMap((member) => (typeof dig(member, "orderId") === "string" ? [row(member)] : [])),
    total: total ? Number(total.replace(/,/g, "")) : null,
  };
}

function row(member: unknown): EbayOrderRow {
  const buyer = dig(member, "__sh", "buyerDetails");
  const address = dig(buyer, "toShippingAddress");
  const items = dig(member, "orderLineItems");
  return {
    orderId: dig(member, "orderId") as string,
    created: spans(dig(member, "creationDate"), " | "),
    status: spans(dig(member, "orderStatus")),
    subStatus: spans(dig(member, "__sh", "orderSubStatus")),
    soldFor: money(spans(dig(member, "__sh", "soldFor"))),
    shipping: money(spans(dig(member, "__sh", "shippingCost"), " | ")),
    total: money(spans(dig(member, "displayTotalPrice"))),
    buyerUsername: spans(dig(buyer, "buyerid")),
    buyerName: spans(dig(buyer, "fullName")),
    street1: spans(dig(address, "street1")),
    street2: spans(dig(address, "street2")),
    city: spans(dig(address, "city")),
    region: spans(dig(address, "stateOrProvince")),
    postalCode: spans(dig(member, "postalCode", "value")),
    lines: Array.isArray(items) ? items.map(line) : [],
  };
}

function line(item: unknown): EbayOrderLine {
  const summary = dig(item, "listingSummary");
  const quantity = Number(spans(dig(summary, "quantity")) ?? "1");
  const points = dig(item, "itemDetailsOverlay", "dataPoints");
  const priceText = Array.isArray(points)
    ? points
        .filter((point) => /item price/i.test(spans(dig(point, "label", "text")) ?? ""))
        .map((point) => spans(dig(point, "value", "text")))[0]
    : null;
  return {
    listingId: typeof dig(summary, "listingId") === "string" ? (dig(summary, "listingId") as string) : null,
    title: spans(dig(summary, "title")),
    quantity: Number.isInteger(quantity) && quantity > 0 ? quantity : 1,
    unitPrice: money(priceText ?? null),
    tracking: spans(dig(item, "__sh", "trackingId")),
  };
}

/**
 * One order, as the landing ingest takes it: the order list's facts, with the money after the sale
 * taken from the seller's ledger. An order the ledger has not caught up with yet is sent with its
 * fees marked pending — never with fees of nought — and is completed by a later scan.
 */
export function toIngestOrder(row: EbayOrderRow, ledger: LedgerOrder | undefined, now: Date): { order?: IngestOrder; issues: RunIssue[] } {
  const reject = (message: string) => ({ issues: [{ message, externalReference: row.orderId, rejected: true }] });

  const orderedAt = saleTime(row.created, now);
  if (!orderedAt) {
    return reject(`The sale time "${row.created ?? ""}" could not be read.`);
  }

  if (row.lines.length === 0) {
    return reject("The order lists no items.");
  }

  const lines: IngestOrderLine[] = [];
  for (const item of row.lines) {
    const unitPrice = item.unitPrice ?? (row.lines.length === 1 && row.soldFor !== null ? cents(row.soldFor / item.quantity) : null);
    if (unitPrice === null) {
      return reject(`The price of "${item.title ?? "an item"}" could not be read.`);
    }

    lines.push({ title: item.title ?? "Untitled eBay item", unitPrice, quantity: item.quantity, externalItemId: item.listingId });
  }

  const issues: RunIssue[] = [];
  const subtotal = row.soldFor ?? cents(lines.reduce((sum, item) => sum + (item.unitPrice ?? 0) * (item.quantity ?? 1), 0));
  const shipping = row.shipping ?? 0;
  let tax = row.total === null ? 0 : cents(row.total - subtotal - shipping);
  if (row.total === null) {
    issues.push({ message: "The order total could not be read, so its sales tax is recorded as none.", externalReference: row.orderId, rejected: false });
  } else if (tax < 0) {
    issues.push({ message: "The order total is less than its items and shipping; its sales tax is recorded as none.", externalReference: row.orderId, rejected: false });
    tax = 0;
  }

  const posted = ledger?.gross !== null && ledger?.gross !== undefined && ledger.net !== null;
  const labels = ledger ? cents(ledger.labels.reduce((sum, label) => sum + label, 0)) : 0;
  const adFees = ledger ? cents(ledger.adFees.reduce((sum, fee) => sum + fee, 0)) : 0;
  const transactionFees = posted ? cents(ledger!.gross! - ledger!.net!) : null;

  const order: IngestOrder = {
    externalOrderId: row.orderId,
    orderedAt: orderedAt.toISOString(),
    itemsSubtotal: subtotal,
    shippingCharged: shipping,
    taxCollected: tax,
    // eBay collects and remits US sales tax for its sellers.
    taxRemittedByPlatform: true,
    feeTotal: posted ? cents(transactionFees! + adFees) : null,
    feesPending: !posted,
    shippingCost: ledger && ledger.labels.length > 0 ? labels : null,
    payoutAmount: posted ? cents(ledger!.net! - labels - adFees) : null,
    feeBreakdownJson: posted ? JSON.stringify({ source: "eBay transaction ledger", transactionFees, adFees }) : null,
    currency: "USD",
    platform: "eBay",
    lines,
    buyerReference: row.buyerUsername,
    buyerName: row.buyerName,
    shipToName: row.buyerName,
    shipToLine1: row.street1,
    shipToLine2: row.street2,
    shipToCity: row.city,
    shipToRegion: row.region,
    shipToPostalCode: row.postalCode,
    carrier: ledger?.carrier ?? null,
    trackingNumber: row.lines.map((item) => item.tracking).find((tracking) => tracking) ?? null,
    rawJson: JSON.stringify({ status: row.status, subStatus: row.subStatus }),
  };

  return { order, issues };
}
