import { describe, expect, it } from "vitest";
import { scanMatches } from "./pack-scan";

const item = { id: "3f2a91c4-0d4e-4b6a-9a51-6d7f0e2b1c99", barcode: "0 12345 67890 5", sku: "ITM-0399" };

describe("pack scan", () => {
  it("accepts the barcode, SKU or id however it is spaced or cased", () => {
    expect(scanMatches("012345678905", item)).toBe(true);
    expect(scanMatches("itm-0399", item)).toBe(true);
    expect(scanMatches("3F2A91C4", item)).toBe(true);
    expect(scanMatches(item.id, item)).toBe(true);
  });

  it("refuses another item's tag and anything too short to mean something", () => {
    expect(scanMatches("ITM-0400", item)).toBe(false);
    expect(scanMatches("3f2a", item)).toBe(false);
    expect(scanMatches("", item)).toBe(false);
  });
});
