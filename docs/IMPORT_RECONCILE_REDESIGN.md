# Import & matching — redesign

Status: **built** — #178 (re-import re-syncs), #179 (one matching chain), #180 (auto-settle +
renames), #181 (quality → Reports), #183. The remaining scope (single drop zone with detection,
import history, multi-channel, scheduled reconciliation) continues in
[CHANNEL_SYNC_DESIGN.md](CHANNEL_SYNC_DESIGN.md).

---

## 1. The complaint, and what actually causes it

> "When I import new reports it keeps having me reconcile things… order reconciliation clashes with
> listing reconciliation if the listing wasn't reconciled first… a week later it'll have me reconcile
> listings and orders that were already reconciled."

Audited causes:

| # | Defect | Status |
|---|---|---|
| 1 | Re-import **demoted reconciled orders** back into the queue unconditionally | **fixed** — only demotes if a line genuinely lost its mapping |
| 2 | Order matching read **ChannelListings only**, so it worked only if the listings report was reconciled first (that write is a side effect of listing reconcile) | **fixed** — also reads reconciled PlatformListings |
| 3 | **"Reconcile" is the wrong model.** The screen asks the human to match rows the computer could match itself | this redesign |
| 4 | **Two coupled queues** with a hidden ordering dependency the operator must know | this redesign |
| 5 | **Import is file-type-first** — four cards, and you must know which eBay export goes in which | this redesign |
| 6 | **Quality snapshots live in Imports** but are reporting data | this redesign |

Defects 1 and 2 were the *bugs*. 3–6 are why the flow feels wrong even once the bugs are gone.

---

## 2. The principle

> **The operator should only ever answer questions the computer genuinely cannot.**

Everything else — file type, which report, which listing, which item, what changed since last time —
is the system's job. Today the human does that work, and does it again next week.

---

## 3. The flow

```
DROP FILES → DETECTED → AUTO-MATCHED → DECIDE THE EXCEPTIONS → SETTLED
```

### 3.1 One drop zone, not four cards
Drop any marketplace export — several at once. The importer **detects the report from its headers**
(it already does header/detail detection for eBay's All-Orders format) and says what it found:

> **3 files read.**
> eBay All-Orders · 143 orders · Jul 1–31
> eBay Active Listings · 212 listings
> eBay Listing Quality · 241 rows → *sent to Reports*

No card-picking. Wrong file? It says so, instead of failing silently mid-parse.

### 3.2 Auto-match on every available signal
In confidence order, first hit wins:

1. **Existing mapping** — already answered; never re-asked
2. **Platform item id → reconciled PlatformListing** *(the link that was missing)*
3. **Platform item id → ChannelListing**
4. **SKU exact match**
5. **Title match** (existing `TitleMatch`)

Then report honestly:

> **138 of 143 matched automatically. 5 need you.**

### 3.3 Cross-source matching in one pass
An orders report referencing item `377372134054` and a listings report containing that same item are
**the same fact from two angles.** Match them to each other, then ask **once** about the underlying
inventory item — instead of asking in the listings queue and again in the orders queue.

This is what removes the "clash": there is no ordering dependency because there is no second queue.

### 3.4 Exceptions only
The queue holds **only genuine ambiguity** — no match, or several plausible matches. Each row shows
what the system tried, so the decision is informed:

> No inventory item matches item 377372134054 ("Chico's Size 1 Turquoise Silk Blend…").
> Closest by title: *Chico's Silk Button Front Jacket* (72%).
> **[ This is it ] [ Create new item ] [ Not my inventory ]**

### 3.5 Settled means settled
Once answered, a re-import **never re-opens it**. It only ever:
- corrects figures in place (fees, tax, payout) — auditable,
- asks about something genuinely new (a line the seller added).

---

## 4. Renaming

"Reconcile" is accounting jargon for what is really *matching*.

| Now | Proposed |
|---|---|
| Reconcile queue | **Needs your decision** |
| Orders to reconcile | **Orders to match** |
| Listings to reconcile | **Listings to match** |
| Link selected item | **This is the item** |
| New item from listing | **Create new item** |
| Ignore | **Not my inventory** |
| Reconcile (Match every line first) | **Confirm** (disabled until every line is decided) |
| "Reconciliation note" | **Note** |

---

## 5. Where things live

- **Import** keeps: orders, listings, refunds. One drop zone.
- **Quality snapshots move to Reports.** They are listing-performance observations
  (impressions, CTR, photo counts) — reporting input, never something to "reconcile." The Imports
  page should not carry a 241-count tile for data no one acts on there.
- **Reports** gains a listing-quality section. (Reports needs its own overhaul; this is the first
  piece of it and should not wait for the rest.)

---

## 6. Build order

1. ~~Convergence bugs~~ — **done** (re-import no longer re-asks; order matching sees listings).
2. **Unified matcher** — one service resolving a platform line to an inventory item through the
   signal chain in §3.2, shared by orders and listings. Pure, testable, replaces two code paths.
3. **Auto-settle** — anything unambiguous never enters a queue; import reports what it did.
4. **Exceptions screen** — one queue, cross-source, showing what was tried. Renames land here.
5. **Single drop zone** with report detection.
6. **Move quality snapshots to Reports.**

Steps 2–3 are where the daily pain goes away; 4–5 are the experience.

---

## 7. Decisions needed

1. **Auto-settle threshold.** Should a high-confidence title match settle on its own, or only exact
   id/SKU matches? *(Recommendation: id and SKU settle silently; title matches settle but are listed
   as "matched by title — review" so they are visible and reversible.)*
2. **Renames** — approve the table in §4, or adjust the wording.
3. **Retro-run.** Apply the new matcher to the existing 44 orders / 21 listings backlog on deploy, so
   the current queue shrinks immediately rather than only helping future imports?
