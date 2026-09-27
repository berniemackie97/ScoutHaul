import { describe, expect, it } from "vitest";
import { challengePage, homePage, productPage, shopPage } from "../../test/depop-fixtures";
import { memoryInMap } from "../../test/memory";
import { AdapterError, type MarketplaceContext, type PageResponse, type Session } from "../types";
import { depopAdapter, readShop } from ".";
import { readProduct } from "./product";

const now = new Date("2026-09-26T12:00:00Z");
const seller: Session = { signedIn: true, username: "overhaultrading", marketplaceUserId: null };

function depop(answer: (url: URL) => string | PageResponse | undefined, memory = memoryInMap()): MarketplaceContext & { read: string[] } {
  const read: string[] = [];
  const serve = async (url: string): Promise<PageResponse> => {
    read.push(url);
    const reply = answer(new URL(url));
    if (reply === undefined) {
      return { status: 404, url, contentType: "text/html", body: "" };
    }

    return typeof reply === "string" ? { status: 200, url, contentType: "text/html", body: reply } : reply;
  };
  return { read, now: () => now, memory, page: serve, request: serve };
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

  it("does not pretend to read sales", async () => {
    await expect(depopAdapter.scanSales(depop(() => ""), { from: null, cursor: null }, seller)).rejects.toThrow(AdapterError);
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

