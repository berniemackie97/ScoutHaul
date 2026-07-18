import { STORE } from "./store";

export type CartLine = {
  storeListingId: string;
  slug: string;
  title: string;
  unitPrice: number;
  imageUrl?: string | null;
  isAvailable: boolean;
};

export type Cart = {
  cartToken: string;
  lines: CartLine[];
  subtotal: number;
  estimatedShippingAmount?: number;
  shippingSummary?: string | null;
  freeShippingThreshold?: number | null;
  qualifiesForFreeShipping: boolean;
};

export type CheckoutResult = {
  checkoutUrl: string;
};

export type OrderSummaryLine = {
  title: string;
  unitPrice: number;
  imageUrl?: string | null;
};

export type OrderSummary = {
  orderNumber: string;
  status: string;
  subtotal: number;
  shippingTotal: number;
  taxTotal: number;
  grandTotal: number;
  isLocalPickup: boolean;
  email?: string | null;
  lines: OrderSummaryLine[];
};

export type OfferResult = {
  id: string;
  status: string;
  message: string;
};

export function imageUrl(url?: string | null) {
  if (!url) return undefined;
  return url.startsWith("http") ? url : `${STORE.apiBaseUrl}${url}`;
}

export function money(value: number | string | null | undefined) {
  const amount = Number(value ?? 0);
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(amount);
}
