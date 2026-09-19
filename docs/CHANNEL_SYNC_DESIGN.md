# Channel sync & reconciliation — design

Status: **approved (revision 2), in build — steps 1 and 2 built.** Decisions in §8 taken as recommended. Revision 1 (eBay API + sale-email capture) was
rejected: eBay production API keys are not realistically obtainable for this business, and email
parsing is brittle. This revision uses a **browser-extension sync agent**, the model the established
reseller tools use. Supersedes the remaining scope of
[IMPORT_RECONCILE_REDESIGN.md](IMPORT_RECONCILE_REDESIGN.md) (its matching work shipped, #178–#183).

---

## 1. What "done" looks like

Every sale, listing, price, fee and payout on eBay, Depop, Vinted and Poshmark reaches HaulScout
**without re-typing or re-uploading**, is matched to inventory automatically, and is re-checked on a
timer. When an item sells on one channel it is **delisted on the others within minutes**, so it is
never sold twice.

The operator sees only two queues:
- **Needs your decision** — a match the computer genuinely cannot make (built, #179–#180).
- **Needs action** — something in the world must change that the system could not do itself.

---

## 2. How existing tools do this

| Tool | Where automation runs | Account connection | Sale detection / auto-delist |
|---|---|---|---|
| **Vendoo** | Chrome extension, in the seller's browser | Uses the browser's logged-in marketplace sessions | Scans every **10 min**; computer on + Chrome open required; no mobile. eBay, Poshmark, Depop, Mercari, Whatnot, Vinted, Etsy |
| **List Perfectly** | Chrome extension | Browser sessions | Scan interval configurable, down to **every 1 min** |
| **PrimeLister** | Extension, optional cloud | Extension connects automatically once you log in to the marketplace in Chrome; or credentials stored encrypted for cloud | — |
| **Nifty** | Cloud (their servers) | Extension-assisted connect, or credentials used once to mint a session | Detects within **15 min**, device can be off |
| **SellerAider** | Extension; "cloud auto-delist" on Pro | — | 11+ marketplaces incl. Vinted |

What they share:

- **Marketplace access is the seller's own logged-in session**, used by the extension. None ask the
  seller for API keys (the tools that use eBay's official API do so with *their* partner keys).
- **Two service levels.** Extension-only works while a computer is on. Cloud works 24/7 but holds
  the seller's marketplace sessions or credentials on the tool's servers.
- **Where accounts get banned:** engagement automation — auto-sharing, following, bumping, mass
  offers — and scraping at volume. Crosslisting, sale detection and delisting your own sold items
  are the accepted use. Vinted tightened duplicate/automation detection in early 2026.

Sources: [Vendoo sale detection](https://www.vendoo.co/sale-detection-and-auto-delist) ·
[Vendoo detection requirements](https://help.vendoo.co/en/articles/8833508-why-aren-t-my-sales-being-detected) ·
[List Perfectly sales detection](https://listperfectly.com/selling/list-perfectlys-sales-and-order-detection-feature/) ·
[Nifty auto-delisting](https://docs.nifty.ai/auto-delisting/auto-delisting) ·
[Nifty connecting Poshmark](https://docs.nifty.ai/connections/connecting-poshmark) ·
[PrimeLister connections](https://docs.primelister.com/getting-started/connect-marketplace-accounts) ·
[SellerAider vs Vendoo](https://selleraider.com/crosslist-vs-vendoo/) ·
[Vinted enforcement 2026](https://www.zipsale.co.uk/blog/how-to-avoid-getting-banned-on-vinted-in-2026)

**Choice: extension agent now, not cloud sessions.** Cloud would mean holding live marketplace
sessions on a server — a security liability — and logging in from datacenter IPs, which trips the
marketplaces' fraud checks. The extension runs where the operator already logs in. The
"computer must be on" limit is solved with an always-on machine (§4.8), not with session custody.

---

## 3. Shape

```
BROWSER (any paired Chrome)                         HAULSCOUT API
┌─ Sync agent (extension) ──────────────┐           ┌──────────────────────────────────────────┐
│ Adapters: eBay · Depop · Vinted · Posh │  ingest   │ SyncRun ─► PlatformOrder / PlatformListing│
│  fast loop  : new sales      (~10 min) │ ────────► │            (landing records, exist)       │
│  slow loop  : all listings   (~4 h)    │           │ Matcher (#179) ─► Sale / ChannelListing   │
│  report loop: official exports (daily)│  leases   │ Reconciler ─► exceptions ─► console       │
│  commands   : delist                   │ ◄──────── │ Command queue (delist) ◄── reconciler      │
│ Safety governor · account binding      │ commands  │ Nightly sweep · agent health              │
└────────────────────────────────────────┘           └──────────────────────────────────────────┘
MANUAL: file drop (same parsers) for backfill and anything the agent cannot reach.
```

The rule from the existing extension stays: **the brain is the API, the extension is the hands.**
Adapters collect and act; matching, money and decisions live server-side where they are tested.

---

## 4. Components

### 4.1 Sync agent (the extension, rebuilt)

**Cross-browser, Firefox first.** One TypeScript codebase built with [WXT](https://wxt.dev) into a
Firefox add-on first, then Chrome and Edge, Safari later. Manifest V3 everywhere; the differences
are handled in the build, not in the adapters:

- **Portal ↔ extension** talk through a small content script on the portal's own pages
  (`window.postMessage` bridge). Firefox does not support `externally_connectable`, and the bridge
  works identically in every browser. *As built:* the bridge answers only "is this browser
  connected" and "check in now" — it never carries a pairing code (see §4.2).
- **Marketplace reads run inside a marketplace tab** (same-origin, the site's own first-party
  cookies), not from the background script — Firefox's cookie partitioning makes background
  requests unreliable, and the in-tab route behaves the same in every browser. *As built:* seller
  pages are **opened** in that background tab — a navigation, as a click makes — and read once
  loaded; only the small JSON endpoints the site's own pages call are requested from the page. A page
  fetched from script sends different request headers from one a person opens, and eBay's bot
  protection answered exactly that with its security check during development. The tab is the
  extension's own **sync tab**. It opens pinned and in the background when there is work, is reused
  by whatever follows, and closes itself when idle, so the operator never opens or keeps one. A page
  counts as loaded only at a marketplace address: Firefox reports a new tab "complete" while it is
  still on about:blank.
- **Trouble a person clears waits for them, not against them.** Signed out, the wrong account, no
  access, or a security check is reported on the heartbeat, and the server holds that account's work
  back; a requested scan stays queued, showing why, and runs once the cause clears. The extension
  notices a sign-in, or a finished check, from the operator's own marketplace tab loading. Anything
  else that stops work before it starts is reported (`work-failures`), which fails the request with
  the reason and backs the schedule off.
- **Host permissions are optional and per marketplace** — the extension asks for Depop access when
  Depop is connected, not for every site at install.
- **Distribution:** Firefox — a signed, unlisted add-on through addons.mozilla.org with automatic
  updates; Chrome/Edge — an unlisted store listing.

One **adapter per marketplace**, each implementing up to five capabilities:

| Capability | Default cadence | What it does |
|---|---|---|
| `scanSales` | off until enabled; suggested every 10 min | Recent sold orders since a cursor — the double-sell guard |
| `snapshotListings` | off until enabled; suggested every 4 h | Every live listing: id, title, price, quantity, status |
| `pullReport` | off until enabled; suggested daily, where one exists | Download the marketplace's **official export** (eBay All-Orders / Active Listings, Depop sales CSV, Poshmark report) through the session and send it to the server's **existing parsers** — full fees, payouts and refunds without a human downloading it. **Not Vinted** — it has none |
| `backfillSales` | first connection, after gaps | Walk the sold history page by page from a date |
| `delist` | on command | End a listing because it sold elsewhere |

**Platforms without an official export — Vinted.** Vinted has no seller sales report: the only
download is a GDPR account-data request that takes days and emails a confirmation each time, which
is not a sync source. So the Vinted adapter has no `pullReport`; instead its `scanSales` captures the
**full money detail** of each sale itself (price, shipping, buyer-protection and other fees, payout),
and a `backfillSales` capability walks the sold history page by page — on first connection, and
after any gap — so nothing sold while every computer was off is lost. Every adapter implements
`backfillSales`; for Vinted it is the only route to history.

*As built (Vinted):*
- **Where the data comes from.** The adapter reads the JSON API Vinted's own pages call: `users/current`,
  `my_orders?type=sold`, `transactions/{id}` and `wardrobe/{userId}/items`. It reads from a
  Vinted page, which renews an expired sign-in as it loads.
- **Money.** Each sale lands at the agreed price, with no seller fees; the buyer pays Buyer
  Protection and the prepaid label. It is paid out once the sale completes.
- **History.** The first scan is the backfill: it reads the whole order history.
- **Routine scans.** Vinted lists the sales with the latest activity first. The adapter remembers
  each sale it has read (`context.memory`), so a routine scan reads again only what changed.
- **Cancellations** travel beside the orders (`cancelledOrderIds`). A cancelled order not yet
  settled is ignored; a settled one is flagged for a person, because sales are never undone
  automatically.

How an adapter reads a marketplace is decided per platform at build time by inspecting that
marketplace's own web app in our logged-in browser: its **own JSON endpoints** where they exist
(structured, stable enough, cheap), otherwise its **pages** in a background tab. Official exports are
preferred wherever they exist — they are the formats the platform supports for sellers, and the
parsers for eBay and Depop are already written and tested.

### 4.2 Pairing — not a pasted token

Today the extension takes a pasted API token; a Supabase access token expires within the hour, so
unattended sync is impossible as built. Instead: **Connect this browser** in the portal shows a
one-time code → the extension exchanges it for a **device token** scoped to sync only (ingest,
leases, commands, heartbeat), revocable per device from the portal, rotated automatically.

*As built:* the code is **pasted into the extension's popup** — never handed to the extension by a
portal page. A script running on a page could otherwise pair the browser to someone else's
organization and send it the seller's orders. The code works once, for ten minutes, through a
rate-limited endpoint; only hashes of codes and tokens are stored; the token rotates weekly and the
previous one keeps working until the browser is seen using the new one.

### 4.3 Account binding

Each adapter reads which marketplace account the browser is logged into and binds it to a
`ChannelAccount`. Logged into a different Depop account than the one bound → the agent **refuses to
sync it** and says so. A browser signed out of a marketplace → *"Log in to Depop in Chrome"* on
the hub, instead of silently missing sales.

### 4.4 Leases — several browsers, one sync

The operator may have the extension on a laptop, a desktop and an always-on box. Before each loop
the agent asks the server for a **lease** on (account, loop); one agent wins, so nothing is scanned
twice and no listing is ever delisted twice. Leases expire, so a closed laptop hands over cleanly.

### 4.5 Command queue (server → agent)

Writes are commands, never side effects of a scan:

- The reconciler creates `Delist(listing, account, because sale S)`.
- An agent holding the account's lease claims it, executes, reports success or failure.
- Idempotent by command id; expires if unclaimed; bounded retries; every step audited.
- **Auto-delist is a setting per platform, off until the operator turns it on**, with three
  positions: *Off* (every delist is a *Needs action* item), *Dry run* (records what it would delist),
  *On*. Nothing acts on a marketplace by default.
- **Only certain matches auto-delist** — the listing is linked to the sold item by id through a
  settled match. Anything less becomes *Needs action* for a human, whatever the setting.

### 4.6 Safety governor (in the agent, configured from the server)

- Per-platform minimum intervals with random jitter, request-rate caps and daily caps.
- Backs off on 429/403 or any challenge page, pauses that platform after repeated failures, and
  raises it on the hub.
- A server-side **kill switch** per platform and per adapter version — a marketplace changes its
  site, we switch the adapter off remotely until it is fixed.
- **Never** engagement automation (sharing, following, bumping, offers). Reads only your own
  account's data. Listing *creation* stays human-reviewed prefill, as today.

### 4.7 Server side (mostly from revision 1, unchanged)

- **SyncRun** — every scan, report pull and manual file drop recorded: account, source (agent /
  file), window, counts (created / updated / unchanged / rejected), row issues, raw payload kept
  for re-runs.
- **Report format registry** — parsers declare header signatures; files (agent-pulled or dropped)
  are detected, never card-picked; unknown formats refused loudly.
- **Landing upserts** by (account, external id) + content hash — scans, reports and drops of the
  same fact never duplicate; a scan-detected sale is **enriched in place** when the daily report
  brings its fees.
- **Reconciler** — pure rules, run after each ingest and in a nightly sweep, producing
  self-resolving exceptions into the console:

| Rule | Outcome | Tier |
|---|---|---|
| Unrecorded sale | Auto-settle when certain (exists), else *Needs your decision* | attention |
| **Double-sell risk** | `Delist` command (certain match) or *Needs action* | attention |
| Listing drift (platform ended/sold vs HaulScout live, or reverse) | Correct + audit when unambiguous, else exception | work |
| Price drift | Platform price recorded as truth, audited | hygiene |
| Orphan listing (live, no inventory) | *Needs your decision* | work |
| **Silent channel** (no successful scan in 2× cadence) | *"No browser has synced Depop since 14:10"* | attention |
| Agent signed out / wrong account | *"Log in to Depop in Chrome"* | attention |
| Missing cost | Link to item | hygiene |
| Payout mismatch (build step 5) | Difference shown | hygiene |

### 4.8 Always-on, without cloud sessions

The limit every extension tool has: nothing syncs while every paired computer is asleep. The answer
here is **an always-on machine** — a small mini-PC or spare laptop running Chrome with the extension,
logged into the marketplaces, set never to sleep. It is simply one more paired browser; leases make
it the one that does the work, and the laptop takes over if it drops. The *Silent channel* rule
makes any gap visible rather than silent.

### 4.9 Scanning on demand

Every loop can also be run **now**, at any scope the operator needs:

| Scope | Examples |
|---|---|
| What | **Sales** · **Listings** · **Reports** · **History backfill** (from a date) · **Everything** |
| Where | One account · one platform · **all accounts** |

- A trigger creates a **scan request** on the server; the agent holding that account's lease runs
  it. Clicked from a browser that has the extension, the portal tells the extension directly, so it
  starts **immediately**; other paired browsers pick it up on their next heartbeat (≤ 1 min).
- The request shows its life on the hub — *Queued → Running on "Office mini-PC" → Done: 3 new
  sales, 1 price change* — or *Waiting for a browser* when none is online, so a click never
  disappears into nothing.
- Requests for the same account and scope **coalesce**; the governor still applies, so a burst of
  clicks cannot hammer a marketplace.
- Available from the hub, from each account row, from the command palette ("Scan Depop sales"),
  and from the extension popup itself.

### 4.10 UI — one "Channels & sync" hub (replaces Imports)

- **Accounts:** per channel — bound marketplace username, last scan per loop, next due, status,
  dry-run / live toggle, and **Scan** with its scopes (§4.9).
- **Browsers:** paired devices, last heartbeat, which accounts each is logged into; revoke.
- **Drop zone:** manual files, detect-then-confirm.
- **History:** every SyncRun, drill into row issues, re-run.
- **Needs your decision / Needs action** queues, plus the command log (what was delisted, when, why).

---

## 5. Guarantees

| Failure | What happens |
|---|---|
| Same data from scan, report and drop | Upsert by external id + hash — *unchanged*, never duplicated |
| Two browsers online | Lease: one scans, one delists |
| Marketplace changes its site | Adapter fails its self-check → paused, kill switch, hub warning; other platforms unaffected |
| Marketplace changes its export columns | Signature mismatch — refused loudly, raw file kept for re-run |
| All computers off | Silent-channel warning; on wake, `backfillSales` resumes from the cursor with no gap — the only safety net on Vinted, which has no export |
| Sale detected, fees unknown yet | Landed as sale; daily report enriches it in place |
| Wrong marketplace account logged in | Binding check refuses to sync; hub tells the operator |
| Delist fails (listing already gone, page changed) | Command reports failure → *Needs action*; never retried blindly |
| False-positive delist | Prevented by design: only id-linked settled matches auto-delist; dry-run before live |

---

## 6. Data model

- **New:** `SyncAgent` (paired device + token hash), `SyncLease`, `ChannelCommand`, `SyncRun`,
  `SyncRunIssue`, `SyncArtifact`, `ReconciliationException`, `PlatformPayout`.
- **Changed:** `ChannelAccount` gains bound marketplace username, per-loop cadence, dry-run flag,
  cursors; `PlatformOrder` / `PlatformListing` gain `SourceRunId`.
- **Unchanged:** matching chain, sales, orders, inventory.

---

## 7. Build order — one PR each

1. **Server foundation** — `SyncRun` + raw artifacts + format registry and detection; existing eBay
   parsers behind it; **Depop sales** and **Poshmark** parsers (samples on hand); drop zone with
   detect-then-confirm; import history. Useful immediately for manual files, and it is the landing
   the agent's report pulls use.
2. **Agent core** *(built)* — extension rebuilt in TypeScript with tests; pairing and device tokens;
   leases, heartbeat, safety governor, ingest endpoint; account binding; scans on demand by scope
   (sales, listings, history from a date, everything). First adapter: **eBay** (`scanSales` with
   fees from the seller's transaction ledger, `snapshotListings`). A sale seen before its fees are
   posted lands as *fees pending* and is not settled into money until they arrive.
3. **Reconciler + commands** — exception model, rules, console wiring; command queue; **delist on
   eBay** in dry-run, then live.
4. **Depop, Poshmark, Vinted adapters** — scan, snapshot, backfill, delist, each shipped in dry-run.
   Vinted's scan carries full sale money detail, since no export exists to supply it later.
5. **Report pulls + money** — agent downloads official exports daily; payouts and fee matching.

Then the sales and reporting overhaul reads from all of it.

---

### 4.11 Every automation is a setting

Nothing in this design acts on the operator's behalf by default. Each behaviour is a visible,
per-account (or per-platform) setting on the hub, off until switched on, with the suggested value
shown beside it:

| Setting | Default | Suggested |
|---|---|---|
| Scheduled sales scan | Off | Every 10 min |
| Scheduled listings snapshot | Off | Every 4 h |
| Scheduled report pull | Off | Daily |
| Auto-delist | Off | Dry run for a week, then On |
| Auto-settle certain matches (built, #180) | becomes a visible setting | On |
| Raw export retention | 13 months | 13 months |

## 8. Decisions (approved, with the operator's changes)

Settled: automation defaults **off** and every behaviour is a setting (§4.11); **Poshmark in scope**;
**Vinted = vinted.com, USD**; **Firefox first**, every browser eventually (§4.1). The recommendations
below remain as the suggested values shown beside each setting.



1. **Extension agent model**, with sync running while a paired computer is on — plus an always-on
   machine for 24/7? *(Recommendation: yes; a cheap mini-PC is the whole answer to "computer must
   be on".)*
2. **Auto-delist.** Live for id-linked matches after each platform's dry-run week? *(Recommended.)*
3. **Cadence defaults** — sales every 10 min, listings every 4 h, reports daily?
4. **Distribution** — unlisted Chrome Web Store listing (auto-updates, recommended) or load
   unpacked (manual updates)?
5. **Poshmark** in scope? *(Assumed yes.)*
6. **Vinted** market and currency?
7. **Raw export retention** — 13 months?

## 9. Needed from you

- At build time for each adapter: a session in Chrome logged into that marketplace, so the adapter
  is written against the real site, not a guess. For Vinted this is the only input — there is no
  file import for Vinted, by decision.
