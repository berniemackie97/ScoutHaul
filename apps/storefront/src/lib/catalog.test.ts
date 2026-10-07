import { beforeEach, describe, expect, it, vi } from "vitest";

const get = vi.fn();

vi.mock("./store", async () => {
  const actual = await vi.importActual<typeof import("./store")>("./store");
  return { ...actual, liveCatalogEnabled: true, storefrontApi: () => ({ GET: get }) };
});

import { CatalogUnavailableError, categorySlug, getListing, getListings, getRelated, getShelfCount } from "./catalog";
import { STORE } from "./store";

beforeEach(() => vi.clearAllMocks());

/** Asserts the catalog returned something and hands back the first item, narrowed. */
function first<T>(items: T[]): T {
  const [item] = items;
  if (!item) throw new Error("Expected the catalog to return at least one item.");
  return item;
}

describe("getListings", () => {
  it("maps what the API sends into a catalog item", async () => {
    get.mockResolvedValue({
      data: {
        items: [
          {
            id: "1",
            slug: "brass-lamp",
            title: "Brass Lamp",
            brand: "Unknown",
            category: "Lighting",
            condition: "Excellent",
            listPrice: 120,
            imageUrl: "/uploads/lamp.jpg",
          },
        ],
      },
    });

    const item = first((await getListings()).items);
    expect(item.title).toBe("Brass Lamp");
    expect(item.price).toBe(120);
    expect(item.image).toBe(`${STORE.apiBaseUrl}/uploads/lamp.jpg`);
  });

  it("does not double-prefix an image the API already gave in full", async () => {
    // This used to be `STORE.apiBaseUrl + dto.imageUrl`, which would have produced a broken URL the
    // moment images moved to a CDN — while the cart, which used the helper, kept working.
    get.mockResolvedValue({
      data: { items: [{ id: "1", imageUrl: "https://cdn.example.com/lamp.jpg" }] },
    });

    const item = first((await getListings()).items);
    expect(item.image).toBe("https://cdn.example.com/lamp.jpg");
  });

  it("fills in defaults rather than rendering undefined at a shopper", async () => {
    get.mockResolvedValue({ data: { items: [{ id: "1" }] } });

    const item = first((await getListings()).items);
    expect(item.price).toBe(0);
    expect(item.condition).toBe("Good");
    expect(item.acceptsOffers).toBe(true);
    expect(item.image).toBeUndefined();
  });

  it("says the catalog is unreachable rather than showing an empty shop", async () => {
    // This used to resolve to []. A backend blip turned a full shop into an empty one with nothing
    // saying a request had failed.
    get.mockRejectedValue(new Error("network down"));
    await expect(getListings()).rejects.toBeInstanceOf(CatalogUnavailableError);
  });

  it("still reports a genuinely empty catalog as empty", async () => {
    get.mockResolvedValue({ data: { items: [], total: 0 } });
    await expect(getListings()).resolves.toEqual({ items: [], page: 1, pageCount: 1, total: 0 });
  });

  it("reaches every page of a shelf bigger than one page", async () => {
    // The catalogue used to ask for page 1 and stop, so everything past the first 48 items was unreachable.
    get.mockResolvedValue({ data: { items: [{ id: "49" }], total: 147 } });
    const result = await getListings({ page: 2 });

    const [, options] = get.mock.calls[0] ?? [];
    expect(options?.params?.query).toMatchObject({ page: 2, pageSize: 48 });
    expect(result).toMatchObject({ page: 2, pageCount: 4, total: 147 });
  });

  it("passes the shopper's category and search through", async () => {
    get.mockResolvedValue({ data: { items: [] } });
    await getListings({ category: "Lighting", search: "brass" });

    const [, options] = get.mock.calls[0] ?? [];
    expect(options?.params?.query).toMatchObject({ category: "Lighting", search: "brass" });
  });
});

describe("getShelfCount", () => {
  it("reports the real number of items on the floor", async () => {
    get.mockResolvedValue({ data: { total: 37 } });
    await expect(getShelfCount()).resolves.toBe(37);
  });

  it("says nothing rather than a made-up number when the API is unreachable", async () => {
    get.mockRejectedValue(new Error("network down"));
    await expect(getShelfCount()).resolves.toBeNull();
  });
});

describe("getListing", () => {
  it("returns null for a slug the store does not have", async () => {
    get.mockResolvedValue({ data: undefined });
    await expect(getListing("nope")).resolves.toBeNull();
  });

  it("does not turn an unreachable catalog into a missing product", async () => {
    // The page calls notFound() on null, so returning null here made a real, in-stock,
    // one-of-a-kind piece vanish exactly as if someone had just bought it.
    get.mockRejectedValue(new Error("network down"));
    await expect(getListing("brass-lamp")).rejects.toBeInstanceOf(CatalogUnavailableError);
  });

  it("keeps the underlying failure for logging", async () => {
    const cause = new Error("ECONNREFUSED");
    get.mockRejectedValue(cause);

    const error = await getListing("brass-lamp").catch((e: unknown) => e);
    expect((error as CatalogUnavailableError).cause).toBe(cause);
  });

  it("keeps every gallery image absolute", async () => {
    get.mockResolvedValue({
      data: {
        id: "1",
        slug: "brass-lamp",
        imageUrls: ["/uploads/a.jpg", "https://cdn.example.com/b.jpg"],
      },
    });

    const listing = await getListing("brass-lamp");
    expect(listing?.images).toEqual([
      `${STORE.apiBaseUrl}/uploads/a.jpg`,
      "https://cdn.example.com/b.jpg",
    ]);
  });
});

describe("the shelf count", () => {
  it("says nothing rather than taking the home page down with it", async () => {
    // Deliberately different from the reads above: the count is a flourish, and null already means
    // "say nothing". Throwing here would blank the home page over a decorative number.
    get.mockRejectedValue(new Error("network down"));
    await expect(getShelfCount()).resolves.toBeNull();
  });
});

describe("getRelated", () => {
  it("asks for the item's own category rather than searching one page of the shelf", async () => {
    get.mockResolvedValue({ data: { items: [{ id: "1", slug: "this-one" }, { id: "2", slug: "another" }], total: 2 } });

    const related = await getRelated({ slug: "this-one", category: "Health & Beauty" });

    const [, options] = get.mock.calls[0] ?? [];
    expect(options?.params?.query).toMatchObject({ category: "health-beauty" });
    expect(related.map((item) => item.slug)).toEqual(["another"]);
  });
});

describe("categorySlug", () => {
  it("joins letters and digits with single hyphens, like the API", () => {
    expect(categorySlug("Health & Beauty")).toBe("health-beauty");
    expect(categorySlug(" Café / Décor ")).toBe("café-décor");
  });
});
