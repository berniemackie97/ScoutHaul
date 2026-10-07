import type { components } from "@overhaul/api-client";

type Presence = components["schemas"]["InventoryChannelPresenceDto"];

/** Whole days since the item was acquired; never negative. */
export function daysHeld(acquiredAt: string | null | undefined, now = Date.now()): number | null {
  if (!acquiredAt) return null;
  const at = Date.parse(acquiredAt);
  return Number.isFinite(at) ? Math.max(0, Math.floor((now - at) / 86_400_000)) : null;
}

/** How a channel row reads in a list: price when live, a warning when it must come down. */
export function presenceLabel(channel: Presence): { text: string; tone: "default" | "warning" | "muted" } {
  const price = channel.price != null ? ` $${Number(channel.price).toFixed(0)}` : "";
  switch (channel.status) {
    case "Active":
      return { text: `${channel.platform}${price}`, tone: "default" };
    case "PendingDelist":
      return { text: `${channel.platform} · take down`, tone: "warning" };
    case "Sold":
      return { text: `Sold on ${channel.platform}`, tone: "default" };
    case "Error":
      return { text: `${channel.platform} · error`, tone: "warning" };
    default:
      return { text: `${channel.platform} · ${String(channel.status ?? "").toLowerCase()}`, tone: "muted" };
  }
}

type MergeCandidate = { thumbnailUrl: string | null; createdAt?: string; facts?: { channels?: Presence[] | null } };

/** The record a merge keeps unless told otherwise: the one with photos and the most listings, then the oldest. */
export function suggestKeeper<T extends MergeCandidate>(rows: readonly T[]): T | undefined {
  const weight = (row: T) => (row.thumbnailUrl ? 100 : 0) + (row.facts?.channels?.length ?? 0);
  return [...rows].sort((a, b) => weight(b) - weight(a) || (a.createdAt ?? "").localeCompare(b.createdAt ?? ""))[0];
}
