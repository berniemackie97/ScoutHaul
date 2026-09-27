import { describe, expect, it, vi } from "vitest";
import { closetPage, closetPageWithoutListings, homePage, salePage, salesPage } from "../../test/poshmark-fixtures";
import { memoryInMap } from "../../test/memory";
import { AdapterError, type MarketplaceContext, type PageResponse, type Session } from "../types";
import { maxSalesPages, poshmarkAdapter } from ".";
import { readClosetPage } from "./listings";

const now = new Date("2026-09-26T12:00:00Z");
const seller: Session = { signedIn: true, username: "overhaultrading", marketplaceUserId: "6a28968fe948982883b29ccf" };

/** A Poshmark tab that answers from a handler and remembers what it was asked for. */
function poshmark(answer: (url: URL) => string | PageResponse | undefined): MarketplaceContext & { read: string[] } {
  const read: string[] = [];
  const serve = async (url: string): Promise<PageResponse> => {
    read.push(url);
    const reply = answer(new URL(url));
    if (reply === undefined) {
      return { status: 404, url, contentType: "text/html", body: "" };
    }

    return typeof reply === "string" ? { status: 200, url, contentType: "text/html", body: reply } : reply;
  };
  return { read, now: () => now, memory: memoryInMap(), page: serve, request: serve };
}

describe("a Poshmark closet", () => {
  it("reads a listing whole from the page it is served on", () => {
    const page = closetPage([
      {
        id: "6a4a90a11986c2a9de9f4f69",
        title: "Ripple Junction Blue Dunder Mifflin Graphic Tee",
        price: 4,
        colors: ["Blue", "White"],
        description: "Bold white logo print.",
      },
    ]);

    const listing = readClosetPage(page).listings[0]!;

    expect(listing).toMatchObject({
      externalListingId: "6a4a90a11986c2a9de9f4f69",
      title: "Ripple Junction Blue Dunder Mifflin Graphic Tee",
      price: 4,
      platform: "Poshmark",
      status: "Active",
      brand: "Ripple Junction",
      size: "XL",
      color: "Blue, White",
      condition: "Used",
      categoryName: "Shirts",
      description: "Bold white logo print.",
      quantity: 1,
      quantitySold: 0,
    });
    expect(listing.primaryImageUrl).toContain("l_cover.jpg");
    // One picture is not a gallery, so none is offered: the rest live on the listing's own page.
    expect(listing.photoUrls).toBeUndefined();
    expect(listing.listedAt).toBe("2026-07-05T17:13:05.000Z");
  });

  it("calls a sold-out listing sold, however Poshmark still publishes it", () => {
    const page = closetPage([
      { id: "1", title: "Sold tee", price: 15, inventoryStatus: "sold_out", available: 0, sold: 1, status: "published" },
    ]);

    expect(readClosetPage(page).listings[0]).toMatchObject({ status: "Sold", quantity: 0, quantitySold: 1 });
  });

  it("reads the state the page built, since the script that wrote it is gone from the HTML", () => {
    const page = closetPage([{ id: "1", title: "Tee", price: 5 }]);

    expect(page.body).not.toContain("__INITIAL_STATE__");
    expect(readClosetPage(page).listings).toHaveLength(1);
    expect(() => readClosetPage({ ...page, global: null })).toThrow(AdapterError);
  });

  it("tells an empty closet apart from a page that lost its listings", () => {
    expect(readClosetPage(closetPage([]))).toMatchObject({ listings: [], found: true });
    expect(readClosetPage(closetPageWithoutListings())).toMatchObject({ listings: [], found: false });
    expect(() => readClosetPage({ status: 200, url: "https://poshmark.com/", contentType: "text/html", body: "<html><body>nothing here</body></html>" }))
      .toThrow(AdapterError);
  });
});

