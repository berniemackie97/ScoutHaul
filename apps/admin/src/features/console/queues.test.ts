import { existsSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { QUEUES, changeTone, describeChange, presentQueues, type ConsoleQueue } from "./queues";

const dashboard = path.resolve(__dirname, "../../app/(dashboard)");

function routeExists(href: string): boolean {
  const pathname = href.split("?")[0] ?? href;
  const segments = pathname === "/" ? [] : pathname.slice(1).split("/");
  return existsSync(path.join(dashboard, ...segments, "page.tsx"));
}

/**
 * The query string each link must carry — the filter the API counted the queue with. The API's
 * OperatorConsoleTests pin its count to the list's total for exactly these filters; this pins the
 * other half, so the two cannot drift apart silently.
 */
const EXPECTED_LINKS: Record<string, string> = {
  "orders.needsAttention": "/orders?stage=NeedsAttention",
  "offers.new": "/offers?status=New",
  "imports.unreconciled": "/imports/orders",
  "orders.toPack": "/orders?stage=ToPack",
  "orders.toShip": "/orders?stage=ToShip",
  "orders.readyForPickup": "/orders?stage=ReadyForPickup",
  "orders.awaitingPayment": "/orders?stage=AwaitingPayment",
  "listings.pendingDelist": "/channels/delist",
  "listings.soldNotRecorded": "/channels/sold",
  "inventory.readyToList": "/inventory?status=ReadyToList",
  "inventory.benchWork": "/repairs",
  "inventory.missingCost": "/inventory?attention=MissingCost",
  "inventory.missingPhotos": "/inventory?lifecycle=Active&attention=MissingPhotos",
  "inventory.missingLocation": "/inventory?lifecycle=Active&attention=MissingLocation",
  "inventory.aged": "/inventory?lifecycle=Active&attention=Aged",
  "purchases.unallocated": "/purchases",
};

const queue = (key: string, tier: string, count: number, amount?: number): ConsoleQueue => ({ key, tier, count, amount });

describe("queue links", () => {
  it("cover every queue the API reports", () => {
    expect(Object.keys(QUEUES).sort()).toEqual(Object.keys(EXPECTED_LINKS).sort());
  });

  it.each(Object.entries(EXPECTED_LINKS))("open %s with the filter it was counted with", (key, href) => {
    expect(QUEUES[key]?.href).toBe(href);
  });

  it.each(Object.entries(QUEUES))("lead %s to a page that exists", (_, presentation) => {
    expect(routeExists(presentation.href)).toBe(true);
  });
});

describe("presentQueues", () => {
  it("groups non-empty queues by how much ignoring them costs", () => {
    const { byTier } = presentQueues([
      queue("orders.needsAttention", "attention", 2),
      queue("orders.toPack", "work", 5),
      queue("inventory.missingCost", "hygiene", 3),
    ]);

    expect(byTier.attention.map((entry) => entry.key)).toEqual(["orders.needsAttention"]);
    expect(byTier.work.map((entry) => entry.key)).toEqual(["orders.toPack"]);
    expect(byTier.hygiene.map((entry) => entry.key)).toEqual(["inventory.missingCost"]);
  });

  it("reports empty work queues as clear rather than hiding them", () => {
    // "To ship: 0" is information — it means the job is done, not that the console forgot.
    const { byTier, clear } = presentQueues([queue("orders.toShip", "work", 0)]);
    expect(byTier.work).toEqual([]);
    expect(clear.map((entry) => entry.label)).toEqual(["To ship"]);
  });

  it("does not list clean data as an achievement", () => {
    const { clear } = presentQueues([queue("inventory.missingCost", "hygiene", 0)]);
    expect(clear).toEqual([]);
  });

  it("drops a queue the portal does not know rather than showing a raw key", () => {
    const { byTier, clear } = presentQueues([queue("orders.somethingNew", "work", 4)]);
    expect(byTier.work).toEqual([]);
    expect(clear).toEqual([]);
  });

  it("keeps the money behind a queue", () => {
    const { byTier } = presentQueues([queue("purchases.unallocated", "hygiene", 1, 120)]);
    expect(byTier.hygiene[0]).toMatchObject({ count: 1, amount: 120, href: "/purchases" });
  });

  it("copes with no queues at all — a caller who may see none of them", () => {
    const { byTier, clear } = presentQueues(undefined);
    expect([...byTier.attention, ...byTier.work, ...byTier.hygiene, ...clear]).toEqual([]);
  });
});

describe("describeChange", () => {
  it("shows a rounded percentage with a direction", () => {
    expect(describeChange({ current: 112, previous: 100, percentChange: 12.4 })).toBe("+12%");
    expect(describeChange({ current: 92, previous: 100, percentChange: -8 })).toBe("−8%");
  });

  it("calls something from nothing new", () => {
    expect(describeChange({ current: 50, previous: 0, percentChange: null })).toBe("new");
  });

  it("does not call nothing-then-nothing new", () => {
    expect(describeChange({ current: 0, previous: 0, percentChange: null })).toBe("—");
  });

  it("says so when nothing moved", () => {
    expect(describeChange({ current: 100, previous: 100, percentChange: 0.2 })).toBe("no change");
  });
});

describe("changeTone", () => {
  it("treats more sales as good news and more expenses as bad", () => {
    expect(changeTone({ current: 120, previous: 100, percentChange: 20 })).toBe("up");
    expect(changeTone({ current: 120, previous: 100, percentChange: 20 }, false)).toBe("down");
  });

  it("calls a sub-cent difference flat", () => {
    expect(changeTone({ current: 100.001, previous: 100, percentChange: 0 })).toBe("flat");
  });
});
