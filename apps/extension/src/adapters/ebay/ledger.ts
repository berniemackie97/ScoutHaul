import { dig, jsonAfter, spans } from "../embedded-json";
import { money, orderNumber } from "./text";

/** What the seller's transaction ledger says about one order's money after the sale. */
export interface LedgerOrder {
  /** Items and shipping, before fees — null until the sale reaches the ledger. */
  gross: number | null;
  /** What the seller keeps from the sale, after eBay's transaction fees. */
  net: number | null;
  /** Shipping labels bought for the order, as positive costs. */
  labels: number[];
  /** Promoted Listings fees charged for the order, as positive costs. */
  adFees: number[];
  /** Refunds sent to the buyer, as positive amounts. */
  refunds: number[];
  carrier: string | null;
}

/**
 * Reads the seller's transaction ledger (`/mes/transactionlist`), which the page embeds as
 * `window.__APP_INITIAL_STATE__`. Keyed by order number. Null when the page carries no ledger —
 * the page changed.
 */
export function readLedgerPage(html: string): { byOrder: Map<string, LedgerOrder>; count: number } | null {
  const state = jsonAfter(html, "window.__APP_INITIAL_STATE__");
  if (state === undefined) {
    return null;
  }

  const transactions = dig(state, "modules", "transactionsModule", "transactions");
  // No transactions in the period is an empty module, not a missing one.
  const list = Array.isArray(transactions) ? transactions : dig(state, "modules") !== undefined ? [] : null;
  if (list === null) {
    return null;
  }

  const byOrder = new Map<string, LedgerOrder>();
  const entry = (id: string) => {
    let found = byOrder.get(id);
    if (!found) {
      found = { gross: null, net: null, labels: [], adFees: [], refunds: [], carrier: null };
      byOrder.set(id, found);
    }

    return found;
  };

  for (const transaction of list) {
    const type = dig(transaction, "type");
    const description = (dig(transaction, "description") as unknown[] | undefined)?.map((part) => spans(part) ?? "") ?? [];
    const id = orderNumber(description.join(" "));
    if (!id) {
      continue;
    }

    const amount = money(spans(dig(transaction, "amount")));
    const net = money(spans(dig(transaction, "net")));
    switch (type) {
      case "ORDER":
        entry(id).gross = amount;
        entry(id).net = net;
        break;
      case "SHIPPING_LABEL":
        if (amount !== null) {
          entry(id).labels.push(Math.abs(amount));
          entry(id).carrier = description[2]?.trim() || entry(id).carrier;
        }
        break;
      case "OTHER_FEE":
        if (/promoted listings/i.test(description.join(" ")) && net !== null) {
          entry(id).adFees.push(Math.abs(net));
        }
        break;
      case "REFUND":
        if (amount !== null) {
          entry(id).refunds.push(Math.abs(amount));
        }
        break;
    }
  }

  return { byOrder, count: list.length };
}
