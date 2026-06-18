# HaulScout + Overhaul Trading — Capability & Requirements Catalog

> The complete "what this must become" — the acceptance criteria, captured so nothing is discovered too late. Companion to [PLATFORM_BLUEPRINT.md](PLATFORM_BLUEPRINT.md).
>
> Owner: berniedev · Last updated: 2026-06-14 · Status: **living spec**

## How to read this

You told me: *"figure this all out so perfectly I will never have to worry about it."* This document is that — an exhaustive catalog of what a real, enterprise-grade resale business needs across the **storefront** (customer-facing) and the **back-office/admin** (running the company), with special depth on **finance/accounting** since that's the part that's easy to under-build and painful to retrofit.

**The no-revision guarantee (and its honest limit):** I can't promise we never *add* features — businesses grow. What I *can* design is a **foundation that doesn't need rebuilding**: a data model and module boundaries wide enough that every capability below slots in without schema rewrites or re-architecture. We build incrementally; we don't repaint the house each time.

**Legend**
- **Priority:** `MUST` (v1 to operate the business) · `SHOULD` (fast follow) · `LATER` (deliberate, parked) · `GATED` (needs legal/compliance)
- **Phase:** maps to the roadmap in §10 and the tracked task list.
- Each capability has a **Done when** acceptance criterion.