describe("a Poshmark scan", () => {
  it("knows who the browser is signed in as, and when it is not", async () => {
    expect(await poshmarkAdapter.detectSession(poshmark(() => homePage("overhaultrading"))))
      .toEqual({ signedIn: true, username: "overhaultrading", marketplaceUserId: "6a28968fe948982883b29ccf" });
    expect(await poshmarkAdapter.detectSession(poshmark(() => homePage(null))))
      .toEqual({ signedIn: false, username: null, marketplaceUserId: null });
  });

  it("follows Poshmark's own cursor through the closet, and reads its sold listings too", async () => {
    const context = poshmark((url) => {
      const cursor = url.searchParams.get("max_id");
      if (url.searchParams.get("availability") === "sold_out") {
        return closetPage([{ id: "sold-1", title: "Sold tee", price: 15, inventoryStatus: "sold_out", available: 0, sold: 1 }]);
      }

      // The first page is asked for without a cursor, and hands out the one for the second.
      return cursor === null
        ? closetPage([{ id: "a", title: "First", price: 10 }], "cursor-abc")
        : cursor === "cursor-abc"
          ? closetPage([{ id: "b", title: "Second", price: 20 }])
          : closetPage([]);
    });

    const snapshot = await poshmarkAdapter.snapshotListings(context, seller);

    expect(snapshot.complete).toBe(true);
    expect(snapshot.listings.map((listing) => listing.externalListingId)).toEqual(["a", "b", "sold-1"]);
    expect(context.read[0]).not.toContain("max_id");
    expect(context.read[1]).toContain("max_id=cursor-abc");
    expect(context.read.filter((url) => url.includes("availability=sold_out"))).toHaveLength(1);
  });

  it("falls back to the page number when a closet says there is more but not where", async () => {
    // Observed behaviour: a closet also answers to a plain page number, so a missing cursor does not strand a scan.
    const context = poshmark((url) => {
      const cursor = url.searchParams.get("max_id");
      if (url.searchParams.get("availability") === "sold_out") {
        return closetPage([]);
      }

      return cursor === null ? closetPage([{ id: "a", title: "First", price: 10 }], true) : closetPage([{ id: "b", title: "Second", price: 20 }]);
    });

    const snapshot = await poshmarkAdapter.snapshotListings(context, seller);

    expect(context.read[1]).toContain("max_id=2");
    expect(snapshot.listings.map((listing) => listing.externalListingId)).toEqual(["a", "b"]);
  });

  it("reads a closet whose listings have all sold", async () => {
    // Nothing active is an ordinary state, not a changed site — and its sold listings still have to be read.
    const context = poshmark((url) =>
      url.searchParams.get("availability") === "sold_out"
        ? closetPage([{ id: "sold-1", title: "Sold tee", price: 15, inventoryStatus: "sold_out", available: 0, sold: 1 }])
        : closetPage([]));

    const snapshot = await poshmarkAdapter.snapshotListings(context, seller);

    expect(snapshot.complete).toBe(true);
    expect(snapshot.listings.map((listing) => listing.externalListingId)).toEqual(["sold-1"]);
  });

  it("says the site changed when a closet page carries no list at all", async () => {
    const context = poshmark(() => closetPageWithoutListings());

    await expect(poshmarkAdapter.snapshotListings(context, seller)).rejects.toMatchObject({ kind: "SiteChanged" });
  });

  it("stops at a security check rather than asking again", async () => {
    const context = poshmark((url) => ({
      status: 403,
      url: url.toString(),
      contentType: "text/html",
      body: "<html><head><title>Just a moment...</title></head><body>/cdn-cgi/challenge-platform/</body></html>",
    }));

    await expect(poshmarkAdapter.snapshotListings(context, seller)).rejects.toMatchObject({ kind: "Challenge" });
  });

  it("will not scan a closet it has no name for", async () => {
    const context = poshmark(() => closetPage([]));

    await expect(poshmarkAdapter.snapshotListings(context, { ...seller, username: null }))
      .rejects.toMatchObject({ kind: "SignedOut" });
    expect(context.read).toEqual([]);
  });

});

