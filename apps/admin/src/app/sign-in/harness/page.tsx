// TEMPORARY smoke-test harness. Never commit.
/* eslint-disable @typescript-eslint/no-explicit-any */
import { WorkInbox } from "../../(dashboard)/work/work-inbox";
import { PackStation } from "../../(dashboard)/work/pack/pack-station";
import { SyncIssues } from "../../(dashboard)/sync/sync-issues";

const now = new Date().toISOString();
const ago = (h: number) => new Date(Date.now() - h * 3600_000).toISOString();
const steps = (n: number) => ["Sold on eBay", "Paid", "Packed", "Shipped", "Delivered"].map((label, i) => ({ label, at: i < n ? ago(10 - i) : null, state: i < n ? "Done" : i === n ? "Now" : "Later" }));
const inbox: any = {
  openCount: 5, lateCount: 1, inTransit: 7, generatedAt: now,
  waiting: [{ key: "order:x:pack", wait: "Snoozed", until: ago(-24), note: "Box", title: "Pack — Lamp" }],
  lanes: [
    { key: "reconcile", label: "Reconcile", hint: "Marketplace facts", tasks: [
      { key: "sold:1", lane: "reconcile", kind: "Sold, not recorded", title: "Record sale — Levi's Tee", detail: "eBay · $48.00", why: "eBay says it sold.", actionLabel: "Record sale", actionHref: "/channels/sold", since: ago(2), dueBy: ago(-22), isLate: false, steps: [], count: 1 },
      { key: "listings:match", lane: "reconcile", kind: "Unmatched listings", title: "Match 3 imported listings", detail: "Ranked suggestions are ready.", why: "No item, no books.", actionLabel: "Review matches", actionHref: "/imports/listings", since: null, dueBy: null, isLate: false, steps: [], count: 3 } ] },
    { key: "list", label: "List", hint: "Ready items", tasks: [] },
    { key: "pack", label: "Pack", hint: "Paid, not packed", tasks: [
      { key: "order:a:pack", lane: "pack", kind: "To pack", title: "Pack — Sony WH-1000XM4", detail: "eBay · 14-0091 · @buyer", why: "Paid. Pack it.", actionLabel: "Start packing", actionHref: "/work/pack?order=a", since: ago(80), dueBy: ago(32), isLate: true, steps: steps(2), count: 1 } ] },
    { key: "ship", label: "Ship", hint: "Packed", tasks: [] },
    { key: "follow", label: "Follow up", hint: "Loose ends", tasks: [] },
  ],
};
const lines: any = [
  { lineId: "l1", itemId: "3f2a91c4-0d4e-4b6a-9a51-6d7f0e2b1c99", title: "Madewell Perfect Vintage Jean", sku: "ITM-0399", barcode: "012345678905", bin: "B-02", condition: "Good", flaws: "Light fading at hem", included: null, quantity: 1, photoUrl: null },
];
const groups: any = [
  { platform: "Poshmark", code: "FIELD_MISSING", title: "Detail missing from a record", fix: "Retry reads the record's own page.", severity: "Warning", count: 2, latestAt: now,
    issues: [{ id: "i1", runId: "r", accountName: "Posh closet", loop: "Sales", severity: "Warning", externalReference: "PM-5521", message: "Ship-to was empty.", step: "Sales scan", attempts: 2, detail: "selector null", hasSnapshot: true, fix: "Retry reads the record's own page.", note: null, createdAt: now },
             { id: "i2", runId: "r", accountName: "Posh closet", loop: "Sales", severity: "Warning", externalReference: "PM-5530", message: "Ship-to was empty.", step: "Sales scan", attempts: 1, detail: null, hasSnapshot: false, fix: null, note: null, createdAt: now }] },
];

export default function Harness() {
  return (
    <div className="mx-auto max-w-7xl space-y-12 p-6">
      <section id="work"><WorkInbox inbox={inbox} /></section>
      <section id="pack"><PackStation order={{ id: "a", number: "PM-5521", channel: "Poshmark", buyer: "@denimhunter", shipTo: "Austin, TX", note: null, localPickup: false, total: 38 }} lines={lines} run={{ position: 1, total: 2, nextOrderId: "b" }} /></section>
      <section id="issues"><SyncIssues groups={groups} /></section>
    </div>
  );
}
