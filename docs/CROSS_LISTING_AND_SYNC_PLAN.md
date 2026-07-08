# Cross-Listing, Inventory Sync & Financial Accuracy — Master Plan

Goal: match **List Perfectly / Vendoo / Flyp / Crosslist / PrimeLister** feature-for-feature and go beyond, fully integrated into ScoutHaul (admin + storefront + mobile share one DB). Everything is done **through a human-led browser extension** driving the operator's own logged-in marketplace tabs — **no marketplace APIs** (eBay prod API is effectively unobtainable; these apps don't use it either).

## 1. Constraints (non-negotiable)

- **No marketplace APIs.** All platform data and actions go through the extension reading/automating logged-in pages: eBay, Mercari, Facebook Marketplace, Depop, Vinted, Poshmark (+ our own storefront, which is already natively synced).
- **Human-led, ban-safe.** The extension *enhances* operator-initiated actions in their real session — prefill + one-click, human-throttled, never a headless bulk bot. This is why the model is allowed and why accounts don't get banned.
- **Enterprise-clean.** Idempotent syncs, full audit trail, encrypted credentials-not-stored (we ride the operator's existing login — we never store marketplace passwords), reconcilable financials, additive migrations.
- **Accuracy from the source.** Fees/payouts/taxes are **read from each platform's own order/finance pages**, not hand-entered — so our numbers match the platform's report by definition.

## 2. Why the finance numbers differ today

Fees are hand-typed. eBay's real cost per order is a stack — final-value-fee %, per-order fixed fee, promoted-listing/ad fee, international fee, below-standard surcharge, payment processing, regulatory operating fee — varying by category and account standing. No human matches that. **Fix:** the extension scrapes the actual order + fee/payout breakdown from the marketplace's Seller Hub / order detail / payouts pages and pushes exact numbers into our `Sale` records.

## 3. Competitor feature matrix (target = all of these + more)

| Capability | List Perfectly | Vendoo | Flyp | ScoutHaul target |
|---|---|---|---|---|
| Cross-list one → many platforms | ✅ | ✅ | ✅ | ✅ + our storefront in the same action |
| Bulk cross-list / templates | ✅ | ✅ | ✅ | ✅ |
| Import existing active listings | ✅ | ✅ | ✅ | ✅ (seeds inventory, no re-entry) |
| Delist-on-sale everywhere (anti-oversell) | ✅ | ✅ | ✅ | ✅ (already have the ledger + cascade) |
| Sales + profit tracking | ✅ | ✅ | ✅ | ✅ **with exact scraped fees** (their weak spot) |
| Accurate fees/payouts | partial | partial | partial | ✅ read from payout pages + reconcile |
| Customer tracking | limited | limited | — | ✅ per-platform buyer records + repeat insight |
| Parcel/delivery tracking | limited | — | ✅ | ✅ tracking # + carrier delivery status |
| Relist / renew stale | ✅ | ✅ | — | ✅ scheduled |
| Price drop / offers | ✅ | ✅ | ✅ | ✅ |
| Poshmark share + send-offer bot | (add-on) | ✅ | ✅ | ✅ (human-throttled) |
| Photo tools (bg removal, reorder) | ✅ | ✅ | — | ✅ (already have multi-upload/hero) |
| Analytics / reports / 1099 | ✅ | ✅ | — | ✅ per-platform P&L, sell-through, tax liability |
| Multi-account / team | ✅ | ✅ | — | ✅ (org roles already shipped) |
| **Beyond them** | | | | unified storefront, acquisition-lot COGS, bundled sales, AI identify/comps, audited corrections |

## 4. Architecture — Brain (API) + Hands (extension)

```
Operator's browser (logged-in tabs)                 ScoutHaul API (the brain)
┌───────────────────────────────┐   push/pull   ┌───────────────────────────────┐
│ MV3 extension                 │ ───────────▶  │ ChannelAccount / ChannelListing│
│  content scripts per platform │               │ Sale / Order / Customer        │
│   READ: listings, orders,     │ ◀───────────  │ Shipment / tracking            │
│    fees/payouts, buyer, track │   work queue  │ ListingTemplate / FieldMapping │
│   ACT: create/delist/relist/  │               │ SyncJob / SyncEvent / audit    │
│    price/share/offer          │               │ Reconciliation + P&L           │
└───────────────────────────────┘               └───────────────────────────────┘
        │ human click                                    │ deterministic, no AI
        ▼                                                ▼
  eBay / Mercari / FB / Depop / Vinted / Poshmark   Admin · Storefront · Mobile (one DB)
```

- **Brain (API):** source of truth for inventory, listing state per channel, sales/finance, customers, shipments, templates, and the sync work queue. Already has `ChannelAccount`, `ChannelListing`, delist-on-sale cascade.
- **Hands (extension):** per-platform **adapters** (content scripts) with two capabilities — **READ** (scrape → normalize → POST to API) and **ACT** (fill forms / click on operator command). A shared **DOM adapter interface** so each platform is a self-contained module: `read.listings()`, `read.orders()`, `read.payouts()`, `act.createListing(item)`, `act.endListing(id)`, `act.updatePrice(id, price)`, `act.share(id)`.
- **Storefront** stays native (no extension) — it's a projection of inventory already.

## 5. Data model (existing + new)

Existing: `ChannelAccount`, `ChannelListing` (per item×channel: status/externalId/URL/price/imageHash/lastSynced/error), `Sale` (+ `SaleGroupId` bundled), `Customer`, `Order`, `Shipment`.

New (additive migrations, one per phase):
- **PlatformOrder** — a scraped marketplace order (platform, externalOrderId, buyer, line items → inventory items, gross, per-fee breakdown JSON, payout id, status, scrapedAt). The idempotent landing zone; reconciles into `Sale`(s).
- **PlatformFee / PayoutStatement** — exact fee lines + payout reconciliation (matches our P&L to the platform's financial report line-for-line).
- **ListingTemplate** + **PlatformFieldMapping** — map our inventory fields → each platform's category + item-specifics (the hard part of cross-listing well).
- **SyncJob / SyncEvent** — queue of pending work (e.g. "cross-list item X to Mercari", "delist item Y on Poshmark", "import eBay orders") + an append-only event log for audit.
- **ExtensionSession** — links an operator's browser to the org (heartbeat, which platforms are connected/logged-in), for the connect UI.

## 6. Sync engine

- **Pull (read):** on operator visit (or a periodic content-script tick while a platform tab is open), the adapter scrapes orders + active listings + payouts → POSTs to the API → reconciled into `PlatformOrder`/`Sale`/`ChannelListing` (dedup by externalId; idempotent).
- **Push (act):** operator selects items + target platforms → API enqueues `SyncJob`s → extension pops the queue, opens/uses the platform's create-listing form, prefills from the template + photos, and **stops for the operator to review + submit** (or one-click submit). Records external listing id/URL back.
- **Delist cascade (already built):** sale detected on platform A → API flips the item's other `ChannelListing`s to `PendingDelist` → extension surfaces the worklist → operator (or one-click) ends those listings → status `Delisted`. Prevents overselling.
- **Reconciliation:** `PayoutStatement` scraped from the payouts page → matched to `PlatformOrder`s/`Sale`s → variance surfaced → P&L equals the platform's report.

## 7. Compliance / anti-ban

- Ride the operator's existing login; never store marketplace credentials.
- Human-initiated actions, randomized human-like delays, per-platform rate caps, no parallel headless posting.
- Respect each platform's ToS posture the way List Perfectly does (assistive automation of a logged-in human).
- Everything auditable; operator can see + undo queued actions before they run.

## 8. Phased roadmap

**Done (foundation):** ChannelAccount + ChannelListing ledger, delist-on-sale cascade, delist worklist, MV3 extension scaffold, per-item cross-list panel (PRs #37–#42, merged).

1. **P1 — Finance/order ingestion (eBay first) — THE PRIORITY (fixes "numbers don't match" + auto-sync).** — *core shipped*
   - ✅ `PlatformOrder` + `PlatformOrderLine` entities + additive migration (`AddPlatformOrders`), unique on org+platform+externalOrderId.
   - ✅ Idempotent `POST /api/platform-orders/ingest` (batch upsert; reconciled orders never overwritten) + list/get with auto-match suggestions + `ignore`.
   - ✅ `POST /api/platform-orders/{id}/reconcile` → creates linked `Sale`(s) via `BundledSaleAllocator`, pushing the platform's EXACT item price, buyer shipping, tax, and fee total (fees apportioned by price). Expected-vs-scraped payout variance surfaced.
   - ✅ Admin `/finance/platform-orders`: reconcile queue (line→item mapping w/ suggestions), variance flags, reconciled history, and a manual JSON import fallback for before the extension is connected.
   - ✅ Extension READ adapter: `content-ebay.js` `readOrders()` (defensive, selector-mapped, never guesses money) + popup "Sync eBay orders" → `ingestPlatformOrders`.
   - Tests: 9 (ingest idempotency, reconciled-skip, exact-fee single + multi-line allocation, all-lines-mapped, double-sell block, negative-money reject, suggestion match, payout variance).
   - ⏳ Live-gated: tune `ORDER_SELECTORS` against the operator's real Seller Hub DOM; add a Payments/Payouts read for the exact fee stack (Orders page carries item price only). Back-correcting already-hand-entered sales stays a follow-up.
2. **P2 — Import existing active listings (eBay).** Adapter reads active listings → creates/links inventory + `ChannelListing`s (dedup). Seeds the system without re-entry.
3. **P3 — Cross-list push (eBay + storefront).** `ListingTemplate` + eBay field/category mapping; extension prefills the eBay create-listing form; record external id/URL. Then bulk.
4. **P4 — Delist-on-sale end-to-end** via the extension (detect → delist others automatically on the operator's next platform visit / open tab).
5. **P5 — Platform breadth:** Mercari, Facebook Marketplace, Poshmark, Depop, Vinted adapters (read + act), reusing the adapter interface. Poshmark share/offer automation.
6. **P6 — Parcel tracking:** capture tracking # from the shipping flow; carrier delivery status (a tracking aggregator webhook, or scrape) → auto-advance to Delivered.
7. **P7 — Analytics & reports:** per-platform P&L, sell-through, aging, tax liability, 1099 reconciliation. Photo tools polish.

Each phase = additive migration + extension adapter module + admin surface + reconciliation + tests, shipped as its own PR.

## 9. Open decisions

- Extension auth to our API: operator pastes a session token now; a proper OAuth/session bridge later.
- Scrape resilience: selectors drift — need a per-platform adapter test harness + graceful "couldn't read, tell the operator" handling (never silently wrong).
- How aggressive the pull tick is (compliance vs freshness).

## 10. Immediate next step

Build **P1 — eBay finance/order ingestion**: it directly fixes the accuracy complaint and delivers real auto-sync. Start with the `PlatformOrder` + payout entities and the ingest endpoint (API-side, testable now), then the eBay read adapter in the extension.
