import { describe, expect, it } from "vitest";
import { challengePage, homePage, productPage, receiptPage, shopPage, soldListPage, soldListShell } from "../../test/depop-fixtures";
import { memoryInMap } from "../../test/memory";
import { AdapterError, type MarketplaceContext, type PageRead, type PageResponse, type Session } from "../types";
import { depopAdapter, readShop } from ".";
import { readProduct } from "./product";
import { readReceipt, readSoldList, titleOf, toIngestOrder } from "./sales";

const now = new Date("2026-09-26T12:00:00Z");
const seller: Session = { signedIn: true, username: "overhaultrading", marketplaceUserId: null };

function depop(answer: (url: URL) => string | PageResponse | undefined, memory = memoryInMap()): MarketplaceContext & { read: string[]; waits: (PageRead | undefined)[] } {
  const read: string[] = [];
  const waits: (PageRead | undefined)[] = [];
  const serve = async (url: string, wait?: PageRead): Promise<PageResponse> => {
    read.push(url);
    waits.push(wait);
    const reply = answer(new URL(url));
    if (reply === undefined) {
      return { status: 404, url, contentType: "text/html", body: "" };
    }

    return typeof reply === "string" ? { status: 200, url, contentType: "text/html", body: reply } : reply;
  };
  return { read, waits, now: () => now, memory, page: serve, request: serve };
}

describe("a Depop listing", () => {
  it("is read whole from the page Depop publishes it on", () => {
    const listing = readProduct(
      "overhhultrading-lane-bryant-black-151e",
      productPage({
        slug: "overhhultrading-lane-bryant-black-151e",
        title: "Lane Bryant Black with Thin Vertical Stripes Button Up Blouse",
        price: "8.00",
        images: 4,
        description: "Cotton, rayon &amp; spandex blend",
      }),
    );

    expect(listing).toMatchObject({
      externalListingId: "overhhultrading-lane-bryant-black-151e",
      title: "Lane Bryant Black with Thin Vertical Stripes Button Up Blouse",
      price: 8,
      currency: "USD",
      platform: "Depop",
      status: "Active",
      brand: "Lane Bryant",
      size: "26",
      condition: "Excellent condition",
      description: "Cotton, rayon & spandex blend",
      quantity: 1,
      quantitySold: 0,
    });
    // The whole gallery, which is what the listing's own page carries and the shop page does not.
    expect(listing.photoUrls).toHaveLength(4);
    expect(listing.primaryImageUrl).toContain("P0.jpg");
  });

  it("closes a listing its own page says has sold", () => {
    const listing = readProduct("gone", productPage({ slug: "gone", title: "Sold tee", price: "15.00", sold: true }));

    expect(listing).toMatchObject({ status: "Sold", quantity: 0, quantitySold: 1 });
  });

  it("reads a listing that states no brand, size or condition", () => {
    const listing = readProduct("plain", productPage({ slug: "plain", title: "Plain tee", price: "5.00", brand: null, size: null, condition: null }));

    expect(listing).toMatchObject({ title: "Plain tee", price: 5, size: null });
  });

  it("says the site changed when a page carries no listing data", () => {
    expect(() => readProduct("x", "<html><body>nothing</body></html>")).toThrow(AdapterError);
  });

  it("names every listing a shop page shows, once each", () => {
    // The header's Sell button links under /products/ too, and is not a listing.
    expect(readShop(shopPage(["a-1", "b-2", "a-1"]))).toEqual(["a-1", "b-2"]);
    expect(readShop("<html><body>no listings</body></html>")).toEqual([]);
  });
});

