import { describe, expect, it } from "vitest";
import { closetPage, homePage } from "../../test/poshmark-fixtures";
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

  it("says a page carries no listings rather than inventing them", () => {
    expect(readClosetPage(closetPage([])).listings).toEqual([]);
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

  it("reads the whole closet, page by page, and its sold listings too", async () => {
    const context = poshmark((url) => {
      const page = Number(url.searchParams.get("max_id") ?? "1");
      if (url.searchParams.get("availability") === "sold_out") {
        return page === 1 ? closetPage([{ id: "sold-1", title: "Sold tee", price: 15, inventoryStatus: "sold_out", available: 0, sold: 1 }]) : closetPage([]);
      }

      return page === 1
        ? closetPage([{ id: "a", title: "First", price: 10 }], true)
        : page === 2
          ? closetPage([{ id: "b", title: "Second", price: 20 }])
          : closetPage([]);
    });

    const snapshot = await poshmarkAdapter.snapshotListings(context, seller);

    expect(snapshot.complete).toBe(true);
    expect(snapshot.listings.map((listing) => listing.externalListingId)).toEqual(["a", "b", "sold-1"]);
    // Two pages of the closet, and one of its sold listings: nothing is asked for twice.
    expect(context.read.filter((url) => url.includes("availability=sold_out"))).toHaveLength(1);
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
