import type { VintedWardrobe, VintedWardrobeItem } from "../adapters/vinted/listings";
import type { VintedOrderList, VintedOrderRow, VintedTransaction } from "../adapters/vinted/orders";

/**
 * Synthetic Vinted API responses with the same shape as the real ones, and made-up values. No real
 * buyer ever appears in a test.
 */

const userStatus = { open: "needs_action", completed: "completed", cancelled: "failed" } as const;
const statusText = { open: "Payment successful!", completed: "Order completed successfully.", cancelled: "Canceled. Refund processed." } as const;

export function orderRow(id: number, date: string, price: string, state: keyof typeof userStatus = "completed"): VintedOrderRow {
  return {
    transaction_id: id,
    date,
    price: { amount: price, currency_code: "USD" },
    status: statusText[state],
    title: `Test item ${id}`,
    transaction_user_status: userStatus[state],
  };
}

export function orderList(rows: VintedOrderRow[], page: number, totalPages: number): VintedOrderList {
  return { my_orders: rows, pagination: { current_page: page, total_pages: totalPages, total_entries: rows.length } };
}

export interface FixtureSale {
  price: string;
  items?: { id: number; title: string; listed: string }[];
  paidAt?: string;
  business?: boolean;
  ownShipping?: string;
}

export function transaction(id: number, sale: FixtureSale): VintedTransaction {
  const items = sale.items ?? [{ id: id * 10, title: `Test item ${id}`, listed: sale.price }];
  return {
    transaction: {
      id,
      item_id: items[0]!.id,
      title: items[0]!.title,
      debit_processed_at: sale.paidAt ?? "2026-09-15T03:45:17-06:00",
      buyer: { login: "test_buyer" },
      seller: { business: sale.business ?? false },
      offer: { price: { amount: sale.price, currency_code: "USD" } },
      service_fee: { amount: "1.13", currency_code: "USD" },
      shipment: { status_title: "Package delivered." },
      order: {
        custom_shipment: sale.ownShipping !== undefined,
        custom_shipment_price: sale.ownShipping ?? null,
        items: items.map((item) => ({ id: item.id, title: item.title, price: { amount: item.listed, currency_code: "USD" } })),
      },
    },
  };
}

export function wardrobeItem(id: number, overrides: Partial<VintedWardrobeItem> = {}): VintedWardrobeItem {
  return {
    id,
    title: `Listing ${id}`,
    price: { amount: "15.0", currency_code: "USD" },
    is_draft: false,
    is_closed: false,
    is_hidden: false,
    is_reserved: false,
    item_closing_action: null,
    url: `https://www.vinted.com/items/${id}-listing`,
    status: "Very good",
    photos: [{ url: `https://images1.vinted.net/t/${id}-a/f800/1.webp`, is_main: false }, { url: `https://images1.vinted.net/t/${id}-b/f800/1.webp`, is_main: true }],
    ...overrides,
  };
}

export function wardrobe(items: VintedWardrobeItem[], page: number, totalPages: number, totalEntries: number): VintedWardrobe {
  return { items, pagination: { current_page: page, total_pages: totalPages, total_entries: totalEntries } };
}

/** A Vinted page as the signed-in or signed-out visitor loads it — the flag its embedded data carries. */
export function vintedPage(signedIn: boolean): string {
  return `<html><body><script>self.__next_f.push([1,"{\\"isLoggedIn\\":${signedIn}}"])</script></body></html>`;
}