describe("a Depop scan", () => {
  const shop = (slugs: string[], products: Record<string, string> = {}) => (url: URL) => {
    if (url.pathname === "/overhaultrading/") {
      return shopPage(slugs);
    }

    const slug = /^\/products\/(.+?)\/$/.exec(url.pathname)?.[1];
    return slug ? products[slug] ?? productPage({ slug, title: `Item ${slug}`, price: "10.00" }) : undefined;
  };

  it("reads the shop for what exists, then each listing for what it is", async () => {
    const context = depop(shop(["a-1", "b-2"]));

    const snapshot = await depopAdapter.snapshotListings(context, seller);

    expect(snapshot.listings.map((listing) => listing.externalListingId)).toEqual(["a-1", "b-2"]);
    expect(snapshot.listings[0]!.title).toBe("Item a-1");
    expect(context.read).toEqual([
      "https://www.depop.com/overhaultrading/",
      "https://www.depop.com/products/a-1/",
      "https://www.depop.com/products/b-2/",
    ]);
  });

  it("never calls a snapshot complete, because a shop shows its listings as you scroll", async () => {
    const snapshot = await depopAdapter.snapshotListings(depop(shop(["a-1"])), seller);

    // Claiming otherwise would end every listing further down the page than this scan could see.
    expect(snapshot.complete).toBe(false);
  });

  it("ends a listing that dropped out of the shop only when its own page says it is gone", async () => {
    const memory = memoryInMap();
    await depopAdapter.snapshotListings(depop(shop(["a-1", "b-2"]), memory), seller);

    // b-2 is no longer on the shop page, and its own page is gone too.
    const gone = depop((url) => (url.pathname === "/overhaultrading/" ? shopPage(["a-1"]) : /a-1/.test(url.pathname) ? productPage({ slug: "a-1", title: "Item a-1", price: "10.00" }) : undefined), memory);
    const snapshot = await depopAdapter.snapshotListings(gone, seller);

    expect(snapshot.listings.find((listing) => listing.externalListingId === "b-2")).toMatchObject({ status: "Ended", quantity: 0 });
    expect(snapshot.complete).toBe(false);

    // Once ended it is not asked about again.
    const later = depop((url) => (url.pathname === "/overhaultrading/" ? shopPage(["a-1"]) : undefined), memory);
    await depopAdapter.snapshotListings(later, seller);
    expect(later.read).not.toContain("https://www.depop.com/products/b-2/");
  });

  it("keeps a listing live that is merely further down than the shop reveals, and one whose page cannot be read", async () => {
    const memory = memoryInMap();
    await depopAdapter.snapshotListings(depop(shop(["a-1", "b-2", "c-3"]), memory), seller);

    const next = depop((url) => {
      if (url.pathname === "/overhaultrading/") return shopPage(["a-1"]);
      if (/b-2/.test(url.pathname)) return productPage({ slug: "b-2", title: "Item b-2", price: "10.00" });
      if (/c-3/.test(url.pathname)) return { status: 500, url: url.href, contentType: "text/html", body: "" };
      return undefined;
    }, memory);
    const snapshot = await depopAdapter.snapshotListings(next, seller);

    expect(snapshot.listings.filter((listing) => listing.status === "Ended")).toEqual([]);
    expect(snapshot.issues.map((issue) => issue.externalReference)).toEqual(["c-3"]);
    // Both are still known live, so both are looked at again next time.
    const again = depop(shop(["a-1"]), memory);
    await depopAdapter.snapshotListings(again, seller);
    expect(again.read).toEqual(expect.arrayContaining(["https://www.depop.com/products/b-2/", "https://www.depop.com/products/c-3/"]));
  });

  it("reads a listing once and remembers it, so an unchanged shop costs one page", async () => {
    const memory = memoryInMap();
    const first = depop(shop(["a-1", "b-2"]), memory);
    await depopAdapter.snapshotListings(first, seller);

    const second = depop(shop(["a-1", "b-2"]), memory);
    const snapshot = await depopAdapter.snapshotListings(second, seller);

    expect(second.read).toEqual(["https://www.depop.com/overhaultrading/"]);
    expect(snapshot.listings.map((listing) => listing.externalListingId)).toEqual(["a-1", "b-2"]);
  });

  it("spreads a large shop over several scans rather than reading it all at once", async () => {
    const slugs = Array.from({ length: 30 }, (_, index) => `item-${index}`);
    const context = depop(shop(slugs));

    const snapshot = await depopAdapter.snapshotListings(context, seller);

    // Twenty listings and the shop page itself; the other ten wait for the next scan.
    expect(context.read).toHaveLength(21);
    expect(snapshot.listings).toHaveLength(20);
  });

  it("keeps going when one listing's page cannot be read, and says which", async () => {
    const context = depop((url) =>
      url.pathname === "/products/b-2/"
        ? { status: 404, url: url.toString(), contentType: "text/html", body: "" }
        : shop(["a-1", "b-2"])(url));

    const snapshot = await depopAdapter.snapshotListings(context, seller);

    expect(snapshot.listings.map((listing) => listing.externalListingId)).toEqual(["a-1"]);
    expect(snapshot.issues.some((issue) => !issue.rejected && issue.externalReference === "b-2")).toBe(true);
  });

  it("stops the scan at a security check rather than working through the shop", async () => {
    const context = depop((url) =>
      url.pathname.startsWith("/products/")
        ? { status: 403, url: url.toString(), contentType: "text/html", body: "Attention Required" }
        : shopPage(["a-1", "b-2"]));

    await expect(depopAdapter.snapshotListings(context, seller)).rejects.toMatchObject({ kind: "Challenge" });
  });

  it("knows who the browser is signed in as, and when it is not", async () => {
    expect(await depopAdapter.detectSession(depop(() => homePage("overhaultrading"))))
      .toMatchObject({ signedIn: true, username: "overhaultrading" });
    expect(await depopAdapter.detectSession(depop(() => homePage(null))))
      .toMatchObject({ signedIn: false, username: null });
  });

  it("takes the signed-in seller's own handle, never another shop's on the same page", async () => {
    // Depop pages link to other people constantly — an order page links to the buyer. Scanning theirs would pull a
    // stranger's listings into this operator's inventory.
    const session = await depopAdapter.detectSession(depop(() => homePage("overhaultrading", ["slapoint", "someone_else"])));

    expect(session.username).toBe("overhaultrading");
  });

  it("stops at Cloudflare's own check, and not at a listing that merely mentions one", async () => {
    const blocked = depop(() => ({ status: 200, url: "https://www.depop.com/overhaultrading/", contentType: "text/html", body: challengePage() }));
    await expect(depopAdapter.snapshotListings(blocked, seller)).rejects.toMatchObject({ kind: "Challenge" });

    // A listing about a puzzle book is not a security check.
    const chatty = depop((url) =>
      url.pathname === "/overhaultrading/"
        ? shopPage(["a-1"])
        : productPage({ slug: "a-1", title: "Captcha puzzle book", price: "5.00", description: "Are you a robot? A book of captcha puzzles." }));
    const snapshot = await depopAdapter.snapshotListings(chatty, seller);
    expect(snapshot.listings[0]!.title).toBe("Captcha puzzle book");
  });

  it("reads structured data whichever attributes its script tag carries", async () => {
    const page = productPage({ slug: "a-1", title: "Tee", price: "5.00" })
      .replace('<script type="application/ld+json">{"@type":"BreadcrumbList"}</script>', "")
      .replace('<script type="application/ld+json">', '<script data-nscript="beforeInteractive" type=\'application/ld+json\' nonce="abc">');
    const context = depop((url) => (url.pathname === "/overhaultrading/" ? shopPage(["a-1"]) : page));

    const snapshot = await depopAdapter.snapshotListings(context, seller);

    expect(snapshot.listings[0]!.title).toBe("Tee");
  });

  it("waits for the shop's grid, which Depop draws in after the page loads", async () => {
    const context = depop(shop(["a-1"]));

    await depopAdapter.snapshotListings(context, seller);

    expect(context.waits[0]).toEqual({ until: 'ol li a[href^="/products/"]' });
  });
});

