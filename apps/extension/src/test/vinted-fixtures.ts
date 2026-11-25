import type { VintedWardrobe, VintedWardrobeItem } from "../adapters/vinted/listings";
import type { VintedEscrowReceipt, VintedOrderList, VintedOrderRow } from "../adapters/vinted/orders";

/**
 * Synthetic Vinted API responses with the same shape as the real ones, and made-up values. No real
 * buyer ever appears in a test.
 */

const userStatus = { open: "needs_action", completed: "completed", cancelled: "failed" } as const;
const statusText = { open: "Payment successful!", completed: "Order completed successfully.", cancelled: "Canceled. Refund processed." } as const;

export function orderRow(id: number, date: string, price: string, state: keyof typeof userStatus = "completed"): VintedOrderRow {
  return {
    transaction_id: id,
    escrow_order_id: id,
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
  items?: { id: number; title: string }[];
  createdAt?: string;
  business?: boolean;
}

export function escrowReceipt(id: number, sale: FixtureSale): VintedEscrowReceipt {
  const items = sale.items ?? [{ id: id * 10, title: `Test item ${id}` }];
  return {
    escrow_order: {
      id,
      current_user_side: "seller",
      seller_id: 5550001,
      created_at: sale.createdAt ?? "2026-09-15T03:45:17-06:00",
      buyer: { login: "test_buyer" },
      is_seller_business: sale.business ?? false,
      items: items.map((item) => ({ item_id: item.id, title: item.title })),
      payment_data: {
        items_price: { price: { amount: sale.price, currency_code: "USD" } },
        service_fee_price: { price: { amount: "1.13", currency_code: "USD" } },
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
    photos: [
      { url: `https://images1.vinted.net/t/${id}-a/f800/1.webp`, full_size_url: `https://images1.vinted.net/tc/${id}-a/1.webp`, is_main: false },
      { url: `https://images1.vinted.net/t/${id}-b/f800/1.webp`, full_size_url: `https://images1.vinted.net/tc/${id}-b/1.webp`, is_main: true },
    ],
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
