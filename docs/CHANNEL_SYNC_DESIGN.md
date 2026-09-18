# Channel sync & reconciliation — design

Status: **proposal, awaiting sign-off.** Supersedes the remaining scope of
[IMPORT_RECONCILE_REDESIGN.md](IMPORT_RECONCILE_REDESIGN.md), whose matching work shipped in #178–#183.

---

## 1. What "done" looks like

Every sale, listing, price, fee and payout on every channel the business sells on — eBay, Depop,
Vinted, Poshmark — lands in HaulScout **without the operator re-typing or re-uploading it on a
schedule**, is matched to inventory automatically, and is **re-checked on a timer** so the books and
the stock stay true even on days nobody opens the portal.

The operator only ever sees two things:

- **Needs your decision** — a match the computer genuinely cannot make (already built, #179–#180).
- **Needs action** — something in the world must change: *delist on Depop, it sold on eBay an hour
  ago*; *eBay has not synced in 26 hours*; *this sale has no cost recorded*.

Principle carried over from #177: **the operator only answers questions the computer cannot.**

---

## 2. What each platform actually allows

Automation has to live inside each marketplace's rules. The browser extension's compliance model
(human click only, never unattended — `apps/extension/README.md`) stays; it is **not** a sync source.

| Channel | Official API for sellers | Export files | Sale emails | Automation ceiling |
|---|---|---|---|---|
| **eBay** | Yes — Fulfillment (orders), Finances (payouts, fees, refunds), Post-Order (returns), Feed / Trading (all active listings)¹ | All-Orders, Active Listings, Listings-Sales (parsers exist) | Yes | **Fully automatic**, scheduled |
| **Depop** | No public seller API² | Sales history CSV — rich: fees, payout dates, refunds, tax, bundles (sample in `docs/`) | Yes | Sale captured within minutes by email; money reconciled from the CSV |
| **Vinted** | No public API for standard sellers (Vinted Pro integrations are partner-gated)² | Needs a sample — **none on hand** | Yes | Same as Depop, once the export format is known |
| **Poshmark** | No public API² | Inventory report (available listings only; sample in `docs/`); sales report to confirm | Yes | Same as Depop |

¹ The Sell Inventory API only sees listings created *through* it, so listings made on ebay.com are
read via the Feed API's active-inventory report or Trading `GetSellerList`. Finances calls may
require eBay's digital-signature keys. **Both verified at build time, not assumed.**
² Checked again at build time; if any platform offers an API to this account, it slots in as a
connector (§4.1) with no redesign.

**Consequence:** three ways data arrives, in order of preference — **API pull**, **email capture**,
**file drop** — and the design treats them as interchangeable sources of the same records.

---

## 3. Shape

```
SOURCES (per ChannelAccount)        LANDING (exists)          ENGINE                     BOOKS
 eBay API connector ──┐
 Email capture ───────┼─► SyncRun ─► PlatformOrder/Line  ─► Matcher (#179) ──────► Sale / Order
 File drop ───────────┘   provenance PlatformListing       Reconciler (new)          ChannelListing
                          + raw file PlatformPayout (new)    │                       Inventory status
                                                             └─► Exceptions ─► Console "Needs attention"
SCHEDULER: per-account interval pulls + nightly reconciliation sweep (runs even with no new data)
```

What exists and is reused unchanged: `PlatformOrder`/`PlatformOrderLine`, `PlatformListing`,
`ChannelAccount`, `ChannelListing`, the one matching chain (`PlatformLineMatching`, #179),
auto-settle (#180), and "settled means settled" re-import (#178).

---

## 4. Components

### 4.1 Sources

- **Connector** (`IChannelConnector`, one per platform with an API) declares capabilities —
  `PullOrders`, `PullListings`, `PullFinances` — and pages from a stored cursor ("everything since
  the last successful pull", with a small overlap window so a late-arriving order is never missed).
  Only eBay has one at first.
- **Report format registry.** Every parser declares a **signature** (required header set), its
  platform, report kind, and version. A dropped file is matched against signatures:
  one match → parsed; none → "unrecognised export" said plainly (a platform renaming a column must
  fail loudly, never import garbage); several → the operator picks. Replaces "which card do I use".
- **Email capture.** One inbound address per organization. The operator adds a single forwarding
  rule in their mailbox for the platforms' *"you made a sale"* emails. A signed webhook receives
  them; per-platform parsers turn each into a `PlatformOrder` flagged **provisional** (price, item,
  buyer — no fees yet). Senders are allow-listed and DKIM-checked; duplicates dropped by
  `Message-ID`. When the platform's CSV later arrives, the same order is **enriched in place**
  (fees, payout), not duplicated.

### 4.2 SyncRun — every arrival is accounted for

One row per pull, email or file: account, source, report kind, window covered, started/finished,
status, and counts — **created / updated / unchanged / rejected** — plus per-row issues ("row 41:
no sale date"). The raw file or payload is kept (compressed, in Postgres like photos) so a parser
fix can **re-run history** instead of asking for the export again. This is the import history the
operator has never had, and the audit trail an accountant would ask for.

### 4.3 Landing records

Upserted by `(account, external id)` with a content hash, so a re-delivered order is "unchanged",
a changed one is an audited correction, and nothing is ever duplicated. Additions:

- `PlatformOrder.IsProvisional` — captured from an email, awaiting financial detail.
- `PlatformPayout` — a platform deposit and the orders it pays out, so payouts can be tied to the
  bank later (§7 step 6).

### 4.4 Reconciler — the part that keeps the books true

A set of **rules**, each pure (facts in → exceptions out), run after every SyncRun and again by the
nightly sweep. An exception **resolves itself** when its condition clears — no one has to close it.

| Rule | Detects | Outcome | Tier |
|---|---|---|---|
| Unrecorded sale | Platform order not yet turned into a sale | Auto-settle when the match is certain (exists); else *Needs your decision* | attention |
| **Double-sell risk** | Item sold on one channel, still live on another | *Delist on Depop* with a deep link; on eBay, end the listing via API **only** when the match is certain and the setting is on | attention — minutes matter |
| Listing drift | Platform says ended/sold, HaulScout says live (or the reverse) | Unambiguous → correct `ChannelListing` + audit entry; conflicting → exception | work |
| Price drift | Live price differs from HaulScout's price | Platform price is recorded as truth (it is what buyers see); change audited | hygiene |
| Orphan listing | Live on a platform, matches no inventory | *Needs your decision* (exists as listing matching) | work |
| Silent channel | Account has not synced within 2× its interval | *eBay has not synced since …* — so silence is never mistaken for no sales | attention |
| Missing cost | Sale settled with no item cost | Link to the item (profit is wrong until fixed) | hygiene |
| Payout mismatch *(step 6)* | Orders' net ≠ payout received | Exception with the difference | hygiene |

Exceptions feed the home console's existing tiers, so "Double-sell risk" appears in **Needs
attention** the moment it is true.

### 4.5 Scheduler

- **Per account:** interval (default every 4 hours for API connectors, configurable), enabled flag,
  cursor, last success, consecutive failures.
- **Nightly sweep** (03:00 in the organization's time zone): re-runs every reconciliation rule over
  current state — this is what catches the listing that went stale while no data arrived.
- A hosted background worker claims due work with `FOR UPDATE SKIP LOCKED`, so a second API
  instance never double-runs a sync. Failures back off exponentially and surface as the *Silent
  channel* exception rather than a log line nobody reads. **Sync now** on every account.

### 4.6 Credentials & security

- eBay OAuth (authorization-code grant): the operator clicks **Connect eBay**, approves on eBay,
  and HaulScout stores the refresh token **encrypted** (ASP.NET Data Protection, key from the
  environment). Never logged, never returned by any endpoint, revocable with **Disconnect**.
- Inbound email and any platform webhook: signature-verified, sender allow-listed, size-capped.
- Everything stays organization-scoped and permission-gated like the rest of the API.

### 4.7 UI — one "Channels & sync" hub

Replaces today's Imports page:

- **Accounts** — each channel with health (last sync, next sync, status), **Sync now**,
  **Connect / Disconnect**, and the email-capture address with setup steps.
- **Drop zone** — drop any exports, several at once; it shows what it detected before committing
  (*"eBay All-Orders · 143 orders · Jul 1–31"*, *"Depop sales · 58 sales"*), then imports.
- **History** — every SyncRun with its counts, drill-down to row issues, **re-run** after a fix.
- **Needs your decision / Needs action** — the exception queues, one place, each showing what the
  system tried.

---

## 5. Guarantees

| Failure | What happens |
|---|---|
| Same data delivered twice (re-import, email + CSV, overlapping pulls) | Upsert by external id + content hash — counted as *unchanged*, never duplicated |
| Platform changes its export columns | Signature mismatch — refused loudly, file kept for re-run once the parser is updated |
| A pull fails half-way | Cursor advances only on success; next run resumes; rows already written are idempotent |
| Platform unreachable for a day | *Silent channel* exception after 2× interval; backlog pulled on recovery |
| Order arrives by email before the CSV | Provisional order → enriched in place when the CSV lands |
| Time zones | Stored in UTC with the platform's zone recorded; days bucketed in the organization's zone |
| Currencies | Stored in the platform's currency; conversion only if an account sells in a second currency (decision below) |

---

## 6. Data model changes

- **New:** `SyncRun`, `SyncRunIssue`, `SyncArtifact` (raw file/payload), `SyncSchedule` (per
  account), `ChannelCredential` (encrypted), `ReconciliationException`, `PlatformPayout`.
- **Changed:** `PlatformOrder` gains `IsProvisional` and `SourceRunId`; `PlatformListing` gains
  `SourceRunId`; `ChannelAccount` gains the inbound-email token.
- **Unchanged:** the matching chain, sales, orders, inventory.

---

## 7. Build order — one PR each

1. **Foundation** — `SyncRun` + raw artifacts + format registry with detection; existing eBay
   parsers move behind it; **Depop sales** and **Poshmark inventory** parsers (samples on hand);
   the drop zone with detect-then-confirm, and import history.
2. **Reconciler v1** — exception model, auto-resolve, rules *double-sell risk*, *listing drift*,
   *silent channel* (plus the existing matching queues), wired into the console.
3. **Scheduler + eBay connector** — OAuth connect, encrypted credentials, orders + listings pulls
   on a schedule, **Sync now**, backoff, health on the hub.
4. **Email capture** — inbound webhook, sale-email parsers for Depop / Vinted / Poshmark,
   provisional orders, enrichment on CSV arrival.
5. **Vinted export parser** — as soon as a sample export exists.
6. **Finances** — eBay Finances API (fees, payouts, refunds), `PlatformPayout`, payout matching.

Then the sales & reporting overhaul reads from all of it.

Steps 1–2 remove the manual sorting and make the books self-checking; 3 makes eBay hands-off;
4 makes the other three near-real-time for the one thing that cannot wait — a sale.

---

## 8. Decisions needed

1. **eBay API.** Build the OAuth integration? It needs a free eBay Developer Program account and a
   production keyset created by the account owner. *(Recommendation: yes — it is the only channel
   that can be fully hands-off.)*
2. **Sync cadence.** Default every 4 hours per API account plus the nightly sweep? *(User asked for
   "daily or every few hours" — both are settings.)*
3. **Auto-ending on eBay.** When an item sells elsewhere, end its eBay listing automatically when
   the match is certain, or always leave it as an action? *(Recommendation: automatic for id/SKU
   matches, behind a setting defaulting on; title-only matches always ask.)*
4. **Price truth.** Platform price wins and is recorded, with the change audited?
   *(Recommendation: yes — it is what buyers actually see.)*
5. **Inbound email provider.** Resend (already used for outbound) if its receiving feature fits,
   else Postmark inbound — decided at step 4 after checking both.
6. **Poshmark** in scope? *(Assumed yes — there is a report on hand.)*
7. **Raw file retention.** Keep raw exports 13 months (a full year of comparisons)?
8. **Second currency.** Does any account sell in a currency other than USD (Vinted markets)?

## 9. Needed from you

- A **Vinted** export (whatever Vinted lets you download for sales) — the parser is built against
  the real file, never a guessed layout.
- One **"you made a sale" email** from each of Depop, Vinted and Poshmark (forward as attachment) —
  for step 4's parsers.
- A **Poshmark sales report**, if Poshmark offers one beyond the inventory report.
