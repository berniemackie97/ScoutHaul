# UX audit and build plan

2026-11-22. Scope: admin portal, mobile app, storefront. Read from the code at `main` (ac08cea) and the live storefront.
Nothing here is built yet. Each slice below is one PR, in the order given.

## What the audit found

### 1. The item is the product, and its screen is the weakest in the portal

`apps/admin/.../inventory/[id]/page.tsx` stacks a 189-line form of six cards in one column, and a side rail of eight
unrelated cards (status, sale, channels, photos, storefront, listing generator, bundle, delete). There is no section
navigation, no way to see "what is wrong with this item" at a glance, and no hierarchy between facts you edit daily
(price, status) and facts you set once (dimensions, notes).

- **Create (`inventory/new`) and edit are different forms.** Create has no photos ("added once it exists"), no channel or
  storefront step, and its own readiness checklist; edit has none of the checklist. The same fields are wired twice.
- **Save model.** One long form with a sticky "Save changes". A field-level mistake in a lower card is found after
  scrolling; errors are not summarised or linked.
- **Mobile `inventory/edit.tsx` is 1,184 lines in one file**, the largest screen in either app. Whether its field rules
  match the admin's (`item-profiles`, vocabularies) has not been checked; slice 4 starts by comparing them.

### 2. Marketplace data the extension already collects is thrown away at the edge of the portal

The extension and server hold, per marketplace listing (`PlatformListing`): title, price, quantity, status, format, SKU,
category, condition, description, brand, colour, size, URL, every photo and where each came from, listed/ends dates,
`MarkedSoldAt`, photo sync state and error, raw JSON. Per order: buyer, ship-to, tracking, carrier, fee breakdown,
fulfillment stage with shipped/delivered dates, payout.

What the portal shows of it:

| Data | Where it surfaces today | Gap |
|---|---|---|
| Listing price, status, link | Channels panel on the item (one line per account) | No title/description/photo comparison, no last-synced time, no drift |
| Listing description, brand, colour, size | Copied onto the item once, when linked | Never shown again; later marketplace edits are invisible |
| Listing photos and sync errors | Nowhere | A photo that failed to sync is silent |
| Sold signal (`MarkedSoldAt`) | A global queue (`/channels/sold`) | Not on the item |
| Fee breakdown, payout, carrier | Order page | Not rolled into item profit view per channel |
| Sync runs and issues | `/sync`, `/imports/runs/{id}` | Not linked from the item or listing they are about |

The server already exposes `GET /api/platform-listings`, but the item page never calls it.

### 3. "Listed on one or two platforms, missing on the others" has no view

The inventory table shows up to three channel badges and "Not listed". The item says "Listed on 2 of 4" in prose.
There is no way to ask: *which sellable items are missing from Poshmark*, *which listings disagree on price*, *which
presence is waiting for a person to take it down*. `ChannelListing.Status` now tracks the marketplace (PR 349), so the
data to answer this exists; no screen does.

### 4. The storefront has no CMS

The API has `StoreCategory` (name, slug, description, parent, sort order, active) and `UpsertStoreCategoryRequest`, but:

- the admin never calls `/api/store/categories`; the public category list is **derived from each item's free-text
  category**, so a typo creates a shelf and no one can reorder, rename, hide or nest one;
- the admin's only storefront controls are per-item (price, offers, shipping, publish) and Settings (name, domain,
  time zone); there is no overview of what is published, featured or in what order;
- homepage copy, hero, announcement and policy text are in code, so changing a sentence is a deploy;
- storefront images went blank in November because the page depended on a metered image service (fixed in PR 347).

### 5. Cross-cutting

- Forms: no shared field-error pattern, no unsaved-changes guard except on the item editor, no autosave anywhere.
- Tables: inventory and orders each have their own table component (`inventory-table.tsx`, `orders-table.tsx`); sorting
  and filter behaviour is per page. Not yet compared in detail.
- Empty, loading and error states, and responsive behaviour below the two-column item layout, have not been audited yet;
  slice 5 starts with a screen-by-screen pass in the browser and the simulator.
- There is a command palette (`components/search/command-palette.tsx`) but no keyboard path for the frequent item jobs
  (price, relist, mark sold).

## Tracking

