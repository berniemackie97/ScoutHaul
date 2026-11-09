import { describe, expect, it } from "vitest";
import { serializeJsonLd } from "./json-ld";

describe("serializeJsonLd", () => {
  it("keeps imported product text inside one JSON-LD script", () => {
    const title = '</ScRiPt><script id="injected">alert(1)</script>';
    const data = { "@context": "https://schema.org", "@type": "Product", name: title };
    const container = document.createElement("div");
    container.innerHTML = '<script type="application/ld+json">' + serializeJsonLd(data) + "</script>";

    expect(container.querySelectorAll("script")).toHaveLength(1);
    expect(container.querySelector("#injected")).toBeNull();
    expect(JSON.parse(container.querySelector("script")?.textContent ?? "")).toEqual(data);
  });

  it("escapes HTML-significant characters and JavaScript line separators without changing JSON values", () => {
    const data = { name: "<>&\u2028\u2029", offers: [{ price: 20.5, available: true }], description: null };
    const serialized = serializeJsonLd(data);

    expect(serialized).toContain("\\u003c\\u003e\\u0026\\u2028\\u2029");
    expect(serialized).not.toMatch(/[<>&\u2028\u2029]/);
    expect(JSON.parse(serialized)).toEqual(data);
  });
});
