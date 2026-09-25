import { describe, expect, it } from "vitest";
import { fulfillment as ebayFulfillment, type EbayOrderRow } from "./ebay/orders";
import { fulfillment as vintedFulfillment } from "./vinted/orders";

/**
 * Every phrase here was taken from the account's own orders, not invented. A marketplace's wording is the one part
 * of this that nobody controls, so the mapping is checked against what they actually said.
 */
describe("how far an order has got", () => {
  const now = new Date("2026-09-25T20:00:00Z");

  const ebayRow = (status: string | null, subStatus: string | null = null): EbayOrderRow => ({
    orderId: "26-15152-72360",
    created: null,
    status,
    subStatus,
    soldFor: null,
    shipping: null,
    total: null,
    buyerUsername: null,
    buyerName: null,
    street1: null,
    street2: null,
    city: null,
    region: null,
    postalCode: null,
    lines: [],
  });

  it("reads eBay's delivered date out of the words it delivers it in", () => {
    const read = ebayFulfillment(ebayRow("Delivered Sep 22"), now);

    expect(read.stage).toBe("Delivered");
    expect(read.deliveredAt).toBe("2026-09-22T12:00:00.000Z");
    expect(read.detail).toBe("Delivered Sep 22");
  });

  it("takes eBay's shipped date from the carrier's first scan", () => {
    const read = ebayFulfillment(ebayRow("Shipped", "Carrier scan Sep 24"), now);

    expect(read.stage).toBe("Shipped");
    expect(read.shippedAt).toBe("2026-09-24T12:00:00.000Z");
    expect(read.detail).toBe("Shipped — Carrier scan Sep 24");
  });

  it("reads a December day as last year's when the year would otherwise be ahead of now", () => {
    expect(ebayFulfillment(ebayRow("Delivered Dec 28"), now).deliveredAt).toBe("2025-12-28T12:00:00.000Z");
  });

  it("says nothing about a phrase it does not know", () => {
    const read = ebayFulfillment(ebayRow("Something eBay has not said before"), now);

    expect(read.stage).toBe("Unknown");
    expect(read.deliveredAt).toBeNull();
    expect(read.shippedAt).toBeNull();
  });

  it("reads Vinted's parcel status, and its order status when there is no parcel yet", () => {
    expect(vintedFulfillment({ status: "Order completed successfully. The buyer accepted the item." }, "Package delivered.").stage)
      .toBe("Delivered");
    expect(vintedFulfillment({ status: "Payment successful!" }, null))
      .toEqual({ stage: "NeedsShipping", detail: "Payment successful!" });
    expect(vintedFulfillment({}, "Parcel is on its way.").stage).toBe("Shipped");
    expect(vintedFulfillment({}, null).stage).toBe("Unknown");
  });
});
