import type { components } from "@overhaul/api-client";
import type { InventoryLifecycleGroup, InventoryStatus } from "@/features/inventory/inventory-operations";
import type { OrderStage } from "@/features/orders/order-stages";

export type OperatorConsole = components["schemas"]["OperatorConsoleDto"];
export type ConsoleQueue = components["schemas"]["ConsoleQueueDto"];
type InventoryAttention = components["schemas"]["InventoryAttention"];

export type QueueTier = "attention" | "work" | "hygiene";

/** How a queue is presented: what it is called, why it matters, and the list it opens. */
export type QueuePresentation = {
  label: string;
  /** One line on why this queue matters — shown under the count. */
  why: string;
  href: string;
};

/** Links into the inventory list, typed against the values that page accepts. */
function inventoryHref(filter: { status?: InventoryStatus; lifecycle?: InventoryLifecycleGroup; attention?: InventoryAttention }): string {
  const params = new URLSearchParams();
  if (filter.status) params.set("status", filter.status);
  if (filter.lifecycle) params.set("lifecycle", filter.lifecycle);
  if (filter.attention) params.set("attention", filter.attention);
  return `/inventory?${params.toString()}`;
}

const ordersHref = (stage: OrderStage) => `/orders?stage=${stage}`;

/**
 * Every queue the console can report, keyed as the API keys them.
 *
 * Each link opens its list with exactly the filter the API counted with — the API's console tests
 * pin that count to the list's total, and this module's tests pin the link. Change one side without
 * the other and a test fails, instead of a card that promises 12 and opens 9.
 */
export const QUEUES: Record<string, QueuePresentation> = {
  "orders.needsAttention": {
    label: "Orders needing attention",
    why: "A return in flight or a payment problem.",
    href: ordersHref("NeedsAttention"),
  },
  "offers.new": {
    label: "Offers awaiting your reply",
    why: "Buyers are waiting, and unanswered offers expire.",
    href: "/offers?status=New",
  },
  "imports.unreconciled": {
    label: "Marketplace orders not yet recorded",
    why: "Imported but not turned into sales — the items still read as available.",
    href: "/imports/orders",
  },
  "orders.toPack": { label: "To pack", why: "Paid and waiting to be packed.", href: ordersHref("ToPack") },
  "orders.toShip": { label: "To ship", why: "Packed and waiting on a label or a carrier.", href: ordersHref("ToShip") },
  "orders.readyForPickup": { label: "Ready for pickup", why: "Waiting for the buyer to collect.", href: ordersHref("ReadyForPickup") },
  "orders.awaitingPayment": { label: "Awaiting payment", why: "Ordered but not yet paid.", href: ordersHref("AwaitingPayment") },
  "listings.pendingDelist": {
    label: "Still for sale after selling",
    why: "Sold on one marketplace and still live on another, where it can be bought again.",
    href: "/channels/delist",
  },
  "listings.soldNotRecorded": {
    label: "Sold, but not on the books",
    why: "A marketplace says it sold and no sale here accounts for the money.",
    href: "/channels/sold",
  },
  "inventory.readyToList": {
    label: "Ready to list",
    why: "Prepared — only the listing is missing.",
    href: inventoryHref({ status: "ReadyToList" }),
  },
  "inventory.benchWork": { label: "On the repair bench", why: "Untested or not fully working.", href: "/repairs" },
  "inventory.missingCost": {
    label: "Items with no cost",
    why: "Profit on these is wrong until a cost is recorded.",
    href: inventoryHref({ attention: "MissingCost" }),
  },
  "inventory.missingPhotos": {
    label: "Stock without photos",
    why: "Cannot be listed until it is photographed.",
    href: inventoryHref({ lifecycle: "Active", attention: "MissingPhotos" }),
  },
  "inventory.missingLocation": {
    label: "Stock with no shelf location",
    why: "Somebody will have to hunt for it when it sells.",
    href: inventoryHref({ lifecycle: "Active", attention: "MissingLocation" }),
  },
  "inventory.aged": {
    label: "Bought over 60 days ago, not listed",
    why: "Money tied up in stock that is not on sale.",
    href: inventoryHref({ lifecycle: "Active", attention: "Aged" }),
  },
  "purchases.unallocated": {
    label: "Purchases not fully itemized",
    why: "Cost of goods is understated until the cost is on items.",
    href: "/purchases",
  },
};

export type PresentedQueue = ConsoleQueue & QueuePresentation & { tier: QueueTier };

/**
 * The console's queues, split into what to show and what is clear.
 *
 * A queue the portal has no presentation for is dropped rather than rendered with a raw key — the
 * API can add queues ahead of the portal without the home page showing "orders.somethingNew".
 */
export function presentQueues(queues: ConsoleQueue[] | null | undefined): {
  byTier: Record<QueueTier, PresentedQueue[]>;
  clear: PresentedQueue[];
} {
  const byTier: Record<QueueTier, PresentedQueue[]> = { attention: [], work: [], hygiene: [] };
  const clear: PresentedQueue[] = [];

  for (const queue of queues ?? []) {
    const presentation = QUEUES[queue.key ?? ""];
    const tier = queue.tier as QueueTier;
    if (!presentation || !(tier in byTier)) continue;

    const presented: PresentedQueue = { ...queue, ...presentation, tier };
    if ((queue.count ?? 0) > 0) byTier[tier].push(presented);
    else if (tier !== "hygiene") clear.push(presented);
  }

  return { byTier, clear };
}

type ReportChange = components["schemas"]["ReportChangeDto"];

/**
 * "+12%", "−8%", "new", "no change" or "—": a change a reader can take in at a glance.
 *
 * "new" only when there was nothing before and something now; nothing both times is not news.
 */
export function describeChange(change: ReportChange | null | undefined): string {
  const current = change?.current ?? 0;
  const previous = change?.previous ?? 0;
  if (previous === 0) return current === 0 ? "—" : "new";

  const percent = change?.percentChange;
  if (percent === null || percent === undefined) return "—";
  const rounded = Math.round(percent);
  if (rounded === 0) return "no change";
  return `${rounded > 0 ? "+" : "−"}${Math.abs(rounded)}%`;
}

/** Whether a change is good news. Expenses going up is not. */
export function changeTone(change: ReportChange | null | undefined, higherIsBetter = true): "up" | "down" | "flat" {
  const delta = (change?.current ?? 0) - (change?.previous ?? 0);
  if (Math.abs(delta) < 0.005) return "flat";
  return delta > 0 === higherIsBetter ? "up" : "down";
}
