import type { components } from "@overhaul/api-client";
import { isSoldStatus, type InventoryStatus } from "./inventory-operations";

/** Exported so callers and tests name the shape rather than deriving it from this signature. */
export type ChannelListing = components["schemas"]["ChannelListingDto"];

/**
 * An item's own status and its marketplace listings disagreeing.
 *
 * They are written by different things and neither checks the other: the status control PATCHes
 * /api/inventory/{id}/status, while the channel panel PUTs /api/inventory/{itemId}/channel-listings
 * and never touches InventoryStatus at all. So the two drift, and the console showed both side by
 * side without ever saying they contradicted each other.
 *
 * Detecting it here does not unify the writers — that belongs in the API — but it stops the operator
 * finding out from a buyer.
 */
export type StatusConflict = {
  kind: "still-live-after-sale" | "sold-elsewhere" | "live-but-not-marked-listed";
  severity: "danger" | "warning";
  title: string;
  detail: string;
  /** Which marketplaces the conflict involves, for naming them in the warning. */
  channels: string[];
};

/** Statuses that mean the item has not been listed anywhere yet. */
const PRE_LISTING: InventoryStatus[] = [
  "Draft",
  "Researching",
  "Watchlist",
  "Purchased",
  "NeedsCleaning",
  "NeedsTesting",
  "NeedsRepair",
  "NeedsPhotos",
  "ReadyToList",
];

const nameOf = (listing: ChannelListing) =>
  listing.channelAccountName ?? listing.channel ?? "a marketplace";

const namesOf = (listings: ChannelListing[]) => [...new Set(listings.map(nameOf))];

/**
 * Every way this item's status and its listings contradict each other, worst first.
 */
export function findStatusConflicts(
  status: InventoryStatus | undefined,
  listings: readonly ChannelListing[] | undefined,
): StatusConflict[] {
  if (!status || !listings?.length) return [];

  const active = listings.filter((listing) => listing.status === "Active");
  const soldElsewhere = listings.filter((listing) => listing.status === "Sold");
  const conflicts: StatusConflict[] = [];

  // The dangerous one: the item is gone, and a marketplace is still taking money for it.
  if (isSoldStatus(status) && active.length > 0) {
    conflicts.push({
      kind: "still-live-after-sale",
      severity: "danger",
      title: "Still for sale after being sold",
      detail:
        "This item is marked sold here but its listing is still live, so someone else can buy it. End the listing.",
      channels: namesOf(active),
    });
  }

  // A marketplace took the money and nothing here recorded it.
  if (soldElsewhere.length > 0 && !isSoldStatus(status)) {
    conflicts.push({
      kind: "sold-elsewhere",
      severity: "danger",
      title: "Sold on a marketplace, not recorded here",
      detail:
        "A listing for this item is marked sold, but the item is not. Record the sale so the money and the inventory match.",
      channels: namesOf(soldElsewhere),
    });
  }

  // Less costly, but it is why an item that is already live keeps showing up as needing listing.
  if (active.length > 0 && PRE_LISTING.includes(status)) {
    conflicts.push({
      kind: "live-but-not-marked-listed",
      severity: "warning",
      title: "Already listed, but not marked as listed",
      detail:
        "This item is live on a marketplace while its status still says it needs listing, so it will keep appearing in the listing queue.",
      channels: namesOf(active),
    });
  }

  return conflicts;
}
