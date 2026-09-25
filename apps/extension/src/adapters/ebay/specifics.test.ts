import { describe, expect, it } from "vitest";
import { itemPage } from "../../test/ebay-fixtures";
import { readSpecifics } from "./specifics";

describe("what an eBay listing says the thing is", () => {
  it("reads brand, colour and size from the item's structured data", () => {
    const read = readSpecifics(itemPage(["AAA111"], { Brand: "Nike", Color: "Black", Size: "L", Department: "Men" }));

    expect(read).toEqual({ brand: "Nike", color: "Black", size: "L" });
  });

  it("takes eBay's spelling of colour either way", () => {
    expect(readSpecifics(itemPage(["AAA111"], { Colour: "Navy" })).color).toBe("Navy");
  });

  it("falls back to a size a category names its own way", () => {
    expect(readSpecifics(itemPage(["AAA111"], { "US Shoe Size": "10.5" })).size).toBe("10.5");
  });

  it("says nothing when the listing states nothing", () => {
    expect(readSpecifics(itemPage(["AAA111"]))).toEqual({ brand: null, color: null, size: null });
    expect(readSpecifics("<html><body>no structured data</body></html>")).toEqual({ brand: null, color: null, size: null });
  });

  it("is not fooled by structured data that is not the product", () => {
    const page = `<script type="application/ld+json">{"@type":"BreadcrumbList","additionalProperty":[{"name":"Brand","value":"Wrong"}]}</script>`;

    expect(readSpecifics(page).brand).toBeNull();
  });
});
