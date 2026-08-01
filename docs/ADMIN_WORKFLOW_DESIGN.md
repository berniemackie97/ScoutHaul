# Admin workflow design — the operating spine

Status: **proposal, awaiting sign-off.** Nothing here is built yet.

Goal: an employee opens the admin, sees exactly what needs doing, and is walked through each job so
the data comes out complete and correct. Not "here are twelve entity browsers, figure it out."

---

## 1. What's actually wrong today (audited, not assumed)

**a) An order has three status fields and no single answer to "what do I do with this?"**

`Status` (Pending/Paid/Fulfilling/Shipped/Delivered/Cancelled/Refunded), `PaymentStatus`
(Pending/Paid/PartiallyRefunded/Refunded/Failed) and `FulfillmentStatus`
(Unfulfilled/Partial/Fulfilled) are three parallel fields that can disagree. Each is individually
correct; together they don't tell an employee what the next action is.

**b) Orders have no state machine.** Inventory has `InventoryLifecycle` + `InventoryStatusRules`
(rank, groups, legal statuses). Sales have `SaleWorkflow`. Orders have **neither** — and
`OrdersWorkflowService` sits in `HaulScout.Api/Services/`, not the Application layer, so order rules
never got domain treatment. Consequence: transition rules are ad-hoc and per-caller. The mobile app
happily offered "mark delivered" on an unshipped, unpaid order, because nothing centrally said no.
(The backend does enforce two rules — delivered implies shipped, carrier and tracking must arrive
together — which proves the rules belong somewhere real.)

**c) Nothing is required at the moment it's needed.** You can mark an order shipped with no carrier,
no tracking, no weight. The data needed to print anything (or to answer "where is it?") is optional
forever, so it's captured late or never.

**d) The IA is entity-first, and work is not.** Six nav groups, twelve destinations, organised by
*noun* (Inventory, Orders, Customers, Channels…). An employee's question is a *verb*: "what do I
pack today?" That answer currently requires knowing which noun hides it.

---

## 2. The design principle

> **The stage is derived from facts. Entering a stage requires what the stage needs. Being in a stage
> unlocks what the stage does.**

Derived — not a fourth status column. A stored "workflow state" would be a second source of truth
that drifts from payment/fulfillment reality the first time an import or a webhook lands. This is the
same state-based convergence rule already used for refunds and the storefront shelf: compute from
facts, so re-running always heals.

---

## 3. The fulfillment spine

```
Awaiting payment → To pick → To pack → To ship → In transit → Delivered
                                  ↘ Needs attention (return / refund / problem) ↙
```

| Stage | Derived from | Entry gate (what it demands) | Actions | Documents |
|---|---|---|---|---|
| **Awaiting payment** | `PaymentStatus = Pending` | — | Record payment · Cancel | — |
| **To pick** | Paid, lines not all picked | Payment recorded | Tick each line picked | **Picklist** (storage location per line) |
| **To pack** | All lines picked, not packed | All lines picked | Record weight + dimensions + package type | **Packing slip**, **thank-you insert**, **coupon** |
| **To ship** | Packed, no tracking | Weight + dims present | Enter carrier + tracking → marks shipped | **Address label**, **receipt** |
| **In transit** | Shipped, not delivered | Carrier + tracking | Mark delivered · flag stalled | Tracking link |
| **Delivered** | `DeliveredAt` set | — | Open return | **Receipt/invoice** |
| **Needs attention** | Open return or refund | — | RMA flow (existing) | **RMA slip** |

The gates are the whole point: **you cannot reach "shipped" without the tracking number**, because
the transition is what asks for it. That's the "require all that info when it enters shipping state"
you described — enforced in the domain, not nagged in the UI.

### New data this needs (additive only)
- `Order`: `PackedAt`, `PackageWeightOz`, `PackageLength/Width/Height`, `PackageType`
- `OrderLine`: `PickedAt`

### New domain rules
- `OrderWorkflow` in `HaulScout.Domain` — the stage derivation + legal transitions + entry
  requirements, mirroring `InventoryLifecycle`.
- Move `OrdersWorkflowService` from `Api/Services` into the Application layer where every other
  workflow service lives.

---

## 4. Documents & printing

