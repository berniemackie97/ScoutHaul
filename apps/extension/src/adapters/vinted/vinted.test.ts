import { describe, expect, it } from "vitest";
import { memoryInMap } from "../../test/memory";
import { orderList, orderRow, transaction, vintedPage, wardrobe, wardrobeItem } from "../../test/vinted-fixtures";
import type { MarketplaceContext, PageResponse, Session } from "../types";
import { tokenCookie, tokenExpiry, vintedAdapter } from ".";
import { toIngestListing } from "./listings";
import { allocate } from "../amounts";
import { toIngestOrder } from "./orders";

const now = new Date("2026-09-19T12:00:00Z");
const seller: Session = { signedIn: true, username: "test_seller", marketplaceUserId: "5550001" };

type Answer = PageResponse | object | undefined;

/** A sign-in token as Vinted's cookie holds one: a JWT that stops working at `expiresAt`. */
function token(expiresAt: Date): string {
  const part = (value: object) => btoa(JSON.stringify(value)).replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
  return `${part({ alg: "RS256" })}.${part({ sub: "5550001", exp: Math.floor(expiresAt.getTime() / 1000) })}.signature`;
}

const live = token(new Date(now.getTime() + 2 * 60 * 60_000));
const lapsed = token(new Date(now.getTime() - 60_000));

/**
 * A Vinted tab that answers from a handler, and remembers what it was asked for. `jar` is the browser's cookie jar,
 * when the test gives it one: a function, so a page load can renew what is in it.
 */
function vinted(answer: (url: URL) => Answer, memory = memoryInMap(), jar?: () => string | null) {
  const requested: string[] = [];
  const opened: string[] = [];
  const serve = async (url: string): Promise<PageResponse> => {
    const reply = answer(new URL(url));
    if (reply === undefined) {
      throw new Error(`No fixture for ${url}`);
    }

    return "status" in reply && "body" in reply ? (reply as PageResponse) : { status: 200, url, contentType: "application/json", body: JSON.stringify(reply) };
  };
  const context: MarketplaceContext & { requested: string[]; opened: string[] } = {
    requested,
    opened,
    memory,
    now: () => now,
    page: (url) => {
      opened.push(url);
      return serve(url);
    },
    request: (url) => {
      requested.push(url);
      return serve(url);
    },
    sleep: async () => undefined,
    ...(jar ? { cookie: async (name: string) => (name === tokenCookie ? jar() : null) } : {}),
  };
  return context;
}

/** Twenty-five sales over two pages of the order list — one open, two cancelled, the rest completed. */
function history() {
  const rows = Array.from({ length: 25 }, (_, index) => {
    const id = 1000 + index;
    const day = String(28 - index).padStart(2, "0");
    const state = index === 0 ? "open" : index === 7 || index === 13 ? "cancelled" : "completed";
    return orderRow(id, `2026-08-${day}T10:00:00-06:00`, "8.5", state);
  });
  return { rows, pages: [rows.slice(0, 20), rows.slice(20)] };
}

function answering(pages: ReturnType<typeof orderRow>[][]) {
  return (url: URL): Answer => {
    if (url.pathname === "/api/v2/my_orders") {
      const page = Number(url.searchParams.get("page"));
      return orderList(pages[page - 1] ?? [], page, pages.length);
    }

    const sale = /^\/api\/v2\/transactions\/(\d+)$/.exec(url.pathname);
    return sale ? transaction(Number(sale[1]), { price: "8.5" }) : undefined;
  };
}

const transactionsRead = (context: { requested: string[] }) => context.requested.filter((url) => url.includes("/transactions/")).length;

