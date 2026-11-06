import { describe, expect, it } from "vitest";
import { itemPage, ledgerPage, listingsPage, ordersPage, profileJson, type FixtureOrder } from "../../test/ebay-fixtures";
import { memoryInMap } from "../../test/memory";
import { AdapterError, type MarketplaceContext, type PageResponse, type Session } from "../types";
import { ebayAdapter, salesFrom } from ".";
import { readLedgerPage } from "./ledger";
import { readOrdersPage, toIngestOrder } from "./orders";

const now = new Date("2026-09-18T20:00:00Z");
const seller: Session = { signedIn: true, username: "overhaultradingcompany", marketplaceUserId: null };

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

function context(routes: Record<string, string | PageResponse>): MarketplaceContext & { requested: string[]; opened: string[] } {
  const requested: string[] = [];
  const opened: string[] = [];
  const serve = (url: string) => {
    const path = new URL(url).pathname;
    const item = /^\/itm\/(\d+)$/.exec(path);
    const route = routes[path] ?? (item ? itemPage([`${item[1]}a`, `${item[1]}b`]) : undefined);
    if (route === undefined) {
      throw new Error(`No fixture for ${url}`);
    }

    return Promise.resolve(typeof route === "string" ? { status: 200, url, contentType: "text/html", body: route } : route);
  };
  return {
    requested,
    opened,
    now: () => now,
    memory: memoryInMap(),
    page(url: string) {
      opened.push(url);
      requested.push(url);
      return serve(url);
    },
    request(url: string) {
      requested.push(url);
      return serve(url);
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

    const scan = await ebayAdapter.scanSales(eBay, { from: null, cursor: null }, seller);

    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["26-15152-72360", "07-15174-93677"]);
    expect(scan.orders[1]).toMatchObject({ feesPending: true });
    expect(scan.cursor).toBe("2026-09-18T07:48:00.000Z");
    expect(scan.coversFrom).toBe("2026-09-04T20:00:00.000Z");
    expect(eBay.requested).toHaveLength(2);
    expect(eBay.requested[1]).toContain(encodeURIComponent("creationDate:(2026-09-03..2026-09-19)"));
    // Seller pages are opened the way a person would open them, never fetched from script.
    expect(eBay.opened).toEqual(eBay.requested);
  });

  it("reaches back to where the last scan stopped after days with every browser off", () => {
    expect(salesFrom({ from: null, cursor: "2026-08-20T00:00:00Z" }, now).toISOString()).toBe("2026-08-19T00:00:00.000Z");
    expect(salesFrom({ from: new Date("2026-01-01T00:00:00Z"), cursor: null }, now).toISOString()).toBe("2026-06-20T20:00:00.000Z");
  });

  it("leaves out sales older than the window", async () => {
    const eBay = context({ "/sh/ord/": ordersPage([sold, earlier]), "/mes/transactionlist": ledger });
    const scan = await ebayAdapter.scanSales(eBay, { from: new Date("2026-09-17T00:00:00Z"), cursor: null }, seller);
    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["26-15152-72360"]);
  });

  it("reads an older order again when its parcel was delivered inside the window", async () => {
    const delivered: FixtureOrder = { ...earlier, status: "Delivered Sep 17", subStatus: "Delivered" };
    const stillMoving: FixtureOrder = { ...earlier, orderId: "07-15174-00001", status: "Shipped", subStatus: "Carrier scan Sep 15" };
    const eBay = context({ "/sh/ord/": ordersPage([sold, delivered, stillMoving]), "/mes/transactionlist": ledger });

    const scan = await ebayAdapter.scanSales(eBay, { from: new Date("2026-09-17T00:00:00Z"), cursor: null }, seller);

    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["26-15152-72360", "07-15174-93677"]);
    expect(scan.orders[1]).toMatchObject({ fulfillmentStage: "Delivered", deliveredAt: "2026-09-17T12:00:00.000Z" });
    // Its fees come from the ledger, so the ledger reaches back to when it was sold.
    expect(eBay.requested[1]).toContain(encodeURIComponent("creationDate:(2026-09-14..2026-09-19)"));
    // The window, and where the next scan starts, are unchanged.
    expect(scan.coversFrom).toBe("2026-09-17T00:00:00.000Z");
    expect(scan.cursor).toBe("2026-09-18T07:48:00.000Z");
  });

  it("snapshots every live listing, and says when the snapshot is complete", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([
        { id: "377296454991", title: "Gucci Oxford Shirt", price: 249.99, sku: "649706ZAE47" },
        { id: "377296454992", title: "Coin lot", price: 12, format: "AUCTION", quantity: 3 },
      ]),
    });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    expect(snapshot.complete).toBe(true);
    expect(snapshot.listings[0]).toMatchObject({
      externalListingId: "377296454991",
      title: "Gucci Oxford Shirt",
      price: 249.99,
      quantity: 1,
      status: "Active",
      format: "FixedPrice",
      sku: "649706ZAE47",
      // The grid's thumbnail address, asked for at the size worth keeping.
      primaryImageUrl: "https://i.ebayimg.com/images/g/377296454991/s-l1600.jpg",
      // The whole gallery, read from the listing's own page — the grid shows only the one picture.
      photoUrls: [
        "https://i.ebayimg.com/images/g/377296454991a/s-l1600.jpg",
        "https://i.ebayimg.com/images/g/377296454991b/s-l1600.jpg",
      ],
      url: "https://www.ebay.com/itm/377296454991",
      listedAt: "2026-06-26T04:11:52.000Z",
    });
    expect(snapshot.listings[1]).toMatchObject({ format: "Auction", quantity: 3 });
  });

  it("never counts a listing twice when eBay repeats a page", async () => {
    const full = Array.from({ length: 200 }, (_, index) => ({ id: String(377100000000 + index), title: `Item ${index}`, price: 5 }));
    const eBay = context({ "/sh/lst/active": listingsPage(full, 400) });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    expect(snapshot.listings).toHaveLength(200);
    expect(snapshot.complete).toBe(false);
    // Two grid pages, and galleries for no more listings than one scan's budget allows.
    expect(eBay.requested.filter((url) => url.includes("/sh/lst/active"))).toHaveLength(2);
    expect(eBay.requested.filter((url) => url.includes("/itm/"))).toHaveLength(25);
  });

  it("does not call a partial snapshot complete", async () => {
    const eBay = context({ "/sh/lst/active": listingsPage([{ id: "1", title: "One", price: 5 }], 130) });
    expect((await ebayAdapter.snapshotListings(eBay, seller)).complete).toBe(false);
  });


  it("leaves a listing's photos alone when its gallery was not reached this scan", async () => {
    const full = Array.from({ length: 200 }, (_, index) => ({ id: String(377100000000 + index), title: `Item ${index}`, price: 5 }));
    const eBay = context({ "/sh/lst/active": listingsPage(full, 400) });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    // Saying nothing is how a scan says "unchanged"; sending one picture would replace a whole gallery with it.
    expect(snapshot.listings.slice(0, 25).every((listing) => listing.photoUrls?.length === 2)).toBe(true);
    expect(snapshot.listings.slice(25).every((listing) => listing.photoUrls === undefined)).toBe(true);
  });

  it("reads a gallery once and remembers it while the listing's picture is unchanged", async () => {
    const eBay = context({ "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]) });

    const first = await ebayAdapter.snapshotListings(eBay, seller);
    const second = await ebayAdapter.snapshotListings(eBay, seller);

    expect(eBay.requested.filter((url) => url.includes("/itm/"))).toHaveLength(1);
    expect(second.listings[0]!.photoUrls).toEqual(first.listings[0]!.photoUrls);
  });

  it("reads the gallery again once the listing's picture changes", async () => {
    const eBay = context({ "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]) });
    await ebayAdapter.snapshotListings(eBay, seller);

    const changed = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]).replace("377296454991/s-l140.jpg", "377296454999/s-l140.jpg"),
    });
    changed.memory = eBay.memory;
    await ebayAdapter.snapshotListings(changed, seller);

    expect(changed.requested.filter((url) => url.includes("/itm/"))).toHaveLength(1);
  });

  it("reports a listing whose page carries no gallery without rejecting the listing", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]),
      "/itm/377296454991": "<html><body>no pictures here</body></html>",
    });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    expect(snapshot.listings[0]!.photoUrls).toBeUndefined();
    expect(snapshot.issues.some((issue) => !issue.rejected && issue.externalReference === "377296454991")).toBe(true);
  });


  it("does not fail a listings scan because one item page could not be read", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]),
      "/itm/377296454991": { status: 404, url: "https://www.ebay.com/itm/377296454991", contentType: "text/html", body: "" },
    });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    // The listings were read. Their pictures can wait for the next scan.
    expect(snapshot.listings).toHaveLength(1);
    expect(snapshot.listings[0]!.photoUrls).toBeUndefined();
    expect(snapshot.issues.some((issue) => !issue.rejected)).toBe(true);
  });

  it("stops the scan when an item page shows a security check", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]),
      "/itm/377296454991": { status: 200, url: "https://www.ebay.com/splashui/challenge", contentType: "text/html", body: "" },
    });

    // Asking a marketplace that is already refusing is how a browser gets itself blocked.
    await expect(ebayAdapter.snapshotListings(eBay, seller)).rejects.toMatchObject({ kind: "Challenge" });
  });


  it("brings back what the item page says the thing is, from the page it already read", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Gucci Oxford Shirt", price: 249.99 }]),
      "/itm/377296454991": itemPage(["AAA111", "BBB222"], { Brand: "Gucci", Color: "White", Size: "16" }),
    });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    expect(snapshot.listings[0]).toMatchObject({ brand: "Gucci", color: "White", size: "16" });
    // One request for the page, serving both its pictures and its specifics.
    expect(eBay.requested.filter((url) => url.includes("/itm/"))).toHaveLength(1);
  });


  it("re-reads an item page remembered before specifics were read", async () => {
    // The regression this guards: galleries remembered by an earlier version short-circuited the page read, so
    // brand, colour and size never arrived for any listing that already had its photos.
    const eBay = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]),
      "/itm/377296454991": itemPage(["AAA111"], { Brand: "Gucci" }),
    });
    await eBay.memory.set("gallery:377296454991", {
      at: now.getTime(),
      primary: "https://i.ebayimg.com/images/g/377296454991/s-l1600.jpg",
      photos: ["https://i.ebayimg.com/images/g/AAA111/s-l1600.jpg"],
    });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    expect(eBay.requested.filter((url) => url.includes("/itm/"))).toHaveLength(1);
    expect(snapshot.listings[0]!.brand).toBe("Gucci");
  });

  it("reads a listing's description from the document its page links to", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]),
      "/itm/377296454991": itemPage(["AAA111"], { Brand: "Gucci" }, "https://vi.vipr.ebaydesc.com/itmdesc?item=377296454991"),
      "/itmdesc": "<html><body><p>Worn twice. No flaws.</p></body></html>",
    });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    expect(snapshot.listings[0]!.description).toBe("Worn twice. No flaws.");
  });

  it("says nothing about a description rather than erasing one, when the page links to none", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]),
      "/itm/377296454991": itemPage(["AAA111"], { Brand: "Gucci" }),
    });

    const snapshot = await ebayAdapter.snapshotListings(eBay, seller);

    expect(snapshot.listings[0]!.description).toBeUndefined();
  });

  it("remembers what a page said, so a second scan asks for neither the page nor its description", async () => {
    const eBay = context({
      "/sh/lst/active": listingsPage([{ id: "377296454991", title: "Shirt", price: 20 }]),
      "/itm/377296454991": itemPage(["AAA111"], { Brand: "Gucci" }, "https://vi.vipr.ebaydesc.com/itmdesc?item=377296454991"),
      "/itmdesc": "<html><body><p>Worn twice. No flaws.</p></body></html>",
    });
    await ebayAdapter.snapshotListings(eBay, seller);
    const asked = eBay.requested.length;

    const second = await ebayAdapter.snapshotListings(eBay, seller);

    expect(eBay.requested.length).toBe(asked + 1); // the listings page only
    expect(second.listings[0]).toMatchObject({ brand: "Gucci", description: "Worn twice. No flaws." });
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
      ebayAdapter.scanSales(context({ "/sh/ord/": route }), { from: null, cursor: null }, seller).then(
        () => "none",
        (error: unknown) => (error instanceof AdapterError ? error.kind : "unexpected"),
      );

    expect(await kind(challenge)).toBe("Challenge");
    expect(await kind(throttled)).toBe("RateLimited");
    expect(await kind(`<html>${"a redesigned seller page ".repeat(8000)}</html>`)).toBe("SiteChanged");
    expect(await kind("<html><title>Something went wrong</title></html>")).toBe("Error");
  });
});