describe("Depop's sold list", () => {
  it("reads each sale from its row: order, buyer, day, parcel, status, total and ship-to", () => {
    const rows = readSoldList(soldListPage([{ id: "475700000001", buyer: "floralfan", soldOn: "09/20/2026", status: "Shipped", total: "US$9.15" }]));

    expect(rows).toEqual([{
      id: "475700000001",
      buyer: "floralfan",
      items: 1,
      soldOn: "09/20/2026",
      method: "Depop shipping",
      carrier: "USPS",
      status: "Shipped",
      total: 9.15,
      shipTo: { line1: "12 Elm St", city: "Springfield", region: "IL", postalCode: "62701-1234", country: "US" },
    }]);
  });

  it("reads a sale shipped without a carrier named", () => {
    const [row] = readSoldList(soldListPage([{ id: "1", method: "Own shipping", carrier: null, status: "To ship" }]))!;

    expect(row).toMatchObject({ method: "Own shipping", carrier: null, status: "To ship" });
  });

  it("tells a seller with nothing sold apart from a page that lost its list", () => {
    expect(readSoldList(soldListPage([]))).toEqual([]);
    expect(readSoldList(soldListShell())).toBeNull();
  });
});

describe("a Depop receipt", () => {
  it("reads the money, the parcel and what was sold", () => {
    const receipt = readReceipt(receiptPage({}))!;

    expect(receipt).toMatchObject({
      buyerName: "Jordan Rivera",
      status: "Delivered",
      deliveredOn: "09/24/2026",
      carrier: "USPS",
      tracking: "9400100000000000000001",
      itemsPrice: 10,
      fees: { "Depop fee": 0, "Payment fee": 0.85 },
      total: 9.15,
      paidOut: true,
      buyerShipping: 1.99,
      products: [{ slug: "overhaultrading-old-navy-floral-5db8", title: "Old Navy floral shirt #floral #buttonup", price: 10 }],
    });
  });

  it("takes no initials for a name, and no payout that has not been sent", () => {
    const receipt = readReceipt(receiptPage({ buyerName: null, paidOut: false, deliveredOn: null }))!;

    expect(receipt).toMatchObject({ buyerName: null, paidOut: false, deliveredOn: null, tracking: null });
  });

  it("finds no receipt on a page that has none", () => {
    expect(readReceipt(soldListPage([]))).toBeNull();
  });
});

