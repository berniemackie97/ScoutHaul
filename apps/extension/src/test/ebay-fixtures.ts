/**
 * Synthetic eBay pages with the same structure as the real ones: the JSON each page embeds, in the
 * places the site puts it, with made-up values. No real buyer ever appears in a test.
 */

const text = (value: string) => ({ textSpans: [{ text: value }] });

export interface FixtureOrder {
  orderId: string;
  created: [string, string];
  soldFor: string;
  shipping?: string;
  total: string;
  lines: { listingId: string; title: string; quantity?: number; price: string; tracking?: string }[];
}

export function orderMember(order: FixtureOrder) {
  return {
    orderId: order.orderId,
    orderNumber: order.orderId,
    creationDate: { textSpans: order.created.map((part) => ({ text: part, styles: ["DEFAULT"] })) },
    paidDate: text(order.created[0]),
    orderStatus: text("Shipped"),
    totalQuantity: text("1"),
    displayTotalPrice: text(order.total),
    postalCode: { label: text("Zip"), value: text("97477") },
    __sh: {
      soldFor: text(order.soldFor),
      shippingCost: order.shipping ? { textSpans: [{ text: order.shipping }, { text: "shipping" }] } : undefined,
      orderSubStatus: text("Label created"),
      buyerDetails: {
        buyerid: text("test_buyer"),
        fullName: text("Test Buyer"),
        toShippingAddress: { street1: text("1 Test Street"), city: text("Springfield"), stateOrProvince: text("OR") },
      },
    },
    orderLineItems: order.lines.map((line, index) => ({
      transactionId: `1000${index}`,
      listingSummary: {
        listingId: line.listingId,
        title: text(line.title),
        quantity: text(String(line.quantity ?? 1)),
      },
      itemDetailsOverlay: {
        dataPoints: [
          { label: { text: text("Item ID") }, value: { text: text(line.listingId) } },
          { label: { text: text("Item price") }, value: { text: text(line.price) } },
        ],
      },
      __sh: { trackingId: line.tracking ? text(line.tracking) : undefined },
    })),
  };
}

export function ordersPage(orders: FixtureOrder[], total = orders.length): string {
  const data = {
    ordersData: {
      _shAwait: true,
      _initialValue: {
        orders: { __type: "Grid", meta: { name: "orders" }, members: orders.map(orderMember) },
        pagination: { summary: text(`Results: 1-${orders.length} of ${total}`) },
      },
    },
  };
  return `<!doctype html><html><head><title>Orders — eBay Seller Hub</title></head><body>
<table><tr class="order-info"><td>rendered table the adapter ignores {"not":"data"}</td></tr></table>
<script>function $affeshorder(d,a){}</script>
<script>$affeshorder(${JSON.stringify(data)},{"l":["w",1]})</script></body></html>`;
}

export interface FixtureTransaction {
  type: "ORDER" | "SHIPPING_LABEL" | "OTHER_FEE" | "REFUND" | "PAYOUT";
  amount?: string;
  net: string;
  description: string[];
}

export function ledgerPage(transactions: FixtureTransaction[]): string {
  const state = {
    title: "Seller Hub | All transactions",
    modules: {
      transactionsModule: {
        transactions: transactions.map((transaction) => ({
          type: transaction.type,
          amount: text(transaction.amount ?? ""),
          net: text(transaction.net),
          description: transaction.description.map(text),
        })),
      },
    },
  };
  return `<html><body><script>window.__APP_INITIAL_STATE__ = ${JSON.stringify(state)};</script></body></html>`;
}

export interface FixtureListing {
  id: string;
  title: string;
  price: number;
  quantity?: number;
  format?: "FIXED_PRICE" | "AUCTION";
  sku?: string;
}

export function listingsPage(listings: FixtureListing[], total = listings.length): string {
  const members = listings.map((listing) => ({
    entityId: listing.id,
    marketplaceId: "0",
    title: {
      texts: [text(listing.title)],
      image: { _type: "Image", title: listing.title, URL: `https://i.ebayimg.com/images/${listing.id}.jpg` },
    },
    listingSKU: { content: listing.sku ? [text(listing.sku)] : [] },
    price: { content: { currencySymbol: "$", currentValue: listing.price.toFixed(2) } },
    additionalInfo: {
      format: listing.format ?? "FIXED_PRICE",
      currentPrice: listing.price,
      listingStartDate: "2026-06-26T04:11:52Z",
      availableQuantity: listing.quantity ?? 1,
      marketplace: "EBAY_US",
    },
  }));
  const header = { pageHeader: { model: { __type: "PageHeaderModule", title: text("Manage active listings"), subTitle: text(`(${total})`) } } };
  const grid = { gridData: { model: { __type: "ListingGrid", meta: { name: "gridData" }, members } } };
  return `<html><body><script>$feshlist_C=(window.$feshlist_C||[]).concat({"w":[["s0",12,${JSON.stringify(header)}],["s1",12,${JSON.stringify(grid)}]]})</script></body></html>`;
}

export function profileJson(username: string | null): string {
  return JSON.stringify({
    content: {
      response: {
        modules: {
          USER_PROFILE: username === null ? { _type: "UserProfile" } : { _type: "UserProfile", userLoginName: text(username), fullName: text("Seller Name") },
        },
      },
    },
  });
}
