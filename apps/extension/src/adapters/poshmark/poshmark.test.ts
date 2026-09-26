import { describe, expect, it } from "vitest";
import { closetPage, closetPageWithoutListings, homePage } from "../../test/poshmark-fixtures";
import { memoryInMap } from "../../test/memory";
import { AdapterError, type MarketplaceContext, type PageResponse, type Session } from "../types";
import { poshmarkAdapter } from ".";
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

  it("reads the state out of the page without being fooled by a brace in a string", () => {
    // The fixture writes another script after the state whose value contains a closing brace.
    expect(readClosetPage(closetPage([{ id: "1", title: "Tee", price: 5 }])).listings).toHaveLength(1);
  });

  it("tells an empty closet apart from a page that lost its listings", () => {
    expect(readClosetPage(closetPage([]))).toMatchObject({ listings: [], found: true });
    expect(readClosetPage(closetPageWithoutListings())).toMatchObject({ listings: [], found: false });
    expect(() => readClosetPage("<html><body>nothing here</body></html>")).toThrow(AdapterError);
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
      body: "<html><body>Are you a robot?</body></html>",
    }));

    await expect(poshmarkAdapter.snapshotListings(context, seller)).rejects.toMatchObject({ kind: "Challenge" });
  });

  it("will not scan a closet it has no name for", async () => {
    const context = poshmark(() => closetPage([]));

    await expect(poshmarkAdapter.snapshotListings(context, { ...seller, username: null }))
      .rejects.toMatchObject({ kind: "SignedOut" });
    expect(context.read).toEqual([]);
  });

  it("does not pretend to read sales", async () => {
    await expect(poshmarkAdapter.scanSales(poshmark(() => ""), { from: null, cursor: null }, seller)).rejects.toThrow(AdapterError);
  });
});