describe("Poshmark sales", () => {
  const first = { from: null, cursor: null };
  const routine = { from: null, cursor: "2026-07-18T00:00:00.000Z" };

  it("waits for Seller Hub to fill in its sales before reading them, so a page still filling is never taken for a changed one", async () => {
    const context = poshmark(() => salesPage([{ id: "s1" }]));
    const page = vi.spyOn(context, "page");

    await poshmarkAdapter.scanSales(context, first, seller);

    expect(page).toHaveBeenCalledWith("https://poshmark.com/seller-hub/sales", { global: "__INITIAL_STATE__", holds: ["$_order_list", "userSales", "data", "sales_summary"] });
  });

  it("records a sale with Poshmark's cut, the buyer and where it ships", async () => {
    const scan = await poshmarkAdapter.scanSales(poshmark(() => salesPage([{ id: "68f1a2b3c4d5e6f708192a3b" }])), first, seller);

    expect(scan.issues).toEqual([]);
    expect(scan.orders).toHaveLength(1);
    expect(scan.orders[0]).toMatchObject({
      externalOrderId: "68f1a2b3c4d5e6f708192a3b",
      platform: "Poshmark",
      orderedAt: "2026-07-19T03:32:28.000Z",
      itemsSubtotal: 4,
      shippingCharged: 0,
      shippingCost: 0,
      taxCollected: 0,
      taxRemittedByPlatform: true,
      feeTotal: 2.95,
      feesPending: false,
      payoutAmount: 1.05,
      currency: "USD",
      buyerName: "Jordan Rivera",
      buyerReference: "jrivera",
      shipToName: "Jordan Rivera",
      shipToLine1: "12 Elm St",
      shipToLine2: "Apt 4",
      shipToCity: "Springfield",
      shipToRegion: "IL",
      shipToPostalCode: "62701",
      shipToCountry: "US",
      fulfillmentStage: "Delivered",
      fulfillmentDetail: "Order Complete",
      lines: [{ title: "Ripple Junction Blue Dunder Mifflin Graphic Tee", unitPrice: 4, quantity: 1, externalItemId: "6a4a90a11986c2a9de9f4f69" }],
    });
    expect(JSON.parse(scan.cursor!)).toEqual({ newest: "2026-07-19T03:32:28.000Z", resume: null });
  });

  it("leaves the payout open until Poshmark completes the order", async () => {
    const scan = await poshmarkAdapter.scanSales(
      poshmark(() => salesPage([{ id: "s1", total: "25.0", earning: "20.0", displayStatus: "Sold" }])), first, seller);

    expect(scan.orders[0]).toMatchObject({ feeTotal: 5, payoutAmount: null, fulfillmentStage: "NeedsShipping" });
  });

  it("reports a cancelled or refunded sale as one, and records no money for it", async () => {
    const scan = await poshmarkAdapter.scanSales(poshmark(() => salesPage([
      { id: "c1", displayStatus: "Cancelled" },
      { id: "r1", displayStatus: "Returned" },
      { id: "o1", displayStatus: "Return Requested" },
    ])), first, seller);

    expect(scan.cancelledOrderIds).toEqual(["c1", "r1"]);
    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["o1"]);
  });

  it("keeps a sale whose cut cannot be read, with its fees left open", async () => {
    const scan = await poshmarkAdapter.scanSales(poshmark(() => salesPage([{ id: "s1", earning: null }])), first, seller);

    expect(scan.orders[0]).toMatchObject({ feeTotal: null, feesPending: true, payoutAmount: null });
    expect(scan.issues[0]).toMatchObject({ externalReference: "s1", rejected: false });
  });

  it("splits a bundle into the listings its own page shows, and reads that page once", async () => {
    const ids = ["6a4a90a11986c2a9de9f4f69", "6a4a90a11986c2a9de9f4f70", "6a4a90a11986c2a9de9f4f71"];
    const context = poshmark((url) =>
      url.pathname === "/order/sales/b1" ? salePage(ids) : salesPage([{ id: "b1", total: "30.0", earning: "24.0", bundleSize: 3 }]));

    const scan = await poshmarkAdapter.scanSales(context, first, seller);

    expect(scan.orders[0]!.lines!.map((line) => [line.externalItemId, line.unitPrice])).toEqual([[ids[0], 10], [ids[1], 10], [ids[2], 10]]);
    expect(scan.orders[0]).toMatchObject({ itemsSubtotal: 30, feeTotal: 6, payoutAmount: 24 });
    expect(scan.issues[0]).toMatchObject({ externalReference: "b1", rejected: false });

    await poshmarkAdapter.scanSales(context, first, seller);
    expect(context.read.filter((url) => url.includes("/order/sales/b1"))).toHaveLength(1);
  });

  it("puts the rounding on the last item of a bundle, so the lines add up to the total", async () => {
    const ids = ["6a4a90a11986c2a9de9f4f69", "6a4a90a11986c2a9de9f4f70", "6a4a90a11986c2a9de9f4f71"];
    const context = poshmark((url) => url.pathname === "/order/sales/b1" ? salePage(ids) : salesPage([{ id: "b1", total: "10.0", bundleSize: 3 }]));

    const lines = (await poshmarkAdapter.scanSales(context, first, seller)).orders[0]!.lines!;

    expect(lines.map((line) => line.unitPrice)).toEqual([3.33, 3.33, 3.34]);
  });

  it("refuses to guess a bundle its page does not account for, and asks again next scan", async () => {
    const context = poshmark((url) =>
      url.pathname === "/order/sales/b1" ? salePage(["6a4a90a11986c2a9de9f4f69"]) : salesPage([{ id: "b1", total: "30.0", bundleSize: 3 }]));

    const scan = await poshmarkAdapter.scanSales(context, first, seller);

    expect(scan.orders).toEqual([]);
    expect(scan.issues[0]).toMatchObject({ externalReference: "b1", rejected: true });
    expect(scan.issues[0]!.message).toContain("Sold, not recorded");
    await expect(context.memory.get("sale:b1")).resolves.toBeUndefined();
  });

  it("refuses a bundle that does not say how many listings it held", async () => {
    const context = poshmark(() => salesPage([{ id: "b1", total: "30.0", isBundle: true, bundleSize: null }]));

    const scan = await poshmarkAdapter.scanSales(context, first, seller);

    expect(scan.orders).toEqual([]);
    expect(scan.issues[0]).toMatchObject({ externalReference: "b1", rejected: true });
    expect(context.read).toHaveLength(1);
  });

  it("carries on next scan from where a scan ran out of pages, after reading what is new", async () => {
    const pages = maxSalesPages + 50;
    const context = poshmark((url) => {
      const page = Number(url.searchParams.get("max_id") ?? "1");
      return salesPage([{ id: `s${page}` }], page < pages ? String(page + 1) : null);
    });

    const capped = await poshmarkAdapter.scanSales(context, first, seller);

    expect(capped.orders).toHaveLength(maxSalesPages);
    expect(JSON.parse(capped.cursor!).resume).toBe(String(maxSalesPages + 1));
    expect(capped.issues.map((issue) => issue.rejected)).toEqual([false]);

    context.read.length = 0;
    const resumed = await poshmarkAdapter.scanSales(context, { from: null, cursor: capped.cursor }, seller);

    expect(context.read[0]).not.toContain("max_id");
    expect(context.read[1]).toContain(`max_id=${maxSalesPages + 1}`);
    expect(resumed.orders.map((order) => order.externalOrderId)).toEqual(["s1", ...Array.from({ length: 50 }, (_, index) => `s${maxSalesPages + 1 + index}`)]);
    expect(JSON.parse(resumed.cursor!).resume).toBeNull();
    expect(resumed.issues).toEqual([]);
  });

  it("follows Poshmark's cursor through the whole history on a first scan", async () => {
    const context = poshmark((url) => url.searchParams.get("max_id") === "next-1"
      ? salesPage([{ id: "old" }])
      : salesPage([{ id: "new" }], "next-1"));

    const scan = await poshmarkAdapter.scanSales(context, first, seller);

    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["new", "old"]);
    expect(context.read[1]).toContain("max_id=next-1");
  });

  it("stops a routine scan at a page with nothing new and nothing still under way", async () => {
    const context = poshmark((url) => url.searchParams.get("max_id") === "next-1"
      ? salesPage([{ id: "old" }])
      : salesPage([{ id: "new" }], "next-1"));
    await poshmarkAdapter.scanSales(context, first, seller);
    context.read.length = 0;

    await poshmarkAdapter.scanSales(context, routine, seller);

    expect(context.read).toHaveLength(1);
  });

  it("reads on past a page whose sales are still under way, so a later payout is not missed", async () => {
    const context = poshmark((url) => url.searchParams.get("max_id") === "next-1"
      ? salesPage([{ id: "old" }])
      : salesPage([{ id: "new", displayStatus: "Shipped" }], "next-1"));
    await poshmarkAdapter.scanSales(context, first, seller);
    context.read.length = 0;

    await poshmarkAdapter.scanSales(context, routine, seller);

    expect(context.read).toHaveLength(2);
  });

  it("reads only sales booked since the date a history scan asks for", async () => {
    const context = poshmark(() => salesPage([
      { id: "recent", bookedAt: "2026-09-01T10:00:00-07:00" },
      { id: "older", bookedAt: "2026-06-01T10:00:00-07:00" },
    ]));

    const scan = await poshmarkAdapter.scanSales(context, { from: new Date("2026-08-01T00:00:00Z"), cursor: null }, seller);

    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["recent"]);
    expect(scan.coversFrom).toBe("2026-08-01T00:00:00.000Z");
  });

  it("says the site changed when the sales page carries no list", async () => {
    await expect(poshmarkAdapter.scanSales(poshmark(() => homePage("overhaultrading")), first, seller))
      .rejects.toMatchObject({ kind: "SiteChanged" });
  });

  it("says it is signed out when Poshmark sends the sales page to sign-in", async () => {
    const context = poshmark((url) => ({ status: 200, url: "https://poshmark.com/login?next=/seller-hub/sales", contentType: "text/html", body: url.toString() }));

    await expect(poshmarkAdapter.scanSales(context, first, seller)).rejects.toMatchObject({ kind: "SignedOut" });
  });
});

describe("the Poshmark sign-in, read from a page the operator has open", () => {
  it("is read from any Poshmark page's state, without loading one", () => {
    expect(poshmarkAdapter.sessionFrom!(closetPage([]))).toMatchObject({ signedIn: true, username: "overhaultrading" });
    expect(poshmarkAdapter.sessionFrom!(homePage(null))).toEqual({ signedIn: false, username: null, marketplaceUserId: null });
    expect(poshmarkAdapter.sessionGlobal).toBe("__INITIAL_STATE__");
  });

  it("cannot tell from a page without its state, so the check loads one instead", () => {
    const bare = { status: 200, url: "https://poshmark.com/", contentType: "text/html", body: "<html></html>" };
    expect(poshmarkAdapter.sessionFrom!(bare)).toBeNull();
    expect(poshmarkAdapter.sessionFrom!({ ...bare, global: JSON.stringify({ somethingElse: true }) })).toBeNull();
  });
});

