import { describe, expect, it } from "vitest";
import { ledgerPage, listingsPage, ordersPage, profileJson, type FixtureOrder } from "../../test/ebay-fixtures";
import { AdapterError, type MarketplaceContext, type PageResponse } from "../types";
import { ebayAdapter, salesFrom } from ".";
import { readLedgerPage } from "./ledger";
import { readOrdersPage, toIngestOrder } from "./orders";

const now = new Date("2026-09-18T20:00:00Z");

const sold: FixtureOrder = {
  orderId: "26-15152-72360",
  created: ["Sep 18", "12:48am PDT"],
  soldFor: "$20.00",
  shipping: "+$6.99",
  total: "$28.95",
  lines: [{ listingId: "377273002938", title: "Sports Game Lot of 4", price: "$20.00", tracking: "9400100000000000000000" }],
};

const earlier: FixtureOrder = {
  orderId: "07-15174-93677",
  created: ["Sep 15", "1:35am PDT"],
  soldFor: "$4.50",
  shipping: "+$5.48",
  total: "$10.30",
  lines: [{ listingId: "377251512872", title: "Psyduck Reverse Holo", price: "$4.50" }],
};

const ledger = ledgerPage([
  { type: "SHIPPING_LABEL", amount: "-$6.99", net: "-$6.99", description: ["Shipping label for order no. 26-15152-72360", "Tracking no. 9400", "USPS"] },
  { type: "ORDER", amount: "$26.99", net: "$22.91", description: ["Order 26-15152-72360", "Sports Game Lot of 4"] },
  { type: "OTHER_FEE", net: "-$0.52", description: ["Promoted Listings - General fee for order 26-15152-72360", "Item 377273002938"] },
  { type: "PAYOUT", net: "-$2.83", description: ["Payout 123", "$2.83 to bank account ending in 0000"] },
]);

function context(routes: Record<string, string | PageResponse>): MarketplaceContext & { requested: string[] } {
  const requested: string[] = [];
  return {
    requested,
    now: () => now,
    async get(url: string) {
      requested.push(url);
      const path = new URL(url).pathname;
      const route = routes[path];
      if (route === undefined) {
        throw new Error(`No fixture for ${url}`);
      }

      return typeof route === "string" ? { status: 200, url, contentType: "text/html", body: route } : route;
    },
  };
}

describe("reading eBay's order list", () => {
  it("reads every order from the page's embedded grid, not its table", () => {
    const page = readOrdersPage(ordersPage([sold, earlier], 36));
    expect(page?.total).toBe(36);
    expect(page?.rows.map((row) => row.orderId)).toEqual(["26-15152-72360", "07-15174-93677"]);
    expect(page?.rows[0]).toMatchObject({ created: "Sep 18 | 12:48am PDT", soldFor: 20, shipping: 6.99, total: 28.95, buyerUsername: "test_buyer" });
    expect(page?.rows[0]?.lines[0]).toEqual({ listingId: "377273002938", title: "Sports Game Lot of 4", quantity: 1, unitPrice: 20, tracking: "9400100000000000000000" });
  });

  it("says the page changed when its data is gone", () => {
    expect(readOrdersPage("<html><table></table></html>")).toBeNull();
  });
});

describe("turning an order into the landing shape", () => {
  const row = readOrdersPage(ordersPage([sold]))!.rows[0]!;
  const byOrder = readLedgerPage(ledger)!.byOrder;

  it("takes the money after the sale from the ledger: fees, the label, the ad fee and the payout", () => {
    const { order, issues } = toIngestOrder(row, byOrder.get(sold.orderId), now);
    expect(issues).toEqual([]);
    expect(order).toMatchObject({
      externalOrderId: "26-15152-72360",
      orderedAt: "2026-09-18T07:48:00.000Z",
      itemsSubtotal: 20,
      shippingCharged: 6.99,
      taxCollected: 1.96,
      feeTotal: 4.6,
      feesPending: false,
      shippingCost: 6.99,
      payoutAmount: 15.4,
      carrier: "USPS",
      trackingNumber: "9400100000000000000000",
    });
    expect(JSON.parse(order!.feeBreakdownJson!)).toEqual({ source: "eBay transaction ledger", transactionFees: 4.08, adFees: 0.52 });
  });

  it("marks the fees pending, never nought, until the sale reaches the ledger", () => {
    const { order } = toIngestOrder(row, undefined, now);
    expect(order).toMatchObject({ feeTotal: null, feesPending: true, payoutAmount: null, shippingCost: null });
  });

  it("rejects an order whose sale time it cannot read, naming the order", () => {
    const unreadable = { ...row, created: "Sep 18" };
    expect(toIngestOrder(unreadable, undefined, now)).toEqual({
      issues: [{ message: 'The sale time "Sep 18" could not be read.', externalReference: "26-15152-72360", rejected: true }],
    });
  });
});