describe("a Vinted sale", () => {
  it("is recorded at the price agreed, with no seller fees, and paid out once complete", () => {
    const { order, issues } = toIngestOrder(orderRow(22309513974, "2026-09-18T09:50:48-06:00", "8.5"), transaction(22309513974, {
      price: "8.5",
      items: [{ id: 9482228763, title: "Plaid Dress Shirt", listed: "12.0" }],
      paidAt: "2026-09-15T03:45:17-06:00",
    }));

    expect(issues).toEqual([]);
    expect(order).toMatchObject({
      externalOrderId: "22309513974",
      orderedAt: "2026-09-15T09:45:17.000Z",
      itemsSubtotal: 8.5,
      shippingCharged: 0,
      taxCollected: 0,
      feeTotal: 0,
      shippingCost: 0,
      payoutAmount: 8.5,
      platform: "Vinted",
      buyerReference: "test_buyer",
      lines: [{ title: "Plaid Dress Shirt", unitPrice: 8.5, quantity: 1, externalItemId: "9482228763" }],
    });
    expect(JSON.parse(order!.feeBreakdownJson!)).toEqual({ source: "Vinted transaction", paidByBuyer: { buyerProtection: 1.13 } });
  });

  it("is not paid out while still under way", () => {
    expect(toIngestOrder(orderRow(1, "2026-09-18T10:00:00-06:00", "8.0", "open"), transaction(1, { price: "8.0" })).order?.payoutAmount).toBeNull();
  });

  it("splits a bundle's price across its items by what they were listed at", () => {
    expect(allocate(30, [10, 30])).toEqual([7.5, 22.5]);
    expect(allocate(10, [null, null, null])).toEqual([3.33, 3.33, 3.34]);

    const { order } = toIngestOrder(orderRow(2, "2026-09-18T10:00:00-06:00", "30.0"), transaction(2, {
      price: "30.0",
      items: [{ id: 21, title: "Shirt", listed: "10.0" }, { id: 22, title: "Jacket", listed: "30.0" }],
    }));
    expect(order!.lines!.map((line) => [line.externalItemId, line.unitPrice])).toEqual([["21", 7.5], ["22", 22.5]]);
  });

  it("records the seller's own shipping as charged to the buyer, at a cost it cannot know", () => {
    const { order } = toIngestOrder(orderRow(3, "2026-09-18T10:00:00-06:00", "20.0"), transaction(3, { price: "20.0", ownShipping: "5.0" }));
    expect(order).toMatchObject({ shippingCharged: 5, shippingCost: null, payoutAmount: 25 });
  });

  it("says when a business account's fees are not read, and refuses a sale with no price", () => {
    expect(toIngestOrder(orderRow(4, "2026-09-18T10:00:00-06:00", "8.0"), transaction(4, { price: "8.0", business: true })).issues)
      .toEqual([expect.objectContaining({ rejected: false, message: expect.stringContaining("business") })]);

    const priceless = transaction(5, { price: "8.0" });
    priceless.transaction!.offer = null;
    expect(toIngestOrder({ ...orderRow(5, "2026-09-18T10:00:00-06:00", "8.0"), price: undefined }, priceless))
      .toEqual({ order: null, issues: [{ message: "The sale price could not be read.", externalReference: "5", rejected: true }] });
  });
});

describe("a Vinted listing", () => {
  it("reads each state a wardrobe item can be in", () => {
    expect(toIngestListing(wardrobeItem(1))).toMatchObject({
      externalListingId: "1",
      status: "Active",
      price: 15,
      quantitySold: 0,
      condition: "Very good",
      // The main photo leads, and the full-size address is the one kept.
      primaryImageUrl: "https://images1.vinted.net/tc/1-b/1.webp",
      photoUrls: ["https://images1.vinted.net/tc/1-b/1.webp", "https://images1.vinted.net/tc/1-a/1.webp"],
    });
    expect(toIngestListing(wardrobeItem(2, { is_closed: true, item_closing_action: "sold" }))).toMatchObject({ status: "Sold", quantitySold: 1 });
    expect(toIngestListing(wardrobeItem(3, { is_hidden: true }))?.status).toBe("Ended");
    expect(toIngestListing(wardrobeItem(4, { is_draft: true }))?.status).toBe("Draft");
    expect(toIngestListing(wardrobeItem(5, { price: null }))).toBeNull();
  });

  it("hands over what Vinted says the thing is", () => {
    // Vinted carries these on the wardrobe item itself, so they cost no extra request.
    expect(toIngestListing(wardrobeItem(1, { brand: "Carhartt", size: "M" }))).toMatchObject({
      brand: "Carhartt",
      size: "M",
      condition: "Very good",
    });
    expect(toIngestListing(wardrobeItem(2))).toMatchObject({ brand: null, size: null });

    // A wardrobe item that carries a description hands it over; one that does not says nothing, which leaves
    // whatever is known alone rather than erasing it.
    expect(toIngestListing(wardrobeItem(3, { description: "  Worn twice, no flaws.  " }))?.description)
      .toBe("Worn twice, no flaws.");
    expect(toIngestListing(wardrobeItem(4))?.description).toBeUndefined();
    expect(toIngestListing(wardrobeItem(5, { description: "   " }))?.description).toBeUndefined();
  });
});

