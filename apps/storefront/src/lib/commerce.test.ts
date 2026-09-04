import { describe, expect, it } from "vitest";
import { imageUrl, money } from "./commerce";
import { STORE } from "./store";

describe("imageUrl", () => {
  it("puts the API in front of a path the API served", () => {
    expect(imageUrl("/uploads/lamp.jpg")).toBe(`${STORE.apiBaseUrl}/uploads/lamp.jpg`);
  });

  it("leaves an absolute URL alone", () => {
    // The reason this helper exists. Concatenating here would produce
    // "https://api.example.comhttps://cdn.example.com/lamp.jpg" and a broken image.
    expect(imageUrl("https://cdn.example.com/lamp.jpg")).toBe("https://cdn.example.com/lamp.jpg");
    expect(imageUrl("http://cdn.example.com/lamp.jpg")).toBe("http://cdn.example.com/lamp.jpg");
  });

  it("has nothing to show for nothing", () => {
    expect(imageUrl(null)).toBeUndefined();
    expect(imageUrl(undefined)).toBeUndefined();
    expect(imageUrl("")).toBeUndefined();
  });
});

describe("money", () => {
  it("formats a price the way a shopper expects to read it", () => {
    expect(money(1234.5)).toBe("$1,234.50");
    expect(money(0)).toBe("$0.00");
  });

  it("accepts the string the API sends for a decimal", () => {
    expect(money("42.75")).toBe("$42.75");
  });

  it("shows nothing priced as free rather than NaN", () => {
    expect(money(null)).toBe("$0.00");
    expect(money(undefined)).toBe("$0.00");
  });
});
