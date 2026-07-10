# ScoutHaul Architecture

The admin/mobile app is the system of record. Items are catalogued once and sold across many channels. This
document is the contract for how that works, and — just as importantly — what we refuse to build.

Supersedes `CROSS_LISTING_AND_SYNC_PLAN.md`, which assumed the extension would scrape marketplaces for bulk data.
That was wrong on two counts: it violates eBay's User Agreement, and DOM scraping of list pages is the single
most brittle thing a system like this can depend on.

## 1. Constraints (non-negotiable)

- **No marketplace APIs.** eBay production Sell API access has been sought and refused for this account. Every
  other marketplace we sell on (Poshmark, Mercari, Depop, Vinted, Facebook Marketplace) has **no public seller
  API at all**.
- **No third-party/paid data APIs.**
- **No bulk DOM scraping.** It is against platform terms and it rots. This is the "cancer" rule.
- **Additive migrations only.** Production auto-applies on deploy.
- **Never silently wrong.** An adapter that cannot read a value reports that it could not, and writes nothing.
  An estimated number is labelled `Estimated`, never passed off as `Actual`.

## 2. The honest ceiling

Without an API there is no unattended sync. This is physics, not a missing feature.

| Capability | Reality |
|---|---|
| Post / delist on **our storefront** | Fully automatic — it is our code |
| Post to a **marketplace** | One click, operator present: we prefill the form, the human submits |
| Delist-on-sale elsewhere | Worklist computed instantly; external marketplace removal is manual unless the platform gives us a real API |
| "Mark as sold" | Known when a report is imported or the operator records it — not real-time |

What this buys: twenty minutes of retyping across six platforms becomes guided prefill/capture, and hand-typed fee guesses
become exact imported numbers.

## 3. Data in — platform report exports, not scraping

Every platform worth selling on hands the seller their own data. Verified:

| Channel | Orders / fees export | Listings export |
|---|---|---|
| Storefront | native (ours) | native |
| eBay | Sales report · Transaction report · Payout report | Listings tabs |
| Poshmark | My Sales Report (CSV, emailed; settles late) | My Inventory Report (CSV) |
| Mercari | Gross Sales Report (item id, fees, net proceeds, tax) | — |
| Depop | Sales download (buyer, fees, shipping); max 3 months/file | — |
| Vinted | GDPR account-data export only | — |
| Facebook Marketplace | none (Commerce Manager only, for shipping-enabled shops) | — |

**Exports are settlement data, not notifications.** Poshmark's sales report lagging is not a bug — a settlement
report by definition only contains settled sales. Timeliness comes from the operator recording the sale; accuracy
comes from the import that follows.

Where no export exists (Vinted, Facebook), money is **`Estimated`** from the channel's fee profile and labelled as
such in the UI until corrected.

## 4. Data out — the extension, and only this

The extension is the *hands*, never the pipeline. Exactly two DOM surfaces per platform:

1. **Prefill** the create/edit-listing form from an inventory item (title, description, price, photos, per-platform
   fields). The operator reviews and submits. We never auto-submit.
2. **Capture** the listing the operator is standing on — external listing id + URL — to link it to the item.

That is the whole brittle footprint. A form and a link. Both fail *visibly*, while the operator is looking at the
page. Nothing writes data on a failed read.

We do **not** read Seller Hub order lists, listing lists, or payout pages from the DOM.

Legacy listings created before ScoutHaul are linked either from an inventory export (Poshmark, eBay) or by
**link-on-touch**: the operator opens their own listing, the extension links that one page.

## 5. The domain — one shape for every channel

```
InventoryItem ──< ChannelListing >── ChannelAccount        (where an item lives, per channel)
      │
      └──< Sale ──> Order ──> Customer ──< CustomerChannelIdentity
                  (a Sale is a line of an Order; an Order belongs to a Channel)
```

- **`Order`** is channel-agnostic. It carries `Channel`, `ChannelAccountId`, `ExternalOrderId`, `BuyerReference`.
  **The storefront is simply channel #1** — a `ChannelAccount` with `IsNativelySynced = true`. There is no
  storefront-only order type, and no storefront special-casing in Orders or Customers.
- **`Customer`** is channel-agnostic. `Email` is nullable, because a marketplace buyer has a *username*, not an
  email. **`CustomerChannelIdentity`** `(Channel, ExternalUserId)` is the durable key, and is what lets the same
  human be recognised across platforms and merged.
- **`Sale`** is the money-bearing line of an `Order` (fees, COGS, refunds, true net).
- **`PlatformOrder` / `PlatformListing`** are *ingest landing zones* — raw imported or helper-captured rows,
  reconciled into the domain above. They are source-agnostic: report parsers, normalized JSON imports, or
  explicit page captures can fill them.

### Retired
`Sale.SaleGroupId` and `Sale.GroupOrderReference` were an order header smuggled onto sale rows, invented only
because `Order` was storefront-only. They are superseded by `Sale.OrderId` and kept solely so existing rows read
back. They are dropped in the contract migration once backfill is verified.

## 6. Per-platform code — two interfaces, nothing else

1. **`IReportParser`** *(data in)* — parse that platform's own export into normalized rows. A pure function.
   Unit-tested against a **real sample file**, mapped by **header name** (never column position), so a reordered
   column cannot silently corrupt money.
2. **`IListingFormAdapter`** *(data out, extension)* — `fillCreateForm(item, template)`, `captureListing()`.

Everything else — items, orders, customers, refunds, fees, payouts, and manual channel action queues — is shared. A new engineer
learns one domain and two interfaces.

## 7. What the server cron can and cannot do

- **Can:** reconcile, compute manual channel action worklists, sell-through and aging reports, nudge ("no Poshmark report
  imported in 21 days").
- **Cannot:** fetch marketplace data. There is no credentialed server-side path without an API, and logging in
  from a server is precisely the brittle hack this document exists to prevent.

## 8. Roadmap

1. **Unify the domain** — `Order`/`Customer` across channels; `Sale.OrderId` replaces the group hack. *(done)*
2. **Clean the extension** — delete scrapers; scope to prefill + capture. *(done)*
3. **Extension hardening** — scoped, revocable API key (no token paste); Chrome **and** Firefox.
4. **Report parsers** — eBay Transaction/Payout, Poshmark, Mercari, Depop — each written against a real file.
5. **Listing helper improvements** — `ListingTemplate` + per-platform field mapping + optional photo prefill; the operator submits.
6. **Manual channel action queue** end to end.