describe("the eBay adapter", () => {
  it("scans the routine window of sales with their fees", async () => {
    const eBay = context({ "/sh/ord/": ordersPage([sold, earlier]), "/mes/transactionlist": ledger });

    const scan = await ebayAdapter.scanSales(eBay, { from: null, cursor: null });

    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["26-15152-72360", "07-15174-93677"]);
    expect(scan.orders[1]).toMatchObject({ feesPending: true });
    expect(scan.cursor).toBe("2026-09-18T07:48:00.000Z");
    expect(scan.coversFrom).toBe("2026-09-04T20:00:00.000Z");
    expect(eBay.requested).toHaveLength(2);
    expect(eBay.requested[1]).toContain(encodeURIComponent("creationDate:(2026-09-03..2026-09-19)"));
  });

  it("reaches back to where the last scan stopped after days with every browser off", () => {
    expect(salesFrom({ from: null, cursor: "2026-08-20T00:00:00Z" }, now).toISOString()).toBe("2026-08-19T00:00:00.000Z");
    expect(salesFrom({ from: new Date("2026-01-01T00:00:00Z"), cursor: null }, now).toISOString()).toBe("2026-06-20T20:00:00.000Z");
  });

  it("leaves out sales older than the window", async () => {
    const eBay = context({ "/sh/ord/": ordersPage([sold, earlier]), "/mes/transactionlist": ledger });
    const scan = await ebayAdapter.scanSales(eBay, { from: new Date("2026-09-17T00:00:00Z"), cursor: null });
    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["26-15152-72360"]);
  });

  it("snapshots every live listing, and says when the snapshot is complete", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([
        { id: "377296454991", title: "Gucci Oxford Shirt", price: 249.99, sku: "649706ZAE47" },
        { id: "377296454992", title: "Coin lot", price: 12, format: "AUCTION", quantity: 3 },
      ]),
    });

    const snapshot = await ebayAdapter.snapshotListings(eBay);

    expect(snapshot.complete).toBe(true);
    expect(snapshot.listings[0]).toMatchObject({
      externalListingId: "377296454991",
      title: "Gucci Oxford Shirt",
      price: 249.99,
      quantity: 1,
      status: "Active",
      format: "FixedPrice",
      sku: "649706ZAE47",
      url: "https://www.ebay.com/itm/377296454991",
      listedAt: "2026-06-26T04:11:52.000Z",
    });
    expect(snapshot.listings[1]).toMatchObject({ format: "Auction", quantity: 3 });
  });

  it("never counts a listing twice when eBay repeats a page", async () => {
    const full = Array.from({ length: 200 }, (_, index) => ({ id: String(377100000000 + index), title: `Item ${index}`, price: 5 }));
    const eBay = context({ "/sh/lst/active": listingsPage(full, 400) });

    const snapshot = await ebayAdapter.snapshotListings(eBay);

    expect(snapshot.listings).toHaveLength(200);
    expect(snapshot.complete).toBe(false);
    expect(eBay.requested).toHaveLength(2);
  });

  it("does not call a partial snapshot complete", async () => {
    const eBay = context({ "/sh/lst/active": listingsPage([{ id: "1", title: "One", price: 5 }], 130) });
    expect((await ebayAdapter.snapshotListings(eBay)).complete).toBe(false);
  });

  it("knows who the browser is signed in as, and when it is not", async () => {
    expect(await ebayAdapter.detectSession(context({ "/gh/user_profile": profileJson("overhaultradingcompany") })))
      .toEqual({ signedIn: true, username: "overhaultradingcompany", marketplaceUserId: null });
    expect((await ebayAdapter.detectSession(context({ "/gh/user_profile": profileJson(null) }))).signedIn).toBe(false);
    const signedOut = { status: 200, url: "https://signin.ebay.com/ws/eBayISAPI.dll", contentType: "text/html", body: "<html>" };
    expect((await ebayAdapter.detectSession(context({ "/gh/user_profile": signedOut }))).signedIn).toBe(false);
  });

  it("tells a security check, a throttle and a changed page apart", async () => {
    const challenge = { status: 200, url: "https://www.ebay.com/splashui/challenge?ap=1", contentType: "text/html", body: "" };
    const throttled = { status: 429, url: "https://www.ebay.com/sh/ord/", contentType: "text/html", body: "" };
    const kind = async (route: string | PageResponse) =>
      ebayAdapter.scanSales(context({ "/sh/ord/": route }), { from: null, cursor: null }).then(
        () => "none",
        (error: unknown) => (error instanceof AdapterError ? error.kind : "unexpected"),
      );

    expect(await kind(challenge)).toBe("Challenge");
    expect(await kind(throttled)).toBe("RateLimited");
    expect(await kind("<html>a redesigned page</html>")).toBe("SiteChanged");
  });
});
