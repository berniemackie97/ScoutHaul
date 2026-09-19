import { describe, expect, it } from "vitest";
import { jsonAfter, spans } from "./embedded-json";

describe("reading JSON a page embeds", () => {
  it("finds the value after an anchor, however much surrounds it", () => {
    const page = `<script>fn({"a":1,"ordersData":{"list":[1,2,{"x":"}]{["}],"s":"a \\"quoted\\" }"},"b":2})</script>`;
    expect(jsonAfter(page, '"ordersData":')).toEqual({ list: [1, 2, { x: "}]{[" }], s: 'a "quoted" }' });
  });

  it("says nothing when the anchor is missing or the JSON is broken", () => {
    expect(jsonAfter("<html></html>", '"ordersData":')).toBeUndefined();
    expect(jsonAfter('"ordersData":{"unclosed":1', '"ordersData":')).toBeUndefined();
  });

  it("joins rich text the way the page shows it", () => {
    expect(spans({ textSpans: [{ text: "Sep 18" }, { text: "12:48am PDT" }] }, " | ")).toBe("Sep 18 | 12:48am PDT");
    expect(spans({ textSpans: [] })).toBeNull();
    expect(spans(undefined)).toBeNull();
  });
});