Every document is generated from data you already own, as HTML → print/PDF. No new dependency.

| Document | Stage | Customisable |
|---|---|---|
| Picklist | To pick | layout only |
| Packing slip | To pack | **yes** — logo, message, fields shown |
| Thank-you insert | To pack | **yes** — full template |
| Coupon | To pack | **yes** — code, value, expiry, message |
| Address label (4×6) | To ship | **yes** |
| Receipt / invoice | Delivered (or any time after paid) | **yes** |
| RMA slip | Needs attention | yes |

Templates live in **Settings → Documents**, edited with a live preview, stored per organisation.
Print happens from the order's stage panel — the thing you're doing is right where you're doing it.

### The one hard constraint — read this before expecting postage
A **carrier shipping label with real postage** (the barcode that makes it mailable) cannot be
invented, customised, or reprinted by us. That barcode encodes a paid postage transaction issued by
USPS/UPS/FedEx. Two honest options:

1. **Today, no cost:** you buy postage where you already do; we capture carrier + tracking at the
   ship gate. We print the *branded* documents (packing slip, thank-you, coupon, a plain address
   label). Nothing about tracking or barcodes is faked.
2. **Later, paid:** a carrier account (EasyPost/Shippo, pay-per-label) lets us buy and print real
   postage inside the app, with live tracking status. This is the only way to get true label
   printing and automatic delivery updates.

Everything in this document works under option 1. Option 2 is an upgrade, not a prerequisite.

---

## 5. Information architecture

Work first; entity browsers stay, they just stop being the front door.

```
TODAY                     ← the shift board: every queue with a live count
  Fulfill                   pick → pack → ship, in stage order
  Prep                      needs cleaning / testing / repair / photos
  List                      ready to list
  Resolve                   returns, refunds, offers awaiting response

CATALOG      Inventory · Purchases · Suppliers
COMMERCE     Orders · Customers · Offers · Channels
INSIGHT      Reports · Imports
SETTINGS     Store · Team · Documents
```

- **Employees live in TODAY.** Every tile is a queue with a count and a one-tap entry into the
  guided flow. Empty queue = the work is done, which is itself the answer.
- **Managers use the rest.** Nothing is removed.
- The existing action cards (Not listed, Missing photos, Aged 60d…) become Prep/List queue entries —
  they already filter correctly as of the attention-filter work.

---

## 6. Why this makes it accurate, not just prettier

- **Required-at-the-gate** means the data is complete by construction — no "shipped, carrier
  unknown" rows to clean up later.
- **One derived stage** means an employee and a report can never disagree about what an order is.
- **Rules in the domain** means the mobile app, the admin, and any future surface enforce the same
  thing; a UI can't invent an illegal transition.
- **Queues, not browsers** means "did we forget anything?" is answered by a number, not a memory.

---

## 7. Build order (each its own PR)

1. **`OrderWorkflow` domain model** — stage derivation, transitions, entry requirements + tests.
   Move `OrdersWorkflowService` into Application. *No UI change; everything below depends on it.*
2. **Migration + capture** — packed/picked/weight/dims fields; the pack and ship gates.
3. **Fulfill queue + guided flow** — pick → pack → ship as a real workspace.
4. **Documents v1** — picklist, packing slip, thank-you, receipt (fixed templates).
5. **Template editor** — Settings → Documents, live preview.
6. **Coupons** — code module + insert + storefront redemption.
7. **TODAY board** — the queue-first landing page, once the queues exist.
8. **Tracking upgrade** — carrier auto-detect from number format, tracking links, stalled-shipment
   flags. (Live status only with option 2 above.)

---

## 8. Decisions needed before build

1. **Stage derived vs stored** — recommendation: **derived** (§2). Confirm.
2. **Pack gate strictness** — should weight + dimensions be *required* to leave "to pack", or
   warn-only? Required gives you real shipping cost data and makes option 2 instant; warn-only is
   faster for someone shipping ten things in a hurry.
3. **Postage** — option 1 (record what you bought elsewhere) now, and is option 2 (paid carrier
   account) on the table later, or a permanent no?
4. **TODAY board timing** — build the queues into the existing pages first (safer, incremental), or
   land the new landing page early so the shape is visible?
