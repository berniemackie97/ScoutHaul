# Commerce Consolidation & Architecture Cleanup — Design

Status: design agreed → executing as sequenced PRs. Each PR is independently shippable, additive, and keeps the
suite green. This doc is the contract; it supersedes ad-hoc fixes.

## Problems (all observed, not theoretical)

1. **Fulfillment has three sources of truth.** A single package's shipped/delivered state + tracking lives on the
   `Order` (`FulfillmentStatus`, carrier/tracking/shippedAt/deliveredAt), on the `Sale` (`Packed/Shipped/Delivered`
   statuses + `/packed //shipped //delivered` endpoints), and on the `Shipment` entity. The operator had to mark
   one order delivered in **three** screens.
2. **Sales, Finance, and Orders are three pages for one thing.** Orders group correctly on the Orders page but not
   on the Sales/Finance page; the same sale is a row in Finance and a line of an Order with no connection in the UI.
3. **Purchases is a top-level tab** when it is really an inventory-intake flow.
4. **The Listings tab is dead** (superseded by inventory + imports).
5. **CSV upload + "Reconcile platform orders" are scattered** across Finance sub-pages instead of one utility.
6. **The eBay Active Listings export is not parsed**, so listings/inventory can't be reconciled from it.
7. **The Application layer is bypassed.** 24/28 controllers touch the `DbContext`; ~100 `SaveChanges` calls live in
   controllers; `Api` is 11.5k LOC vs `Application` 4k; `ApiContracts.cs` is one 1,331-line file. Clean-architecture
   skeleton, transaction-script guts.

## Target information architecture (admin nav)

```
Control      Command Center · Inventory
Commerce     Orders · Customers · Channels          ← "Sales & finance" folds into Orders
Operations   Repairs · Consignments · Suppliers      ← Purchases leaves the nav (see below)
Data         Imports                                 ← new: CSV upload + reconcile queue
Intelligence Reports
System       Settings
```

- **Listings** tab: removed.
- **Purchases** tab: removed from nav. Intake becomes a button on Inventory ("Log a purchase") and a tab on the
  inventory item. The `AcquisitionLot` domain is unchanged; only its entry point moves.
- **Finance / Sales & profit**: folds into **Orders**. An Order is the unit; its lines carry the per-item money
  (fees, COGS, refunds, true net). The P&L KPIs move to the top of Orders (or Reports). `/finance/*` routes
  redirect to `/orders/*`.

## The fulfillment model — one source of truth: the Order

Fulfillment is a property of the **physical package = the Order**, never of a money line.

- **Order owns fulfillment:** `FulfillmentStatus` (Unfulfilled → Partial → Fulfilled), carrier, tracking, shippedAt,
  deliveredAt. One surface: the order's "Package tracking" card. The `Shipment` entity is absorbed into the Order
  (its fields already exist there) and retired.
- **Sale owns money only:** `Recorded / Returned / Refunded / PartiallyRefunded / Cancelled`. `Packed/Shipped/
  Delivered` are removed from the sale's meaning.
- **Derivation:** a sale's displayed fulfillment = its order's `FulfillmentStatus`. `SaleDto` exposes
  `OrderId` + `OrderFulfillmentStatus`. Advancing the order propagates to its sales (they never go stale). The
  `SalesController` fulfillment endpoints are removed; `SaleWorkflow` keeps only the financial transitions.
- **Migration:** additive + backfill. Existing `Sale.Status ∈ {Packed,Shipped,Delivered}` → set the parent
  `Order.FulfillmentStatus` accordingly, then set the sale's status to `Recorded`. Legacy `Shipment` rows → copy
  onto the `Order`.
- **Stage dates are history.** A step ("mark packed / shipped / delivered") records only its own stage, once, and
  never overwrites a date already recorded. An earlier stage nobody recorded stays null — the timeline shows it done
  with "Date not recorded" rather than borrowing a later date. Marking delivered asks when, defaulting to the
  marketplace's delivery date (`OrderDetailDto.TrackingDeliveredAt`) or now. Only the shipment form edits dates.
- **Delivery from sync.** A marketplace scan whose only news is "delivered" is a change (`DeliveredAt` and
  `FulfillmentStage` are in the ingest fingerprint), so it reaches the order. eBay's routine scan also re-sends
  older orders whose parcel moved inside the window. eBay gives the delivery day only; an exact time needs a carrier
  tracking source (not built: USPS Tracking 3.2 needs registered credentials).

## Architecture — get orchestration out of controllers

Introduce a thin **application-service layer** the controllers delegate to. The pattern, applied consistently:

```
Controller (Api)        HTTP concerns only: model-bind, auth attribute, map result -> ActionResult. No DbContext.
  → AppService (Application)   orchestration + the ONE SaveChanges per use case; returns DTOs or a Result<T>.
    → Domain                   pure rules (already good: allocators, workflows, calculators, parsers).
    → DbContext (Infrastructure) data access.
```

- Move `OrderAssembler`, refund/grouping/reconcile orchestration, and the import pipeline into `Application/`.
- Controllers shrink to routing. Target: no `HaulScout.Infrastructure.Persistence` import in any controller.
- Split `ApiContracts.cs` into `Contracts/{Orders,Sales,Inventory,Imports,Channels,Org}.cs` by feature.
- `Result<T>` (ok / notfound / conflict / validation) replaces `(T?, string?)` tuples and scattered `BadRequest`.

This is done **incrementally, one controller at a time**, never as a big-bang rewrite. Each converted controller
is a small PR with its tests still green.

## Imports — one hub, one pipeline (already begun)

`CsvTable` + `EbayOrdersReportParser` (PR #73) are the substrate. The hub:

- **One page `/imports`:** drop any file → detected type (by header signature) → preview of what will change →
  apply → history (`ImportBatch`, append-only, auditable).
- **One apply pipeline** fanning out: Orders → Customers → Sales/lines → Listings → Refunds, idempotent.
- **Parsers** (`Application/Imports/`), each pure + tested against a real header:
  - eBay All Orders ✅ (#73) · eBay Sales report (fees, merge by item number) · eBay **Active Listings** (#6) ·
    Depop Sales · Poshmark Inventory.
- The "Reconcile platform orders" queue moves here from Finance.

## Sequenced PRs

| # | PR | Risk | Notes |
|---|----|------|-------|
| A | Remove Listings tab; nav → target IA | trivial | frontend only |
| B | **Fulfillment single source of truth** | med | Order owns it; Sale money-only; propagate; migration; strip sale-fulfillment UI |
| C | Fold Finance/Sales into Orders; P&L KPIs on Orders; `/finance/*` → `/orders/*` | med | frontend + read models |
| D | Imports hub (`/imports` + `ImportBatch`) + move reconcile there | med | wraps #73 substrate |
| E | eBay Active Listings parser + reconcile to ChannelListing/inventory | low | additive, real-file tested |
| F | Purchases → inventory-initiated flow; drop nav tab | low | routing + entry point |
| G–… | Architecture layering: one controller → AppService per PR; split ApiContracts | low each | incremental, tests stay green |

Order of execution: **A → B → C → D → E → F**, then the G-series in parallel with feature work. B is first among
the real changes because it is the loudest defect and the foundation the consolidated Orders page (C) renders.