describe("scanning Vinted sales", () => {
  it("reads the whole history the first time, and tells HaulScout which sales were cancelled", async () => {
    const { pages } = history();
    const context = vinted(answering(pages));

    const scan = await vintedAdapter.scanSales(context, { from: null, cursor: null }, seller);

    expect(scan.orders).toHaveLength(23);
    expect(scan.cancelledOrderIds).toEqual(["1007", "1013"]);
    expect(transactionsRead(context)).toBe(23);
    expect(scan.cursor).toBe("2026-08-28T16:00:00.000Z");
    expect(scan.coversFrom).toBe("2026-08-04T16:00:00.000Z");
  });

  it("afterwards, reads again only what changed, and stops at the first page with nothing new", async () => {
    const { pages } = history();
    const memory = memoryInMap();
    const first = await vintedAdapter.scanSales(vinted(answering(pages), memory), { from: null, cursor: null }, seller);

    const quiet = vinted(answering(pages), memory);
    const again = await vintedAdapter.scanSales(quiet, { from: null, cursor: first.cursor }, seller);
    expect(transactionsRead(quiet)).toBe(0);
    expect(quiet.requested.filter((url) => url.includes("/my_orders"))).toHaveLength(1);
    expect(again.cancelledOrderIds).toEqual([]);
    expect(again.orders).toHaveLength(18);

    // The open sale completes: it alone is read again, now with its payout.
    pages[0]![0] = orderRow(1000, "2026-09-19T10:00:00-06:00", "8.5", "completed");
    const changed = vinted(answering(pages), memory);
    const completed = await vintedAdapter.scanSales(changed, { from: null, cursor: again.cursor }, seller);
    expect(transactionsRead(changed)).toBe(1);
    expect(completed.orders.find((order) => order.externalOrderId === "1000")?.payoutAmount).toBe(8.5);
  });

  it("reads every page for a history scan, keeping only sales since the day asked for", async () => {
    const { pages } = history();
    const context = vinted(answering(pages));

    const scan = await vintedAdapter.scanSales(context, { from: new Date("2026-08-20T00:00:00Z"), cursor: "2026-08-28T16:00:00.000Z" }, seller);

    expect(context.requested.filter((url) => url.includes("/my_orders"))).toHaveLength(2);
    expect(scan.orders.map((order) => order.externalOrderId)).toEqual(["1000", "1001", "1002", "1003", "1004", "1005", "1006", "1008"]);
    expect(scan.cancelledOrderIds).toEqual(["1007"]);
  });

  it("stops for a security check rather than reading past it", async () => {
    const context = vinted(() => ({ status: 403, url: "https://www.vinted.com/api/v2/my_orders", contentType: "application/json", body: '{"url":"https://geo.captcha-delivery.com/captcha/?initialCid=x"}' }));
    await expect(vintedAdapter.scanSales(context, { from: null, cursor: null }, seller)).rejects.toMatchObject({ kind: "Challenge" });
  });
});

describe("who is signed in to Vinted", () => {
  const current = { user: { id: 5550001, login: "test_seller" } };
  const page = (signedIn: boolean) => (url: URL): Answer =>
    url.pathname === "/my_orders" ? { status: 200, url: url.href, contentType: "text/html", body: vintedPage(signedIn) } : current;

  it("loads a page first, and asks which account only once the page shows a sign-in", async () => {
    const context = vinted(page(true));

    expect(await vintedAdapter.detectSession(context)).toEqual(seller);
    expect(context.opened).toEqual(["https://www.vinted.com/my_orders?order_type=sold"]);
    expect(context.requested).toEqual(["https://www.vinted.com/api/v2/users/current"]);
  });

  it("asks Vinted nothing when the page shows no one is signed in", async () => {
    // A question to the API without a session is what Vinted's bot protection blocks.
    const context = vinted(page(false));

    expect(await vintedAdapter.detectSession(context)).toEqual({ signedIn: false, username: null, marketplaceUserId: null });
    expect(context.requested).toEqual([]);
  });

  it("stops at a security check on the page", async () => {
    const context = vinted((url) => ({ status: 403, url: url.href, contentType: "text/html", body: "<script src=https://geo.captcha-delivery.com/c.js>" }));

    await expect(vintedAdapter.detectSession(context)).rejects.toMatchObject({ kind: "Challenge" });
    expect(context.requested).toEqual([]);
  });

  it("is read as signed out from the operator's own tab, without a request", () => {
    const signedOut: PageResponse = { status: 200, url: "https://www.vinted.com/", contentType: "text/html", body: vintedPage(false) };
    const signedIn: PageResponse = { ...signedOut, body: vintedPage(true) };

    expect(vintedAdapter.sessionFrom!(signedOut)).toEqual({ signedIn: false, username: null, marketplaceUserId: null });
    // A signed-in page does not name the account, so that is left to a full check.
    expect(vintedAdapter.sessionFrom!(signedIn)).toBeNull();
  });
});