**Resale reality that shapes everything** (why this isn't a generic Shopify clone): most items are **unique, quantity = 1**, each with its own **acquisition cost, condition grade, defects, photos, and provenance**. Cost basis is **specific-identification** (not FIFO averages). Channels (own store + eBay + others) and **marketplace/payment fees** materially change true profit. **Make-an-offer / negotiation** is normal. **Consignment** means some inventory isn't owned — it's sold on behalf of others with a payout split. The model is built around these from the start.

---

## 1. Storefront — customer-facing (the "impressive, advanced" store)

### 1.1 Catalog & discovery — `MUST` (P4)
- Categories, curated **collections**, "new arrivals", featured/merchandised rows, lookbooks.
- **Instant search** (typo-tolerant, as-you-type), **faceted filters** (category, brand, condition, price, size, color, source/era, availability), sorting. _(Infra: Postgres full-text search first; self-host Typesense/Meilisearch if volume needs it — avoid per-search SaaS cost.)_
- Related/recommended items, "recently viewed", "you may also like".
- **Done when:** a shopper can find any published item in ≤3 interactions; filters update results without full reload.

### 1.2 Product detail page (PDP) — `MUST` (P4)
- Multi-photo gallery with zoom + optional video; mobile-first.
- **Condition report** (grade + per-defect notes/photos from `InventoryIssue`), **measurements**, specs, **provenance/source story**, authenticity notes.
- Trust signals: reviews/ratings, return policy, secure-checkout badges.
- Share, ask-a-question, size guide.
- **Done when:** PDP renders full condition/measurement/provenance data sourced from the one inventory record, with JSON-LD `Product`/`Offer` for SEO.

### 1.3 Pricing, offers & deals — `MUST`/`SHOULD` (P6)
- List price, **compare-at/MSRP**, sale/markdown price.
- **Make an Offer / Best Offer** on an item — `MUST` (resale-critical): buyer proposes, seller accepts/counters/declines, expiry, auto-accept/auto-decline thresholds.
- **Bundles**: curated or build-your-own bundles with bundle pricing — `SHOULD`.
- **Make an offer on a bundle** — `SHOULD` (you asked for this specifically).
- **Coupons / promo codes**: %-off, $-off, free shipping, category/collection-scoped, usage limits, expiry, one-per-customer, stacking rules — `MUST`.
- **Automatic promotions**: BOGO, tiered ("spend $X save $Y"), free-shipping threshold, first-order discount — `SHOULD`.
- **Quantity/lot breaks** where qty > 1 — `LATER`.
- **Gift cards / store credit** — `SHOULD`.
- **Loyalty/points** & **referrals** — `SHOULD`.
- **Done when:** offers, coupons, and bundle pricing all resolve to a single correct cart total with auditable discount lines; an accepted offer converts to a purchasable price for that buyer only.

### 1.4 Cart & checkout — `MUST` (P3/P4)
- Guest + account checkout; persistent cart; cart merge on login; **abandoned-cart recovery**.
- Real-time **shipping options & rates**, **sales tax via Stripe Tax**, **address validation**.
- Payments: cards + **wallets** (Apple/Google Pay) via Stripe; optional BNPL later.
- **Local pickup** option; order notes; clear order review.
- **Done when:** a buyer completes purchase (incl. Stripe Tax + shipping) via Stripe test mode; the item is **reserved at checkout under a concurrency guard** (two buyers cannot take the same qty-1 item), decrements on payment, and a `Sale` + receipt email are produced.

### 1.5 Customer accounts — `MUST`/`SHOULD` (P4)
- Profile, multiple **addresses**, **order history & live tracking**, reorder.
- **Wishlist/favorites** (+ shareable), **saved searches**, **back-in-stock alerts**.
- **Returns/exchange requests** (RMA), **store credit** balance.
- Submit **repair requests** and **consignment submissions** from the account.
- Messages/support thread.
- **Done when:** a logged-in customer can self-serve orders, returns, wishlists, and intake requests without contacting support.

### 1.6 Content, brand & SEO — `MUST`/`SHOULD` (P4/P9)
- Brand/about story, **blog/CMS**, FAQ, policy pages (shipping, returns, privacy, terms).
- Newsletter signup, announcement banners, marketing landing pages.
- SEO: per-item metadata, structured data, sitemaps, canonical URLs, fast Core Web Vitals.
- **Done when:** Overhaul Trading brand pages + blog are editable in admin and indexable; Lighthouse SEO ≥ 95.

### 1.7 Engagement & notifications — `MUST` transactional (P3/P4) · `SHOULD` marketing (P9)
- **Transactional email is `MUST` and early (P3/P4):** order confirmation/receipt, shipping + tracking, offer accepted/countered, password reset. Needed the moment checkout exists — provider (Resend/Postmark) wired in Phase 3.
- **Marketing** (`SHOULD`, P9): back-in-stock, price drops, abandoned-cart, campaigns; email/SMS/push.
- Reviews & ratings (moderated, photo reviews), Q&A.
- **Done when:** transactional emails send from Phase 3; marketing sends with per-customer consent in Phase 9.

### 1.8 Trust, accessibility & compliance — `MUST` (cross-cutting)
- Secure checkout (PCI via Stripe-hosted fields), cookie/privacy consent.
- **WCAG 2.1 AA** accessibility; performance budget (LCP < 2.0s, CLS < 0.05, INP < 200ms).
- **Done when:** automated a11y + perf checks pass in CI for storefront routes.

---

## 2. Admin / back-office — running the company

### 2.1 Inventory management — `MUST` (P5) — *the shared brain*
- The **one inventory** (your existing `InventoryItem`): create/edit/bulk-edit, full **lifecycle status**, photos, **condition & defects**, **storage location/bins**, barcodes/SKU, quantity, **per-item acquisition cost**.
- **Publish to channels** with a toggle: own storefront + eBay (+ future channels) from the same record — no re-entry.
- **Cross-channel oversell prevention (`MUST`):** a sale on any channel instantly delists/decrements every other channel; availability derives from `InventoryStatus`, never duplicated. Add a `Quantity` field (default 1) so qty>1 supplies decrement instead of hard-sell.
- Valuation, **aging**, dead-stock flags; import/export (CSV); print labels/barcodes.
- **Done when:** editing an item once updates everywhere it's published; selling it on eBay marks it sold on the storefront within seconds (no oversell); a published item appears on the storefront with no duplicate data entry.

### 2.2 Sourcing & purchasing — `MUST` (exists, P5)
- Sourcing trips, sources/suppliers, **purchase/cost capture**, **budgets**, deal analysis (ROI/margin/break-even already computed), receiving into inventory.
- **Done when:** a sourcing trip's spend rolls into budget actuals and each item's cost basis.

### 2.3 Suppliers / vendors — `SHOULD` (P5)
- Supplier records, contacts, terms, purchase history, amounts owed (payables).
- **Done when:** spend and items are attributable to a supplier with a running history.

### 2.4 Orders & fulfillment — `MUST` (P7)
- Order management (search/filter/status), **pick/pack**, **shipping label purchase + tracking** (carrier integration), partial/multi-shipment, local-pickup scheduling.
- **Returns/RMA & exchanges**, refunds (full/partial), cancellations, restocking.
- **Done when:** an order goes from paid → packed → labeled → shipped → delivered, with tracking surfaced to the customer; a return restocks inventory and issues a refund/credit.

### 2.5 Customers / CRM — `SHOULD` (P5)
- Customer profiles, order history, **lifetime value**, segments, notes, marketing consent, communication log.
- **Done when:** any customer's full history and value are visible in one view.

### 2.6 Pricing, merchandising & promotions admin — `MUST`/`SHOULD` (P6)
- Manage prices, **markdown/aging rules**, collections/featured placement.
- Build/manage **coupons, automatic promotions, bundles**, configure **make-offer** thresholds, gift cards, loyalty, referrals.
- **Done when:** non-technical operator can launch a coupon or a bundle and see it live on the storefront.

### 2.7 Repairs — `SHOULD` (P9)
- Intake (from storefront or walk-in), job tracking, status, parts/labor cost, customer comms, invoicing/payment.
- **Done when:** a storefront repair request becomes a tracked job with cost and customer updates.

### 2.8 Consignment — `SHOULD` (P9)
- Intake → approval → consignor account; **split/payout terms**; sold-item attribution; **consignor statements & payouts**.
- **Done when:** a consigned item sells, the consignor's share is computed and a payout is recorded; your books separate owned vs consigned revenue.

### 2.9 Marketing & growth — `SHOULD` (P9)
- Email/newsletter campaigns, abandoned-cart flows, loyalty/referrals, reviews moderation, SEO content/blog, discount campaigns, UTM/attribution.
- **Done when:** a campaign can be created, sent, and its revenue attributed.

### 2.10 Staff, roles & permissions — `MUST` (exists, P5)
- Multiple staff, **custom roles + granular permissions** (already in RBAC), teams, invite codes, **audit log** of who did what.
- **Done when:** a limited-permission staff member is blocked from finance/settings and every sensitive action is logged.

### 2.11 Settings & configuration — `MUST` (P5)
- Store branding/theme, shipping methods/zones/rates, **tax settings**, payment config, policies, notification templates, domains, integrations.
- **Done when:** core store behavior is configurable without code.

---

## 3. Finance & accounting — the enterprise backbone *(the part you're unsure of — built thoroughly so you won't have to revise)*  `MUST`/`SHOULD` (P8)

This is what turns "I sell stuff" into "I run a company with books that survive tax season and tell me the truth about profit."

### 3.1 Cost & profit (per item / order / period)
- **Specific-identification COGS** — each unique item carries its own acquisition cost (+ allocated costs: cleaning/repair/shipping-in). On sale, that exact cost flows to COGS.
- **Gross profit & margin** per item, per order, per period, per channel, per category.
- **True net** after **payment processing fees** and **marketplace fees** (eBay etc.) — fees captured per transaction.
- **Done when:** every sale shows revenue, exact item cost, fees, discounts, and net profit; period P&L ties to the sum of line items.

### 3.2 Revenue, discounts, refunds
- Gross vs net revenue; discount/coupon cost; refunds and their effect on profit; store-credit liability.
- **Done when:** a refund reverses revenue and restores/writes-off COGS correctly.

### 3.3 Expenses & budgets
- Operating expense tracking by **category** (rent, supplies, software, mileage, fees), recurring expenses, budgets vs actual, **cash tied up in unsold inventory**.
- **Done when:** monthly expenses by category and budget variance are reportable.

### 3.4 Inventory valuation & cash
- Total **inventory value** at cost and at expected retail, **aging buckets**, dead-stock write-downs, **inventory turnover** & **days-to-sell**.
- Cash-flow view: money in (sales) vs out (sourcing/expenses/payouts).
- **Done when:** a point-in-time inventory valuation and an aging report are one click away.

### 3.5 Sales tax & compliance
- **Sales tax** via **Stripe Tax** (automated jurisdiction-aware rates at checkout), liability report, exemptions; tracked separately from revenue.
- **1099-K reconciliation** (payment-processor/marketplace reports vs your books), year-end summaries.
- **Done when:** tax collected is tracked separately from revenue and reportable by period/jurisdiction.

### 3.6 Payouts & liabilities
- **Consignment payouts**, refunds, store credit — tracked as liabilities until settled.
- **Done when:** outstanding payouts/credits are visible and clearable.

### 3.7 Accounting handoff
- **Export to accounting** (QuickBooks/Xero or CSV) — journal-ready summaries; or built-in P&L statement per period.
- **Done when:** a bookkeeper/accountant can take a period export and reconcile without re-keying.

---

## 4. Reporting, KPIs & Command Center — `MUST`/`SHOULD` (P5/P8)

**Command Center** (admin home, real-time): today's revenue & profit, orders to fulfill, low/aging inventory, sourcing pipeline value, channel health, repair/consignment queues, **alerts** (out-of-stock, aged stock, returns, over-budget).

**Report library** (what a first-time owner needs, pre-built so you never wonder):
- Sales: revenue/profit over time, by channel, by category, **best/worst sellers**, AOV.
- Inventory: valuation, **turnover**, **sell-through %**, **days-to-sell**, aging, dead stock.
- Finance: **P&L**, margin trends, expenses vs budget, cash flow, tax collected.
- Customers: new vs returning, **repeat rate**, LTV, top customers.
- Marketing: coupon/promo performance, campaign attribution, conversion rate.
- Sourcing: spend vs budget, ROI by source/supplier, **buy-recommendation accuracy**.
- **Scheduled reports** emailed (daily/weekly/monthly); export any report to CSV/PDF.
- **Done when:** each report renders from live data and exports; Command Center tiles update in near real-time.

---

## 5. Integrations — `MUST`/`SHOULD`
- **Payments:** Stripe (cards, wallets, refunds, webhooks). `MUST`
- **Shipping:** carrier rates + labels + tracking (Shippo/EasyPost). `MUST` (P7)
- **Marketplaces:** eBay (exists), extensible to others. `SHOULD`
- **Accounting:** QuickBooks/Xero export. `SHOULD` (P8)
- **Email/SMS:** transactional + marketing (Resend/Postmark + Twilio). `SHOULD`
- **Tax:** rate service (e.g. tax API) if manual rates outgrow. `LATER`
- **Done when:** each integration is configurable in settings with keys stored as secrets, not in code.

---

## 6. Non-functional — enterprise quality bars (cross-cutting, every phase)
- **Security:** Supabase auth, granular RBAC, **automatic tenant/data scoping** (EF global query filters), PCI via Stripe, secrets in env/manager, rate limiting, idempotency.
- **Data integrity:** audit log, soft-delete where needed, migrations versioned, referential integrity.
- **Reliability:** automated backups + tested restore, health checks, uptime monitoring.
- **Observability:** structured logs, metrics, traces (OpenTelemetry), error tracking (Sentry).
- **Performance:** storefront CWV budgets; admin tables paginate server-side.
- **Accessibility:** WCAG 2.1 AA.
- **Privacy:** GDPR/CCPA data export & deletion requests, consent tracking.
- **Testing & release:** unit + integration + e2e (Playwright); **staging environment** mirrors prod; migrations + critical flows (checkout, oversell, refunds) covered before deploy.
- **Analytics:** privacy-friendly web/product analytics (self-host Plausible/Umami or PostHog free tier) for conversion + funnel.
- **Done when:** each bar has a CI check or an operational runbook.

---

## 7. Explicitly parked / gated (so they're not "forgotten", just deferred)
- **Public SaaS** (other resellers onboarding, billing, Stripe Connect, platform admin) — `LATER` (P11).
- **Pawn / lending** (`PawnTicket`) — `GATED` (P12): regulated; needs legal review before any build.
- **Multi-currency / international** — `LATER`.
- **POS / in-person retail** — `LATER`.
- **Mobile storefront app** (vs responsive web) — `LATER`.

---

## 8. Capability → phase map

| Phase | Theme | Headline capabilities |
|-------|-------|-----------------------|
| P0 | Foundation | Monorepo, design system, infra |
| P1 | Security & data scoping | Global query filters, secret rotation, audit log base |
| P2 | Contract & auth | OpenAPI TS client, Supabase auth on web |
| P3 | Commerce core + infra | Catalog publish, cart, orders, customers, Stripe+Tax, **checkout reservation/oversell guard**, **object-storage image pipeline**, **transactional email** |
| P4 | Storefront experience | Discovery, PDP, accounts, SEO, CWV |
| P5 | Admin & Command Center | Inventory, orders, CRM, suppliers, RBAC, settings, real-time home |
| P6 | Pricing, promotions & offers | Coupons, bundles, **make-offer (+ on bundles)**, markdown rules, loyalty, referrals |
| P7 | Fulfillment, shipping & returns | Labels, tracking, RMA, refunds |
| P8 | Finance, accounting & reporting | COGS, P&L, margins, valuation, tax, cash flow, exports, KPI reports |
| P9 | Adjacent & marketing | Repairs, consignment + payouts, CMS/blog, reviews, notifications, campaigns |
| P10 | Scale hardening | Legacy image backfill, observability/Sentry, rate limits, search upgrade, load test, restore drill |
| P11 | Public SaaS (parked) | Onboarding, billing, Connect, platform admin |
| P12 | Pawn/lending (gated) | Compliance-first |

> **Foundation-first principle:** P0–P3 lay a data model and module layout that already anticipates P4–P12. We don't ship every feature at once, but we don't have to re-architect to add them.