| Epic | Issues |
|---|---|
| #351 One item workspace and cross-marketplace coverage | #354 shell, #355 create/edit flow, #356 Marketplaces section, #357 adopt listing data, #358 coverage view, #359 to-list queue, #360 mobile editor, #361 mobile marketplace status, #362 photo sync state |
| #352 Storefront CMS | #363 categories as records, #364 category tree, #365 listings overview, #366 collections, #367 content model, #368 content editor and preview, #369 storefront renders from CMS, #370 SEO |
| #353 Sync accuracy | #371 verify ended-listing detection, #372 listings batch rejection, #373 Poshmark gallery, #374 eBay gallery budget, #375 Depop removed-listing response, #376 accuracy panel, #377 guided order-report import |
| Cross-cutting | #378 screen-by-screen evidence pass, #379 two failing non-Docker tests, #380 pre-push hook and compiling `main`, #342 CI billing (existing) |

Existing polish tasks #300-#305 stay the home for general accessibility, tokens, states and per-surface polish; the epics
above add the structural work they depend on.

## The plan

Each slice ships on its own, behind no flags. Design before code for 1, 3 and 4: a one-page layout sketch checked
against the real component library (`@overhaul/ui`) before building.

### Slice 1 — Item workspace (admin)

One page, six sections with a sticky section rail and a status header that answers "what is wrong with this item":

1. **Overview** — title, status, price, cost, profit, readiness blockers (the create-time checklist, live, on every item).
2. **Details** — category-driven fields; category and condition from the shared vocabularies.
3. **Photos** — upload, reorder, hero; shows the source and sync state of every marketplace photo.
4. **Marketplaces** — one row per account, from `PlatformListing`: status, price (with drift against the item and the
   other channels), title/description differences, photo count, last read, sync issue, link. Actions: *use this
   listing's description/specifics*, *mark sold on this channel*, *open to list here* (a checklist of what is missing).
5. **Storefront** — the store listing, merged into this page rather than a side card.
6. **History** — sales, status changes, sync runs that touched it.

Create becomes the same page with the same sections in a guided order; photos come first. Autosave per section with
explicit "saved at" and inline errors; one save model everywhere.
Server: `GET /api/inventory/{id}/marketplace-listings` (join of `ChannelListing` and `PlatformListing`, with drift flags);
`POST .../adopt` to copy a listing's description/specifics onto the item.

### Slice 2 — Coverage and gaps

A Channels "Coverage" view: items by platform with filters *missing on X*, *price differs by more than N*, *take down
pending*, *ended elsewhere, still live here*. Row actions hand off to the item's Marketplaces section; a per-platform
"to list" queue replaces guesswork. Mobile gets the same list as a task queue.

### Slice 3 — Storefront CMS

Planned as data first:

- **Categories as records.** Use `StoreCategory`: create, rename, reorder (drag), nest, hide, description and hero image.
  Items are assigned to a category by choice, not by matching text; the public list reads the table. A migration seeds
  one `StoreCategory` per existing distinct item category so nothing disappears.
- **Merchandising.** Featured items and an ordered "collections" list (manual or rule-based: category, tag, price band).
- **Site content.** A small `StoreContent` model: announcement bar, hero (headline, copy, image, link), homepage sections
  (ordered list of featured collection / category grid / story block), footer and policy pages (markdown), per-page SEO.
  Edited in the portal with a live preview of the storefront in an iframe; published atomically (draft → publish).
- **Public API.** One cached read endpoint the storefront renders from; no deploy to change copy.

### Slice 4 — Mobile item editor

Split `inventory/edit.tsx` into the same sections as the portal, sharing the field rules from one package instead of a
copy. Add the Marketplaces section (read-only status, price, issues) and the coverage task queue.

### Slice 5 — Admin system pass

A shared `DataTable` (sort, filter, saved views, density), form error summary, empty/loading/error states, keyboard
shortcuts for price/relist/sold, and an accessibility sweep. Applied page by page after slices 1–3 settle the patterns.

### Slice 6 — Storefront polish

Product page (gallery, condition report, trust signals, size/fit), category and search pages driven by slice 3, cart and
checkout copy, mobile layout, performance budgets. Uses the CMS content from slice 3.

## Open decisions for the owner

1. Is *rule-based* collections wanted in slice 3, or manual-only first?
2. Should the item workspace write to marketplaces ever, or stay strictly record-and-guide (current compliance stance)?
   This plan assumes record-and-guide.
3. Slice order: the plan puts the item workspace first because every other screen links into it.