describe("snapshotting Vinted listings", () => {
  const items = Array.from({ length: 109 }, (_, index) => wardrobeItem(9000 + index, index % 9 === 0 ? { is_closed: true, item_closing_action: "sold" } : {}));
  const answer = (total: number) => (url: URL): Answer => {
    const page = Number(url.searchParams.get("page"));
    return url.pathname === "/api/v2/wardrobe/5550001/items" ? wardrobe(items.slice((page - 1) * 96, page * 96), page, 2, total) : undefined;
  };

  it("reads every page of the wardrobe, and is complete when it has every item Vinted counts", async () => {
    const snapshot = await vintedAdapter.snapshotListings(vinted(answer(109)), seller);

    expect(snapshot.listings).toHaveLength(109);
    expect(snapshot.listings.filter((listing) => listing.status === "Sold")).toHaveLength(13);
    expect(snapshot.complete).toBe(true);
  });

  it("is not complete when Vinted counts more than it read", async () => {
    expect((await vintedAdapter.snapshotListings(vinted(answer(110)), seller)).complete).toBe(false);
  });
});

describe("Vinted's pages", () => {
  it("tells the sign-in form and the seller's own pages from the rest", () => {
    expect(vintedAdapter.pageKind("https://www.vinted.com/member/login")).toBe("sign-in");
    expect(vintedAdapter.pageKind("https://www.vinted.com/my_orders?order_type=sold")).toBe("seller");
    expect(vintedAdapter.pageKind("https://www.vinted.com/member/5550001")).toBe("seller");
    expect(vintedAdapter.pageKind("https://www.vinted.com/catalog?search_text=shirt")).toBe("other");
    expect(vintedAdapter.readable("https://www.vinted.com/")).toBe(true);
    expect(vintedAdapter.readable("https://vinted.com.evil.example/")).toBe(false);
  });
});