describe("a Depop sale's line title", () => {
  it("is the description's opening words, without its hashtags", () => {
    expect(titleOf("Old Navy floral shirt #floral #buttonup\nSize M, worn twice")).toBe("Old Navy floral shirt");
  });

  it("stops short at a word, however long the description runs", () => {
    const title = titleOf(`${"Vintage 90s denim trucker jacket in faded blue ".repeat(10)}#vintage`);

    expect(title.length).toBeLessThanOrEqual(80);
    expect(title).toMatch(/jacket…$|blue…$|faded…$|in…$|denim…$|trucker…$|90s…$|Vintage…$/);
  });

  it("names an item whose description is only hashtags", () => {
    expect(titleOf("#y2k #vintage")).toBe("Untitled Depop item");
  });
});

describe("a Depop sale, as the sales report would describe it", () => {
  const row = readSoldList(soldListPage([{ id: "475700000001", buyer: "floralfan" }]))![0]!;

  it("records the items price, Depop's fees, the payout and a label paid for by the buyer", () => {
    const { order } = toIngestOrder(row, readReceipt(receiptPage({}))!);

    expect(order).toMatchObject({
      externalOrderId: "475700000001",
      orderedOnDayOnly: true,
      itemsSubtotal: 10,
      shippingCharged: 1.99,
      shippingCost: 1.99,
      taxCollected: null,
      feeTotal: 0.85,
      payoutAmount: 9.15,
      buyerReference: "floralfan",
      buyerName: "Jordan Rivera",
      shipToLine1: "12 Elm St",
      shipToPostalCode: "62701-1234",
      carrier: "USPS",
      trackingNumber: "9400100000000000000001",
      fulfillmentStage: "Delivered",
      lines: [{ title: "Old Navy floral shirt", unitPrice: 10, quantity: 1, externalItemId: "overhaultrading-old-navy-floral-5db8" }],
    });
    expect(new Date(order!.orderedAt!).getDate()).toBe(20);
  });

  it("splits a bundle across its listings by their prices", () => {
    const receipt = readReceipt(receiptPage({
      itemsPrice: "US$30.00",
      products: [
        { slug: "a-1", title: "Shirt", price: "$10.00" },
        { slug: "b-2", title: "Jeans", price: "$20.00" },
      ],
    }))!;

    expect(toIngestOrder({ ...row, items: 2 }, receipt).order!.lines!.map((line) => [line.externalItemId, line.unitPrice])).toEqual([["a-1", 10], ["b-2", 20]]);
  });

  it("leaves the payout open until Depop has sent it", () => {
    expect(toIngestOrder(row, readReceipt(receiptPage({ paidOut: false }))!).order!.payoutAmount).toBeNull();
  });

  it("refuses a receipt that names no listing, rather than record a sale of nothing", () => {
    const { order, issues } = toIngestOrder(row, readReceipt(receiptPage({ products: [] }))!);

    expect(order).toBeNull();
    expect(issues[0]).toMatchObject({ externalReference: "475700000001", rejected: true });
  });
});

