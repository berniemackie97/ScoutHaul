import { describe, expect, it } from "vitest";
import { marketplaceOrigins } from "./origins";
import { adapters } from "./registry";

describe("the marketplaces this build can sync", () => {
  it("declares every adapter's pages in the manifest, or the browser refuses to grant access to them", () => {
    for (const adapter of adapters) {
      expect(marketplaceOrigins).toEqual(expect.arrayContaining(adapter.origins));
    }
  });
});