describe("Vinted's sign-in token", () => {
  const current = { user: { id: 5550001, login: "test_seller" } };
  const signedInPage = (url: URL): PageResponse => ({ status: 200, url: url.href, contentType: "text/html", body: vintedPage(true) });
  const unauthorized = (url: URL): PageResponse => ({ status: 401, url: url.href, contentType: "application/json", body: "{}" });

  it("is read from its cookie, without a request", () => {
    expect(tokenExpiry(live)).toBe(Math.floor((now.getTime() + 2 * 60 * 60_000) / 1000) * 1000);
    expect(tokenExpiry("not-a-token")).toBeNull();
  });

  it("asks which account with a live token, once", async () => {
    const context = vinted((url) => (url.pathname === "/my_orders" ? signedInPage(url) : current), memoryInMap(), () => live);

    expect(await vintedAdapter.detectSession(context)).toEqual(seller);
    expect(context.requested).toEqual(["https://www.vinted.com/api/v2/users/current"]);
  });

  it("is renewed the way Vinted renews it — a page loads — before anything is asked with it", async () => {
    let jar = lapsed;
    let loads = 0;
    const context = vinted((url) => {
      if (url.pathname === "/my_orders") {
        // The first load is the check's own; the second renews the token.
        if (++loads === 2) {
          jar = live;
        }

        return signedInPage(url);
      }

      return jar === live ? current : unauthorized(url);
    }, memoryInMap(), () => jar);

    expect(await vintedAdapter.detectSession(context)).toEqual(seller);
    expect(context.opened).toHaveLength(2);
    expect(context.requested).toEqual(["https://www.vinted.com/api/v2/users/current"]);
  });

  it("counts as signed out, asking nothing, when the page cannot renew it", async () => {
    const context = vinted((url) => (url.pathname === "/my_orders" ? signedInPage(url) : current), memoryInMap(), () => lapsed);

    await expect(vintedAdapter.detectSession(context)).rejects.toMatchObject({ kind: "SignedOut" });
    expect(context.requested).toEqual([]);
  });

  it("is never asked about again after a 401, when it looked live", async () => {
    const context = vinted((url) => (url.pathname === "/my_orders" ? signedInPage(url) : unauthorized(url)), memoryInMap(), () => live);

    await expect(vintedAdapter.scanSales(context, { from: null, cursor: null }, seller)).rejects.toMatchObject({ kind: "SignedOut" });
    expect(context.requested).toHaveLength(1);
    expect(context.opened).toEqual([]);
  });

  it("without a cookie to read, gets one more try after a 401 — once per scan, not once per request", async () => {
    let answered = 0;
    const { pages } = history();
    const scanning = answering(pages);
    const context = vinted((url) => {
      if (url.pathname === "/my_orders") {
        return signedInPage(url);
      }

      // The page was still renewing the sign-in: the first question is refused, the rest go through, until it lapses.
      answered++;
      return answered === 1 || answered > 5 ? unauthorized(url) : scanning(url);
    });

    await expect(vintedAdapter.scanSales(context, { from: null, cursor: null }, seller)).rejects.toMatchObject({ kind: "SignedOut" });
    expect(context.opened).toHaveLength(1);
    expect(context.requested).toHaveLength(6);
  });

  it("is used from the operator's own page without loading another, and not when that page does not show the sign-in", async () => {
    const context = vinted(() => current, memoryInMap(), () => live);
    const shown = (body: string): PageResponse => ({ status: 200, url: "https://www.vinted.com/items/1", contentType: "text/html", body });

    expect(await vintedAdapter.detectSession(context, shown(vintedPage(true)))).toEqual(seller);
    expect(context.opened).toEqual([]);
    await expect(vintedAdapter.detectSession(context, shown("<html></html>"))).rejects.toMatchObject({ kind: "Interrupted" });
    expect(context.requested).toHaveLength(1);
  });

  it("calls a loaded orders page that says nothing of the sign-in a changed site, rather than asking", async () => {
    const context = vinted((url) => ({ status: 200, url: url.href, contentType: "text/html", body: "<html></html>" }), memoryInMap(), () => live);

    await expect(vintedAdapter.detectSession(context)).rejects.toMatchObject({ kind: "SiteChanged" });
    expect(context.requested).toEqual([]);
  });
});

describe("a Vinted page as it stands", () => {
  const page = (status: number, body: string): PageResponse => ({ status, url: "https://www.vinted.com/", contentType: "text/html", body });

  it("tells a security check, a sign-in and a sign-out apart", () => {
    expect(vintedAdapter.look!(page(403, '<iframe src="https://geo.captcha-delivery.com/captcha/">'))).toEqual({ challenge: true, signedIn: null });
    expect(vintedAdapter.look!(page(200, vintedPage(true)))).toEqual({ challenge: false, signedIn: true });
    expect(vintedAdapter.look!(page(200, vintedPage(false)))).toEqual({ challenge: false, signedIn: false });
    expect(vintedAdapter.look!(page(200, "<html></html>"))).toEqual({ challenge: false, signedIn: null });
    // Cloudflare's check, which Vinted also sits behind — but not its script, which ordinary pages load as well.
    expect(vintedAdapter.look!(page(403, "<html><head><title>Just a moment...</title></head><script>window._cf_chl_opt={}</script>"))).toMatchObject({ challenge: true });
    expect(vintedAdapter.look!(page(200, `${vintedPage(true)}<script src="/cdn-cgi/challenge-platform/scripts/jsd/main.js"></script>`))).toEqual({ challenge: false, signedIn: true });
  });

  it("counts the steps of signing in as the sign-in form", () => {
    expect(vintedAdapter.pageKind("https://www.vinted.com/member/signup/select_type?ref_url=%2F")).toBe("sign-in");
    expect(vintedAdapter.pageKind("https://www.vinted.com/member/verification")).toBe("sign-in");
  });
});
