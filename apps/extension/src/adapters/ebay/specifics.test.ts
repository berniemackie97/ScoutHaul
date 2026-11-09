import { describe, expect, it } from "vitest";
import { itemPage } from "../../test/ebay-fixtures";
import { descriptionUrl, readDescription, readSpecifics } from "./specifics";

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

describe("an eBay listing's description", () => {
  it("finds the document the item page links to", () => {
    const page = itemPage(["AAA111"], {}, "https://vi.vipr.ebaydesc.com/itmdesc?item=377296454991&amp;t=0");

    expect(descriptionUrl(page)).toBe("https://vi.vipr.ebaydesc.com/itmdesc?item=377296454991&t=0");
    expect(descriptionUrl(itemPage(["AAA111"]))).toBeNull();
  });

  it("rejects description links to unrelated and lookalike hosts", () => {
    expect(descriptionUrl(`<iframe id="desc_ifr" src="https://attacker.example/collect"></iframe>`)).toBeNull();
    expect(descriptionUrl(`<iframe id="desc_ifr" src="https://vi.vipr.ebaydesc.com.attacker.example/"></iframe>`)).toBeNull();
    expect(descriptionUrl(`<iframe id="desc_ifr" src="https://user@vi.vipr.ebaydesc.com/"></iframe>`)).toBeNull();
    expect(descriptionUrl(`<iframe id="desc_ifr" src="https://attacker.example/"></iframe><iframe src="https://vi.vipr.ebaydesc.com/itmdesc"></iframe>`))
      .toBe("https://vi.vipr.ebaydesc.com/itmdesc");
  });

  it("keeps the words and drops the markup", () => {
    const document = `<html><head><style>.x{color:red}</style></head><body>
      <script>trackView()</script>
      <div><h2>Reshiram &amp; Zekrom GX</h2><p>Near mint.<br>Sleeved &amp; shipped in a top loader.</p></div>
      <ul><li>Set: Cosmic Eclipse</li><li>Card&nbsp;number: 157/236</li></ul>
    </body></html>`;

    expect(readDescription(document)).toBe(
      "Reshiram & Zekrom GX\nNear mint.\nSleeved & shipped in a top loader.\nSet: Cosmic Eclipse\nCard number: 157/236"
    );
  });

  it("treats a description of nothing but markup as no description", () => {
    expect(readDescription("<div><span>   </span><br></div>")).toBeNull();
  });

  it("keeps a runaway description to a sane length", () => {
    expect(readDescription(`<p>${"word ".repeat(2000)}</p>`, 100)).toHaveLength(100);
  });
});
