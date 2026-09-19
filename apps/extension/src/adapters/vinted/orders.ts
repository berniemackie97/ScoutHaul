import type { IngestOrder, IngestOrderLine, RunIssue } from "../../agent/types";
import { cents } from "../amounts";

/** Money as Vinted's API writes it: `{ amount: "8.5", currency_code: "USD" }`. */
export interface VintedMoney {
  amount?: string | number | null;
  currency_code?: string | null;
}

/** One sale in the seller's order list (`/api/v2/my_orders?type=sold`). */
export interface VintedOrderRow {
  transaction_id?: number | string;
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

/** The parts of a transaction (`/api/v2/transactions/{id}`) a sale is recorded from. */
export interface VintedTransaction {
  transaction?: {
    id?: number | string;
    item_id?: number | string | null;
    item_title?: string | null;
    title?: string | null;
    debit_processed_at?: string | null;
    buyer?: { login?: string | null } | null;
    seller?: { business?: boolean | null } | null;
    offer?: { price?: VintedMoney | null } | null;
    service_fee?: VintedMoney | null;
    shipment?: { status_title?: string | null } | null;
    order?: {
      custom_shipment?: boolean | null;
      custom_shipment_price?: VintedMoney | string | number | null;
      items?: { id?: number | string; title?: string | null; price?: VintedMoney | null }[] | null;
    } | null;
  };
}

/** Where a sale stands: under way, done and paid out, or called off. */
export type SaleState = "open" | "completed" | "cancelled";

/** `transaction_user_status` from the seller's side: `completed`; `failed` for a cancelled or refunded sale; otherwise still under way. */
export function saleState(row: VintedOrderRow): SaleState {
  switch (row.transaction_user_status) {
    case "completed":
      return "completed";
    case "failed":
      return "cancelled";
    default:
      return "open";
  }
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
 * Splits what the buyer paid for a bundle across its items, in proportion to their listed prices —
 * evenly when those are unknown — to the cent, the last item taking the rounding.
 */
export function allocate(total: number, listed: (number | null)[]): number[] {
  if (listed.length === 0) {
    return [];
  }

  const weights = listed.map((price) => (price !== null && price > 0 ? price : 0));
  const sum = weights.reduce((running, weight) => running + weight, 0);
  const shares = weights.map((weight) => cents(sum > 0 ? (total * weight) / sum : total / listed.length));
  shares[shares.length - 1] = cents(total - shares.slice(0, -1).reduce((running, share) => running + share, 0));
  return shares;
}

/**
 * One Vinted sale, as HaulScout records it. On Vinted the buyer pays the fee — Buyer Protection —
 * and the shipping, on a label Vinted prepays; an individual seller pays neither and receives the
 * price agreed. So the fees are none, the label costs the seller nothing, and the payout is that
 * price once the sale completes — before then it is not the seller's yet. A seller's own shipping,
 * where Vinted allows it, is what the buyer paid for it, and costs what the seller paid, unknown here.
 */
export function toIngestOrder(row: VintedOrderRow, detail: VintedTransaction): { order: IngestOrder | null; issues: RunIssue[] } {
  const id = String(row.transaction_id);
  const reject = (message: string) => ({ order: null, issues: [{ message, externalReference: id, rejected: true }] });
  const transaction = detail.transaction;
  if (!transaction) {
    return reject("Vinted did not return this sale's details.");
  }

  const price = amount(transaction.offer?.price) ?? amount(row.price);
  if (price === null) {
    return reject("The sale price could not be read.");
  }

  const paidAt = Date.parse(transaction.debit_processed_at ?? "");
  const orderedAt = Number.isFinite(paidAt) ? paidAt : Date.parse(row.date ?? "");
  if (!Number.isFinite(orderedAt)) {
    return reject(`The sale time "${row.date ?? ""}" could not be read.`);
  }

  const items = transaction.order?.items?.length
    ? transaction.order.items.map((item) => ({ id: item.id, title: item.title, listed: amount(item.price) }))
    : [{ id: transaction.item_id, title: transaction.item_title ?? transaction.title, listed: null }];
  const shares = allocate(price, items.map((item) => item.listed));
  const lines: IngestOrderLine[] = items.map((item, index) => ({
    title: item.title?.trim() || row.title?.trim() || "Untitled Vinted item",
    unitPrice: shares[index]!,
    quantity: 1,
    externalItemId: item.id === null || item.id === undefined ? null : String(item.id),
  }));

  const issues: RunIssue[] = [];
  if (transaction.seller?.business) {
    issues.push({
      message: "This Vinted account sells as a business. Vinted's business fees are not read, so this sale's fees are recorded as none.",
      externalReference: id,
      rejected: false,
    });
  }

  const ownShipping = transaction.order?.custom_shipment === true;
  const shipping = ownShipping ? amount(transaction.order?.custom_shipment_price) ?? 0 : 0;
  const completed = saleState(row) === "completed";
  return {
    order: {
      externalOrderId: id,
      orderedAt: new Date(orderedAt).toISOString(),
      paidAt: Number.isFinite(paidAt) ? new Date(paidAt).toISOString() : null,
      itemsSubtotal: price,
      shippingCharged: shipping,
      // Vinted collects and remits US sales tax; none of it passes through the seller.
      taxCollected: 0,
      taxRemittedByPlatform: true,
      feeTotal: 0,
      shippingCost: ownShipping ? null : 0,
      payoutAmount: completed ? cents(price + shipping) : null,
      feeBreakdownJson: JSON.stringify({ source: "Vinted transaction", paidByBuyer: { buyerProtection: amount(transaction.service_fee) } }),
      currency: (row.price?.currency_code ?? "USD").toUpperCase(),
      platform: "Vinted",
      lines,
      buyerReference: transaction.buyer?.login ?? null,
      rawJson: JSON.stringify({ status: row.status ?? null, sellerStatus: row.transaction_user_status ?? null, shipment: transaction.shipment?.status_title || null }),
    },
    issues,
  };
}