describe("a Depop sales scan", () => {
  const first = { from: null, cursor: null };
  const answer = (sales: Parameters<typeof soldListPage>[0]) => (url: URL) =>
    url.pathname === "/sellinghub/sold-items/" ? soldListPage(sales) : url.pathname.startsWith("/sellinghub/sold-items/") ? receiptPage({}) : undefined;

  it("reads the list, then each new sale's receipt, waiting for what Depop draws in", async () => {
    const context = depop(answer([{ id: "475700000001" }]));

    const scan = await depopAdapter.scanSales(context, first, seller);

    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["475700000001"]);
    expect(context.read).toEqual(["https://www.depop.com/sellinghub/sold-items/", "https://www.depop.com/sellinghub/sold-items/475700000001/"]);
    expect(context.waits).toEqual([{ until: 'a[aria-label^="View receipt"]' }, { until: 'aside[role="dialog"] table' }]);
  });

  it("reports a refunded or cancelled sale as one, and reads no receipt for it", async () => {
    const context = depop(answer([{ id: "1", status: "Payment refunded" }, { id: "2", status: "Canceled" }]));

    const scan = await depopAdapter.scanSales(context, first, seller);

    expect(scan.cancelledOrderIds).toEqual(["1", "2"]);
    expect(scan.orders).toEqual([]);
    expect(context.read).toHaveLength(1);
  });

  it("reads an unchanged sale's receipt only once", async () => {
    const memory = memoryInMap();
    await depopAdapter.scanSales(depop(answer([{ id: "475700000001" }]), memory), first, seller);
    const context = depop(answer([{ id: "475700000001" }]), memory);

    const scan = await depopAdapter.scanSales(context, first, seller);

    expect(scan.orders).toHaveLength(1);
    expect(context.read).toHaveLength(1);
  });

  it("reads an open sale's receipt again after a while, since its payout and parcel change without its row", async () => {
    const memory = memoryInMap();
    const pending = (url: URL) => url.pathname === "/sellinghub/sold-items/" ? soldListPage([{ id: "475700000001" }]) : receiptPage({ paidOut: false });
    await depopAdapter.scanSales(depop(pending, memory), first, seller);
    const soon = depop(pending, memory);
    const later = { ...depop(answer([{ id: "475700000001" }]), memory), now: () => new Date(now.getTime() + 7 * 60 * 60 * 1000) };

    await depopAdapter.scanSales(soon, first, seller);
    const scan = await depopAdapter.scanSales(later, first, seller);

    expect(soon.read).toHaveLength(1);
    expect(later.read).toHaveLength(2);
    expect(scan.orders[0]!.payoutAmount).toBe(9.15);
  });

  it("never reads a settled sale's receipt again while its row is unchanged", async () => {
    const memory = memoryInMap();
    await depopAdapter.scanSales(depop(answer([{ id: "475700000001" }]), memory), first, seller);
    const muchLater = { ...depop(answer([{ id: "475700000001" }]), memory), now: () => new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000) };

    await depopAdapter.scanSales(muchLater, first, seller);

    expect(muchLater.read).toHaveLength(1);
  });

  it("reads a few receipts per scan and says the rest come next", async () => {
    const sales = Array.from({ length: 17 }, (_, index) => ({ id: String(475700000100 + index) }));
    const context = depop(answer(sales));

    const scan = await depopAdapter.scanSales(context, first, seller);

    expect(scan.orders).toHaveLength(15);
    expect(scan.issues).toEqual([expect.objectContaining({ message: "2 more Depop sales are read on the next scan.", rejected: false })]);
  });

  it("keeps going past a receipt it cannot read, and says which", async () => {
    const context = depop((url) =>
      url.pathname === "/sellinghub/sold-items/" ? soldListPage([{ id: "1" }, { id: "2" }])
        : url.pathname === "/sellinghub/sold-items/1/" ? soldListPage([]) : receiptPage({}));

    const scan = await depopAdapter.scanSales(context, first, seller);

    expect(scan.orders).toHaveLength(1);
    expect(scan.issues[0]).toMatchObject({ externalReference: "1", rejected: true });
  });

  it("leaves a receipt that is slow to load for the next scan, and carries on", async () => {
    const context = depop((url) => {
      if (url.pathname === "/sellinghub/sold-items/1/") {
        throw new AdapterError("Network", "The page took too long to load.");
      }

      return url.pathname === "/sellinghub/sold-items/" ? soldListPage([{ id: "1" }, { id: "2" }]) : receiptPage({});
    });

    const scan = await depopAdapter.scanSales(context, first, seller);

    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["2"]);
    expect(scan.issues).toEqual([expect.objectContaining({ externalReference: "1", rejected: false })]);
  });

  it("stops when receipts keep failing to load, rather than wait out every one", async () => {
    const context = depop((url) => {
      if (url.pathname !== "/sellinghub/sold-items/") {
        throw new AdapterError("Network", "The page took too long to load.");
      }

      return soldListPage([{ id: "1" }, { id: "2" }, { id: "3" }]);
    });

    await expect(depopAdapter.scanSales(context, first, seller)).rejects.toMatchObject({ kind: "Network" });
    expect(context.read).toHaveLength(3);
  });

  it("says the site changed when the sold page carries no list at all", async () => {
    await expect(depopAdapter.scanSales(depop(() => soldListShell()), first, seller)).rejects.toMatchObject({ kind: "SiteChanged" });
  });
});

describe("the Depop sign-in, read from a page the operator has open", () => {
  const page = (body: string): PageResponse => ({ status: 200, url: "https://www.depop.com/", contentType: "text/html", body });

  it("is read from the header's own Likes link", () => {
    expect(depopAdapter.sessionFrom!(page(homePage("overhaultrading")))).toMatchObject({ signedIn: true, username: "overhaultrading" });
  });

  it("cannot say someone is signed out from a page without the header, so the check loads one instead", () => {
    expect(depopAdapter.sessionFrom!(page(homePage(null)))).toBeNull();
  });
});

