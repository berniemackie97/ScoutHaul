import type { FulfillmentStage, IngestOrder, IngestOrderLine, RunIssue } from "../../agent/types";
import { allocate, cents } from "../amounts";

/** Money as Vinted's API writes it: `{ amount: "8.5", currency_code: "USD" }`. */
export interface VintedMoney {
  amount?: string | number | null;
  currency_code?: string | null;
}

/** One sale in the seller's escrow-orders list. */
export interface VintedOrderRow {
  transaction_id?: number | string;
  escrow_order_id?: number | string;
  date?: string;
  price?: VintedMoney;
  status?: string;
  title?: string;
  transaction_user_status?: string;
}

export interface VintedOrderList {
  my_orders?: VintedOrderRow[];
  pagination?: { current_page?: number; total_pages?: number; total_entries?: number };
}

/** The fields read from the seller's escrow receipt. */
export interface VintedEscrowReceipt {
  escrow_order?: {
    id?: number | string;
    current_user_side?: string;
    seller_id?: number | string;
    is_seller_business?: boolean | null;
    created_at?: string | null;
    buyer?: { login?: string | null } | null;
    items?: { item_id?: number | string | null; title?: string | null }[] | null;
    payment_data?: {
      items_price?: { price?: VintedMoney | null } | null;
      service_fee_price?: { price?: VintedMoney | null } | null;
    } | null;
  };
}

/**
 * How far Vinted says a parcel has got, from the shipment's own words ("Package delivered.") and, failing that,
 * the order's ("Payment successful!" — paid for, not yet sent). A phrase this does not know is reported as nothing
 * rather than guessed at.
 */
export function fulfillment(row: VintedOrderRow, shipment: string | null): { stage: FulfillmentStage; detail: string | null } {
  const detail = shipment ?? row.status ?? null;
  const words = `${shipment ?? ""} ${row.status ?? ""}`;

  if (/delivered|buyer accepted/i.test(words)) {
    return { stage: "Delivered", detail };
  }

  if (/on its way|in transit|shipped|sent|handed over|collected/i.test(words)) {
    return { stage: "Shipped", detail };
  }

  if (/label|ready to ship|print/i.test(words)) {
    return { stage: "LabelCreated", detail };
  }

  if (/payment successful|waiting for the seller|to be shipped|awaiting/i.test(words)) {
    return { stage: "NeedsShipping", detail };
  }

  return { stage: "Unknown", detail };
}

/** Where a sale stands: under way, done and paid out, or called off. */
export type SaleState = "open" | "completed" | "cancelled";

/** Vinted also shows terminal outcomes in the order's display status. */
export function saleState(row: VintedOrderRow): SaleState {
  const status = row.status?.trim() ?? "";
  if (row.transaction_user_status === "failed"
    || /^(?:(?:order )?cancell?ed|(?:payment (?:was )?)?(?:fully )?refunded|refund processed)(?:[.!]|$)/i.test(status)) {
    return "cancelled";
  }

  return row.transaction_user_status === "completed" ? "completed" : "open";
}

/** Everything about a sale in the list that, when it changes, means the sale must be read again. */
export function saleStamp(row: VintedOrderRow): string {
  return [row.date, row.transaction_user_status, row.status, row.price?.amount].join("|");
}

export function amount(value: VintedMoney | string | number | null | undefined): number | null {
  const raw = typeof value === "object" && value !== null ? value.amount : value;
  const number = typeof raw === "number" ? raw : typeof raw === "string" && raw.trim() !== "" ? Number(raw) : Number.NaN;
  return Number.isFinite(number) && number >= 0 ? cents(number) : null;
}


/**
 * One Vinted sale, as HaulScout records it. On Vinted the buyer pays the fee — Buyer Protection —
 * and the shipping, on a label Vinted prepays; an individual seller pays neither and receives the
 * price agreed. The receipt names the items and sale price. For a US consumer sale, Vinted
 * collects shipping and Buyer Protection from the buyer.
 */
export function toIngestOrder(row: VintedOrderRow, detail: VintedEscrowReceipt, sellerId: string | null): { order: IngestOrder | null; issues: RunIssue[] } {
  const id = String(row.transaction_id);
  const reject = (message: string) => ({ order: null, issues: [{ message, externalReference: id, rejected: true }] });
  const receipt = detail.escrow_order;
  if (!receipt || String(receipt.id) !== String(row.escrow_order_id ?? row.transaction_id)
    || receipt.current_user_side !== "seller" || (sellerId !== null && String(receipt.seller_id) !== sellerId)) {
    return reject("Vinted did not return the matching seller receipt.");
  }

  const itemPrice = receipt.payment_data?.items_price?.price;
  const price = amount(itemPrice) ?? amount(row.price);
  if (price === null) {
    return reject("The sale price could not be read.");
  }

  const orderedAt = Date.parse(receipt.created_at ?? "");
  if (!Number.isFinite(orderedAt)) {
    return reject("The receipt's order time could not be read.");
  }

  const items = receipt.items;
  if (!items?.length) {
    return reject("The receipt did not list any items.");
  }

  const shares = allocate(price, items.map(() => null));
  const lines: IngestOrderLine[] = items.map((item, index) => ({
    title: item.title?.trim() || row.title?.trim() || "Untitled Vinted item",
    unitPrice: shares[index]!,
    quantity: 1,
    externalItemId: item.item_id === null || item.item_id === undefined ? null : String(item.item_id),
  }));

  const issues: RunIssue[] = [];
  const business = receipt.is_seller_business === true;
  if (business) {
    issues.push({
      message: "This Vinted account sells as a business. Its seller fees and payout need review.",
      externalReference: id,
      rejected: false,
    });
  }

  const completed = saleState(row) === "completed";
  const stage = fulfillment(row, null);
  return {
    order: {
      externalOrderId: id,
      orderedAt: new Date(orderedAt).toISOString(),
      paidAt: null,
      itemsSubtotal: price,
      shippingCharged: 0,
      // Vinted collects and remits US sales tax; none of it passes through the seller.
      taxCollected: 0,
      taxRemittedByPlatform: true,
      feeTotal: business ? null : 0,
      feesPending: business,
      shippingCost: business ? null : 0,
      payoutAmount: completed && !business ? price : null,
      feeBreakdownJson: JSON.stringify({ source: "Vinted escrow order", paidByBuyer: { buyerProtection: amount(receipt.payment_data?.service_fee_price?.price) } }),
      currency: (itemPrice?.currency_code ?? row.price?.currency_code ?? "USD").toUpperCase(),
      platform: "Vinted",
      lines,
      buyerReference: receipt.buyer?.login ?? null,
      fulfillmentStage: stage.stage,
      fulfillmentDetail: stage.detail,
      rawJson: JSON.stringify({ status: row.status ?? null, sellerStatus: row.transaction_user_status ?? null }),
    },
    issues,
  };
}
