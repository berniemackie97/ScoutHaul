import { describe, expect, it } from "vitest";
import { findStatusConflicts, type ChannelListing } from "./status-conflicts";
import type { InventoryStatus } from "./inventory-operations";

const listing = (status: string, channelAccountName = "eBay – main") =>
  ({ status, channelAccountName }) as ChannelListing;

const kinds = (status: InventoryStatus | undefined, listings: readonly ChannelListing[] | undefined) =>
  findStatusConflicts(status, listings).map((conflict) => conflict.kind);

describe("findStatusConflicts", () => {
  it("says nothing when the item and its listings agree", () => {
    expect(kinds("Listed", [listing("Active")])).toEqual([]);
    expect(kinds("Sold", [listing("Sold")])).toEqual([]);
    expect(kinds("ReadyToList", [listing("NotListed")])).toEqual([]);
    expect(kinds("NeedsPhotos", [listing("Draft")])).toEqual([]);
  });

  it("says nothing when there are no listings at all", () => {
    expect(kinds("ReadyToList", [])).toEqual([]);
    expect(kinds("Sold", undefined)).toEqual([]);
  });

  it("flags an item that is sold here but still on sale there", () => {
    // The expensive one: the item is gone and a marketplace is still taking money for it.
    expect(kinds("Sold", [listing("Active")])).toContain("still-live-after-sale");
  });

  it("treats every stage after the sale the same way", () => {
    for (const status of ["Sold", "Packed", "Shipped", "Delivered"] as InventoryStatus[]) {
      expect(kinds(status, [listing("Active")])).toContain("still-live-after-sale");
    }
  });

  it("flags a marketplace sale that was never recorded here", () => {
    expect(kinds("Listed", [listing("Sold")])).toContain("sold-elsewhere");
    expect(kinds("ReadyToList", [listing("Sold")])).toContain("sold-elsewhere");
  });

  it("flags an item that is already live but still queued for listing", () => {
    // Why an item that is on eBay keeps coming back around as needing to be listed.
    expect(kinds("ReadyToList", [listing("Active")])).toContain("live-but-not-marked-listed");
    expect(kinds("NeedsPhotos", [listing("Active")])).toContain("live-but-not-marked-listed");
  });

  it("does not call a listed item unlisted", () => {
    expect(kinds("Listed", [listing("Active")])).not.toContain("live-but-not-marked-listed");
  });

  it("puts the costly conflict first", () => {
    const conflicts = findStatusConflicts("Sold", [listing("Active"), listing("Sold", "Poshmark")]);
    expect(conflicts[0]?.kind).toBe("still-live-after-sale");
    expect(conflicts[0]?.severity).toBe("danger");
  });

  it("names the marketplaces involved, without repeating one", () => {
    const [conflict] = findStatusConflicts("Sold", [
      listing("Active", "eBay – main"),
      listing("Active", "eBay – main"),
      listing("Active", "Poshmark"),
    ]);
    expect(conflict?.channels).toEqual(["eBay – main", "Poshmark"]);
  });

  it("still names a channel when the account has no name", () => {
    const [conflict] = findStatusConflicts("Sold", [
      { status: "Active", channel: "eBay" } as ChannelListing,
    ]);
    expect(conflict?.channels).toEqual(["eBay"]);
  });

  it("ignores listings that are neither live nor sold", () => {
    for (const status of ["NotListed", "Draft", "PendingDelist", "Delisted", "Error"]) {
      expect(kinds("ReadyToList", [listing(status)])).toEqual([]);
    }
  });

  it("reports both when an item is sold here, sold on one channel and live on another", () => {
    const found = kinds("Sold", [listing("Active", "eBay"), listing("Sold", "Poshmark")]);
    expect(found).toEqual(["still-live-after-sale"]);
  });
});
