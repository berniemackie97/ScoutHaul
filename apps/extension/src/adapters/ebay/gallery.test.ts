import { describe, expect, it } from "vitest";
import { itemPage } from "../../test/ebay-fixtures";
import { fullSize, readGallery } from "./gallery";

describe("eBay galleries", () => {
  it("reads a listing's own pictures and nothing else on the page", () => {
    // The page also carries the seller's avatar and a recommended listing. Neither belongs to this listing.
    const photos = readGallery(itemPage(["AAA111", "BBB222", "CCC333"]));

    expect(photos).toEqual([
      "https://i.ebayimg.com/images/g/AAA111/s-l1600.jpg",
      "https://i.ebayimg.com/images/g/BBB222/s-l1600.jpg",
      "https://i.ebayimg.com/images/g/CCC333/s-l1600.jpg",
    ]);
  });

  it("keeps the order the listing shows and never repeats a picture", () => {
    const html = itemPage(["AAA111", "BBB222"]) + itemPage(["BBB222"]);

    expect(readGallery(html)).toEqual([
      "https://i.ebayimg.com/images/g/AAA111/s-l1600.jpg",
      "https://i.ebayimg.com/images/g/BBB222/s-l1600.jpg",
    ]);
  });

  it("asks for every picture at the size worth keeping", () => {
    const html = `<button data-zoom-src=https://i.ebayimg.com/images/g/AAA111/s-l500.jpg></button>`;

    expect(readGallery(html)).toEqual(["https://i.ebayimg.com/images/g/AAA111/s-l1600.jpg"]);
    expect(fullSize("https://i.ebayimg.com/images/g/AAA111/s-l140.webp")).toBe("https://i.ebayimg.com/images/g/AAA111/s-l1600.webp");
    expect(fullSize("https://example.com/photo.jpg")).toBe("https://example.com/photo.jpg");
  });

  it("finds nothing on a page that carries no gallery", () => {
    expect(readGallery("<html><body>Pardon our interruption</body></html>")).toEqual([]);
  });

  it("ignores an image host that is not eBay's", () => {
    const html = `<button data-zoom-src=https://i.evil.example/images/g/AAA111/s-l1600.jpg></button>`;

    expect(readGallery(html)).toEqual([]);
  });
});
