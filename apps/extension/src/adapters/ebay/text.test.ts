import { describe, expect, it } from "vitest";
import { money, orderNumber, saleTime } from "./text";

describe("eBay's printed money", () => {
  it("reads amounts as eBay prints them", () => {
    expect(money("$20.00")).toBe(20);
    expect(money("+$6.99 | shipping")).toBe(6.99);
    expect(money("-$0.52")).toBe(-0.52);
    expect(money("$1,234.5")).toBe(1234.5);
    expect(money("Free shipping")).toBe(0);
  });

  it("refuses anything it cannot read, rather than guessing", () => {
    expect(money("")).toBeNull();
    expect(money("20")).toBeNull();
    expect(money("£20.00")).toBeNull();
    expect(money(null)).toBeNull();
  });
});

describe("eBay's sale times", () => {
  const now = new Date("2026-09-18T20:00:00Z");

  it("reads the time in the zone eBay printed", () => {
    expect(saleTime("Sep 18 | 12:48am PDT", now)?.toISOString()).toBe("2026-09-18T07:48:00.000Z");
    expect(saleTime("Sep 6 | 10:16pm PDT", now)?.toISOString()).toBe("2026-09-07T05:16:00.000Z");
    expect(saleTime("Jan 2 | 12:05pm PST", new Date("2026-02-01T00:00:00Z"))?.toISOString()).toBe("2026-01-02T20:05:00.000Z");
  });

  it("puts a date that would be in the future in the year before", () => {
    expect(saleTime("Dec 30 | 9:00am PST", new Date("2026-01-05T00:00:00Z"))?.toISOString()).toBe("2025-12-30T17:00:00.000Z");
  });

  it("honours a year when eBay prints one", () => {
    expect(saleTime("Dec 30, 2024 | 9:00am PST", now)?.toISOString()).toBe("2024-12-30T17:00:00.000Z");
  });

  it("refuses a bare date, and a zone it does not know", () => {
    expect(saleTime("Sep 18", now)).toBeNull();
    expect(saleTime("Sep 18 | 12:48am XYZ", now)).toBeNull();
  });

  it("finds order numbers in ledger descriptions", () => {
    expect(orderNumber("Shipping label for order no. 26-15152-72360")).toBe("26-15152-72360");
    expect(orderNumber("Store (Basic): Subscription Fee")).toBeNull();
  });
});
