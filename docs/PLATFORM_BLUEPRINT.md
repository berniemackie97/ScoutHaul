# ScoutHaul Platform Blueprint

> Authoritative architecture + execution plan for unifying **ScoutHaul** (the company's internal resale/inventory engine + API) and **Overhaul Trading** (the company's public storefront brand) into one enterprise-grade platform, sharing one inventory.
>
> Status: **APPROVED — internal-tool scope** · Owner: berniedev · Last updated: 2026-06-14
>
> This document is the single source of truth for **architecture**. The exhaustive feature/acceptance spec lives in [REQUIREMENTS_CATALOG.md](REQUIREMENTS_CATALOG.md). Every phase below maps to a tracked task. Do not start a phase before its predecessor's exit criteria are met.
>
> **⚠️ SCOPE (2026-06-14):** ScoutHaul is an **internal tool/component for the company**, not a public SaaS — no public tenant onboarding, no self-serve billing, no Stripe Connect, no SaaS marketing site for now. The company runs **one** Overhaul Trading storefront. The multi-tenant infrastructure already in the codebase (`Organization`, `IOrganizationScoped`, RBAC) is **kept as-is** — it costs nothing, prevents data-scoping bugs, and leaves the public-SaaS door open later (parked Phase 11). We build the company's own stack first.

---

## 0. TL;DR — what we're building

One backend (the existing **.NET 10 ScoutHaul API**) is the system of record for everything. The web apps from OverhaulTrading are **salvaged for UI, repointed at that API, and their Prisma DB / Clerk / tRPC data layers are retired.** Auth consolidates on **Supabase** (already wired into the backend). Everything lives in **one polyglot monorepo**. The multi-tenant data model stays in place (dormant) so the public-SaaS door is open later, but current scope is **the company's own single store** — we build Overhaul Trading's storefront + admin + command center on the shared inventory. Public SaaS onboarding is parked (Phase 11).

```
                         ┌────────────────────────────────────────────────┐
                         │     Company Platform — ScoutHaul + Overhaul     │
                         └────────────────────────────────────────────────┘

  SURFACES (presentation only)                       CORE (system of record)

  📱 Mobile (Expo)          ─┐
     sourcing / scanning     │
                             │      OpenAPI typed client      ┌───────────────────────────┐
  🛍  Storefront (Next)      ─┼──────────  HTTPS/JSON  ───────▶│   ScoutHaul.Api (.NET 10) │
     overhaultrading.com      │                                │   modular monolith        │
     public store            │                                │   ┌─────────────────────┐ │
                             │                                │   │ Sourcing  Inventory │ │
  🛠  Admin / Command Center─┘                                │   │ Market    Listings  │ │
     admin.overhaultrading.com                                │   │ Sales     Catalog   │ │
     operator back-office                                     │   │ Commerce  Repairs   │ │
                                                              │   │ Consign.  CMS       │ │
   (SaaS marketing site = parked, Phase 11)                   │   │ Org/RBAC  Finance   │ │
                                                              │   └─────────────────────┘ │
                                                              └─────────────┬─────────────┘
                                                                            │ EF Core
   AUTH: Supabase (operator + customer projects)                           ▼
   STORAGE: object store + CDN (images, early)         ┌───────────────────────────────────┐
   PAYMENTS: Stripe (+ Stripe Tax)  ·  Connect parked  │  PostgreSQL (single DB, Supabase) │
                                                       └───────────────────────────────────┘
```

---

## 1. Decisions locked

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| D1 | Business model | **ScoutHaul = internal back-office/engine for the company; Overhaul Trading = the company's public storefront brand.** Not a public SaaS for now. | Confirmed by you (2026-06-14). `Organization`/`IOrganizationScoped` infra stays dormant for future-proofing; one active org. |
| D2 | System of record | Single **.NET API + one Postgres** | Confirmed. Clean architecture + mature RBAC already exist. Avoids two ORMs fighting one schema. |
| D3 | OverhaulTrading fate | **Salvage UI, retire data layer.** Prisma → reference spec; Clerk/tRPC removed | Next.js storefront/admin UI is real work worth keeping; its Postgres/Prisma duplicates the source of truth. |
| D4 | Auth | **Supabase** everywhere | Backend already validates Supabase JWTs. Free to 50k MAU. Adding Clerk = a second auth system to pay for and maintain. |
| D5 | Repo | **One polyglot monorepo** (fold web into ScoutHaul) | Detailed in §11. Atomic API+client+UI changes; shared design system; solo/small-team coordination cost of multi-repo not justified yet. |
| D6 | Storefront vs Admin | **Separate deployables**, unified at data/auth layer | Opposite security/caching/SEO/audience profiles. Roles govern *within* admin, not storefront-vs-admin. |
| D7 | Product model | Storefront product = **projection of `InventoryItem` + a `StoreListing`**, not a new table | `InventoryItem` already carries the full resale lifecycle and `OrganizationId`. |
| D8 | API style | **REST + OpenAPI**, typed TS client generated | Cross-language (C#↔TS) means contract-over-codegen, not shared types. Drop tRPC. |
| D9 | Sequencing | **Company-stack-first; SaaS parked** | Ship the company's own storefront + admin + command center. Keep multi-tenant seams dormant; don't build public onboarding/billing until/unless ScoutHaul goes public (Phase 11, parked). |
| D10 | Domains | `overhaultrading.com` = storefront; admin on `admin.overhaultrading.com` | No scouthaul.com needed — ScoutHaul is internal, not a marketed product. |
| D11 | Repo merge | **Copy** OverhaulTrading web apps/packages into ScoutHaul | OverhaulTrading has **no git commits** (loose files) — there's no history to subtree-merge. Copy in, keep the original as a backup until the monorepo builds, then delete. |
| D12 | Pawn/lending | **Out of scope**, gated to compliance review (Phase 12) | Regulated; not v1. |
| D13 | Images | **Object storage + CDN from the start** (not a late migration) | Storefront LCP<2s needs CDN images; serving `bytea` from the API to a public store is slow/expensive. Moved into Phase 3. |
| D14 | Customer auth | **Two Supabase projects** — operators (invite-only) vs customers (open signup) | Keeps a public-signup pool from ever touching privileged operator identities. Free tier allows 2 projects. |
| D15 | Sales tax | **Stripe Tax** | Automated, jurisdiction-aware, cheap; avoids hand-maintaining rate tables. |
| D16 | Search | **Postgres FTS first → self-host Typesense/Meilisearch if needed** | Cost-conscious; avoid per-search SaaS (Algolia) until volume justifies. |

---

## 2. Business & tenancy model

- **Tenant = `Organization`.** Overhaul Trading is one row. Future resellers are new rows.
- **Two user populations, never mixed:**
  - **Operators / staff** — `AppUser` ↔ `OrganizationMember` with `OrganizationRoleDefinition` + `OrganizationRolePermission` (custom roles, teams, hashed invite codes). They use **mobile + admin**.
  - **Customers / shoppers** — buyers on a tenant's storefront. **New `Customer` aggregate, `IOrganizationScoped`, linked to a Supabase user id.** A customer must *never* be resolvable to operator scope.
- **Storefront resolves tenant by host (built now, single store in use):**
  - `overhaultrading.com` → resolves to the Overhaul Trading org.
  - The host→tenant seam is built now (cheap, keeps the model honest); multi-store wildcard/custom-domain automation is parked (Phase 11).
  - Host→tenant resolution happens in Next.js middleware; the resolved tenant id is sent to the API, which still authorizes independently (never trust the host alone).
- **Company/brand pages** (`/about`, etc.) for Overhaul Trading live **inside the storefront app** — they are the store's brand. (A separate ScoutHaul SaaS marketing site is parked, Phase 11.)

---

## 3. Surfaces

| Surface | Stack | Audience | Auth | Notes |
|---------|-------|----------|------|-------|
| Mobile | Expo / React Native (existing) | Operators in the field | Supabase | Sourcing, scanning, comps, inventory capture. Already built. |
| Storefront | Next.js 15 (App Router, RSC, ISR) | Public + customers | Supabase (customer pop.) | Per-tenant, SEO-critical, CDN-cached. `overhaultrading.com`. |
| Admin | Next.js 15 (App Router, mostly client/data-dense) | Operators/staff | Supabase (operator pop.) | RBAC-gated back-office. `admin.overhaultrading.com`. |
| Command Center | A tier **inside Admin** (role-gated), split out only if needed | Owner/Manager | Supabase | Real-time ops cockpit: live orders, KPIs, sourcing pipeline, channel health, alerts. |
| ~~Platform Admin~~ | — | — | — | **Parked** — only if ScoutHaul ever goes public (Phase 11). |
| ~~Marketing (SaaS)~~ | — | — | — | **Parked** — no scouthaul.com needed; ScoutHaul is internal. |

**Command Center clarification:** it is *not* a separate product — it's the high-signal overview layer of Admin (think "mission control" home screen + real-time widgets). Building it as a separate app now would duplicate auth/data/deploy for no gain.

---

## 4. Backend: modular monolith

Keep the existing clean-architecture layering (`Domain` / `Application` / `Infrastructure` / `Api`). Organize the domain into **bounded-context modules** (folders/namespaces, not separate services). Microservices are explicitly **out of scope** until a context proves it needs independent scaling.

### 4a. Existing contexts (keep)
`Organization/RBAC` · `Sourcing` (SourcingTrip/Stop, SourceLocation, Budget) · `Inventory` (InventoryItem, Photo, Issue) · `Catalog` (ProductIdentity) · `Market` (MarketObservation, MarketAnalysis, BuyRecommendation) · `Listings` · `Sales` · `Tasks` · `Integrations` (eBay, Gemini vision).

### 4b. New commerce contexts — port from the OverhaulTrading Prisma schema

The Prisma schema becomes a **specification**, not a running DB. Map it to .NET aggregates (all `IOrganizationScoped`):

| Prisma model | .NET target | Notes |
|--------------|-------------|-------|
| `Category` | `StoreCategory` | Public storefront taxonomy; distinct from internal `Category` strings on inventory. |
| `Product` / `ProductImage` | **`StoreListing` (projection of `InventoryItem`)** | Publishing an `InventoryItem` to the storefront creates a `StoreListing` (slug, listPrice, SEO, hero image). No duplicate product table. |
| `ChannelListing` | extend existing `Listing` | Add channel `OwnStorefront` alongside eBay etc. |
| `Order` / `OrderItem` | `Order` / `OrderLine` | New `Commerce` context. On payment, decrement inventory + advance `InventoryStatus`→`Sold` and write a `Sale`. |
| `Cart` / `CartItem` | `Cart` / `CartLine` | Guest carts keyed by cookie; merge on login. |
| `WishlistItem` | `WishlistItem` | Customer-scoped. |
| `Review` | `Review` | Moderated; tenant-scoped. |
| `RepairJob` | `RepairOrder` | `Repairs` context. Intake from storefront, manage in admin. |
| `ConsignmentSubmission` | `Consignment` | Intake → approval → becomes `InventoryItem`. |
| `PawnTransaction` | `PawnTicket` | `Lending` context. Regulated — see §9 security. |
| `Referral` | `Referral` | Growth. |
| `Notification` | `Notification` | Plus email/push fan-out. |
| `Supplier` | `Supplier` | Already conceptually present via sourcing; reconcile. |
| `User`/`Address` | reconcile with `AppUser` + new `Customer`/`Address` | Operators = AppUser; shoppers = Customer. |
| Blog (tRPC `blog` router) | `Content`/`Blog` context | Tenant CMS for storefront blog. |

**Key unification:** the resale `InventoryItem` is the single physical-goods record. eBay listings, the storefront listing, and the eventual `Sale` all hang off it. This is the whole point of merging — one item, every channel, one lifecycle.

---

## 5. Multi-tenancy & security hardening (do this early)

1. **EF Core global query filters.** Today scoping is manual via `ICurrentUserContext` → one forgotten `.Where` = cross-tenant data leak. Add a global query filter on every `IOrganizationScoped` entity keyed to the current org. Apply automatically in `SaveChanges` for inserts too. **This is the #1 correctness/security task.**
2. **Customer ≠ Operator isolation.** **Two Supabase projects** (operators invite-only, customers open-signup) — see §6. The API validates both issuers and maps customer tokens only to `Customer`, operator tokens only to `AppUser`/`OrganizationMember`. Authorization policies enforce the split; a customer token is rejected by every operator/admin endpoint.
3. **Rotate the committed secret.** `appsettings.Development.json` contains a Supabase project URL + `JwtSecret`. Rotate it, move to user-secrets / env / Supabase dashboard, and `.gitignore`-verify. Never commit secrets.
4. **Storefront host trust.** Resolve tenant from host in middleware, but the API re-derives tenant from the authenticated token / signed tenant context — host header alone is never authoritative.
5. **PCI:** card data never touches our servers — Stripe Elements / hosted checkout only.
6. **Keep** the existing idempotency middleware, security headers, rate limiting; extend rate limits per-tenant.

---

## 6. Auth (Supabase, consolidated)

- **One provider: Supabase Auth.** Already integrated server-side (`Authentication:Supabase`). Free to 50k MAU; Pro ($25/mo) removes project pausing + raises limits.
- **Drop Clerk and Prisma `User.clerkId`.** Migrate OverhaulTrading's Clerk sign-in/up pages to Supabase (`@supabase/ssr` for Next.js).
- **Two populations, two Supabase projects** — an **operators** project (invite-only, used by mobile + admin) and a **customers** project (open self-signup, used by the storefront). The .NET API validates both issuers and maps operator tokens only to `AppUser`/`OrganizationMember` and customer tokens only to `Customer`. This guarantees a public-signup account can never resolve to operator scope. (Free tier allows 2 projects.)
- **Operator org-membership** stays in our DB (`OrganizationMember`), resolved after token validation (`OrganizationAccessService`).
- **Mobile** keeps its current Supabase flow unchanged.

**Clerk cost answer:** Clerk has a free tier (~10k MAU) but its B2B *Organizations* features are gated to paid plans, and adopting it means **operating two identity systems**. Supabase is already in the backend and free to a higher ceiling — consolidating is both cheaper and simpler.

---

## 7. API contract & client generation

- API emits an **OpenAPI** document (already a .NET strength).
- Generate a **typed TS client** consumed by storefront + admin (and optionally mobile). Recommended: `openapi-typescript` + `openapi-fetch` (tiny, typed, no runtime bloat). Alternative: **Kiota** (Microsoft, heavier, fully typed SDKs).
- The generated client lives in `packages/api-client` and is regenerated in CI when the spec changes.
- **Drop tRPC** — it needs a TS backend; our backend is .NET. Next.js route handlers / server actions call the .NET API server-side; the browser calls it via the typed client.

---

## 8. Frontend architecture & design language

### 8a. Shared foundation (`packages/ui`)
- Design-token system (color, type scale, spacing, radius, shadow, motion) as CSS variables + Tailwind theme, **per-tenant themeable** (storefront reads tenant brand tokens at runtime).
- Headless + styled component library (build on Radix primitives already implied by the stack). Storefront and admin share primitives but have distinct skins.
- Accessibility budget: **WCAG 2.1 AA**. Keyboard-complete, visible focus, contrast ≥ 4.5:1, motion-reduce respected.

### 8b. Storefront ("impressive and advanced")
Design direction: **premium editorial resale** — large imagery, generous whitespace, confident type, restrained motion. Not a generic Shopify clone.
- App Router + **RSC + ISR**; product pages statically generated, revalidated on inventory change (webhook/tag revalidation).
- Advanced features: faceted/instant search, visual-first product cards, quick-view, real-time stock ("1 left"), saved/wishlist, recently-viewed, rich PDP (condition report from `InventoryIssue`, measurements, provenance/`SourceType`), trust signals (reviews), repair-request and consignment intake flows.
- Performance budget: **LCP < 2.0s, CLS < 0.05, INP < 200ms**; images via object store + responsive/AVIF; route-level code splitting.
- SEO: per-product metadata, JSON-LD `Product`/`Offer`, sitemaps per tenant, canonical per custom domain.

### 8c. Admin / Command Center
- Data-dense, fast, keyboard-driven. Tables with server pagination/filter/sort against the API.
- **Command Center home:** real-time tiles (today's revenue, orders to fulfill, low/aged inventory, sourcing pipeline value, channel listing health, repair/consignment queues, alerts). Live via SSE/WebSocket or polling initially.
- Sections (from existing admin routes, repointed): inventory, orders, customers, suppliers, consignments, repairs, blog/CMS, analytics, settings — plus sourcing/market/budget views surfaced from the existing API.
- RBAC: every view/action gated by `OrganizationRolePermission` via a `RequireOrganizationPermission` check mirrored on the client.

---

## 9. Data, storage, payments, lending

- **Images (Phase 3, not a late migration):** move `InventoryPhoto` off Postgres `bytea` to **object storage + CDN** (Supabase Storage or Cloudflare R2). Store keys + metadata in DB; serve responsive/AVIF via CDN. The storefront's LCP<2s budget depends on this, so it lands with the commerce work, not at the end. Mobile photo capture writes to object storage too.
- **Payments:** **Stripe** (the company's single account) — Checkout/Elements, webhooks drive order state. **Stripe Tax** for automated sales-tax calculation. Stripe **Connect** is parked with the public-SaaS phase (Phase 11).
- **Lending/Pawn (`PawnTicket`):** regulated (state pawnbroking law, interest caps, hold periods, police reporting in some jurisdictions). Treat as a **separate, gated phase** (Phase 12); do not ship without compliance review. Out of MVP.

---

## 10. Cross-channel inventory & oversell prevention

A unique item (qty 1) can be published to the **own storefront** *and* **eBay** (and future channels) at once. The hazard: it sells on one channel while still buyable on another → **oversell**. Rules:

- **Single source of truth:** channel listings are projections of one `InventoryItem`; availability derives from `InventoryStatus`, never duplicated.
- **Reserve on checkout:** entering checkout places a short-lived reservation on the item; a second buyer (any channel) sees it unavailable. Reservation expires if checkout is abandoned.
- **Sell = global delist:** payment capture flips status to `Sold` and triggers **delist/decrement on every other channel** (eBay end-listing via integration; storefront marks sold instantly via cache/tag revalidation).
- **`Quantity` support:** add a `Quantity` field to `InventoryItem` (default 1) so qty>1 supplies/multipacks decrement instead of hard-sold — decided in Phase 3 so it's never retrofitted.
- **Concurrency:** reservation + status transitions are guarded against race conditions (row-level lock / optimistic concurrency on the item).

---

## 11. Repo strategy — final: one polyglot monorepo

**Decision: fold the web apps into the ScoutHaul repo as a single monorepo.** Reasoning (you asked me to choose the long-term best):

- **The contract seam wins.** A backend endpoint change, the regenerated TS client, and the UI that consumes it land in **one type-checked PR**. Split repos make this a multi-PR, version-bump dance — the single biggest source of integration bugs, for zero benefit at your team size.
- **Shared TS across web + mobile.** Design system, API client, domain enums (mirrored from C#) are shared once. OverhaulTrading already proves pnpm + Turbo works here.
- **Polyglot is fine.** .NET stays in `services/api` with its own build/test lane; Turbo manages all TS and simply ignores `services/api`. Many enterprise polyglot monorepos do exactly this.
- **You don't lose the exit.** If a context later needs its own repo/service, `git filter-repo` extracts it. Starting unified preserves atomicity now and splitting later; starting split throws atomicity away permanently.
- **Independent deploys preserved** via path-filtered CI: deploy API on `services/**`, storefront on `apps/storefront/**` + shared packages, etc.

### Target layout
```
ScoutHaul/
  apps/
    mobile/                 # Expo (existing, moved under apps/ — already there)
    storefront/             # Next.js public store (from OverhaulTrading/apps/storefront)
    admin/                  # Next.js back-office + command center (from OverhaulTrading/apps/admin)
    marketing/              # (later) scouthaul.com
  packages/
    ui/                     # shared design system + tokens (from OverhaulTrading/packages/ui)
    api-client/             # generated TS client from OpenAPI (replaces packages/api tRPC)
    config/                 # shared tsconfig/eslint/tailwind (from OverhaulTrading/packages/config)
    domain-types/           # shared enums/constants mirrored from C#
  services/
    api/                    # .NET 10 solution (existing, unchanged location)
  db/                       # seed scripts (existing)
  docs/                     # this blueprint + audits
  infra/                    # IaC, Dockerfiles, compose, CI helpers
  package.json              # pnpm workspace root
  pnpm-workspace.yaml
  turbo.json
  docker-compose.yml        # pg + api + web (extended)
```
`packages/database` (Prisma) is **deleted** after data/model port. `packages/api` (tRPC) is **deleted** after client codegen replaces it.

### Tooling
- pnpm workspaces + Turbo for TS (build/lint/test/type-check + affected-graph).
- .NET CLI for `services/api` (`dotnet build/test ScoutHaul.slnx`).
- CI = two lanes (TS via Turbo, .NET via dotnet) + a contract-check job (regenerate client, fail if drift).

---

## 12. Infrastructure, hosting & cost

Two paths. You're cost-sensitive, so both are cheap; pick per phase.

| Component | Scrappy ($0–10/mo) | Solid (~$35/mo) | Notes |
|-----------|--------------------|-----------------| ------|
| Postgres + Auth + Storage | Supabase Free (50k MAU, pauses when idle) | **Supabase Pro $25** (no pause, daily backups, 100k MAU) | One vendor for DB+auth+files = less ops. |
| .NET API host | Fly.io / Railway free-ish / Hetzner VPS €4 | Fly/Railway small ~$7 | Dockerfile already exists. |
| Web (storefront+admin) | Cloudflare Pages/Workers (free) or same VPS | Vercel Pro $20 *(only if you want its DX)* | **Vercel Hobby forbids commercial use** — self-host or Cloudflare to stay free. |
| Images CDN | Cloudflare R2 (10GB free) | R2 / Supabase Storage | Cheap egress. |
| Payments | Stripe (per-txn only) | Stripe | No monthly fee. |
| Domains | overhaultrading.com (owned) | + scouthaul.com (~$12/yr) | SaaS marketing domain. |
| **Est. monthly** | **~$5–10** | **~$32–40** | Excludes Stripe %. |

Recommendation: develop on **Scrappy**, flip to **Supabase Pro** the day Overhaul Trading takes real orders (you need no-pause + backups).

---

## 13. Observability, backups, DR

- **Logs/metrics/traces:** OpenTelemetry from the .NET API → a free/cheap sink (Grafana Cloud free tier, or Seq self-hosted). Structured logging already in place to extend.
- **Errors:** Sentry (free tier) on web + API.
- **Uptime:** a free monitor (e.g. Better Stack / UptimeRobot) on `/health` (endpoint exists).
- **Backups:** Supabase Pro daily backups + periodic `pg_dump` to object storage. Test a restore before launch.
- **DR target:** RPO ≤ 24h, RTO ≤ 4h for MVP; tighten later.

---

## 14. Phased roadmap (tracked)

Each phase has **exit criteria**. Don't advance until met. These map 1:1 to the task list.

**Phase 0 — Foundation & decisions (this doc).**
Exit: blueprint approved; open decisions in §15 answered; monorepo skeleton bootstrapped (pnpm/turbo at ScoutHaul root, web apps moved in, builds green side-by-side with .NET).

**Phase 1 — Security & tenancy hardening.**
EF global query filters on all `IOrganizationScoped` entities; auto-set `OrganizationId` on insert; rotate the committed Supabase secret; customer-vs-operator claim split; tenant-isolation tests.
Exit: a test proving tenant A cannot read tenant B's data; secret rotated and out of git.

**Phase 2 — Contract & auth plumbing.**
OpenAPI client generation into `packages/api-client`; storefront + admin authenticate via Supabase (`@supabase/ssr`), Clerk removed; admin and storefront boot against the real API with a logged-in user.
Exit: both web apps log in via Supabase and call one real API endpoint each.

**Phase 3 — Commerce domain + core infra.**
Add `StoreCategory`, `StoreListing` (InventoryItem projection), `Cart`, `Order`/`OrderLine`, `Customer`, `Address`, `Review`, `WishlistItem`; add `Quantity` to `InventoryItem`; EF config + migrations; controllers + DTOs; publish-to-storefront flow; **checkout reservation + concurrency guard**; Stripe Checkout + **Stripe Tax** + webhooks; **object-storage + CDN image pipeline** (off `bytea`); **transactional email** provider (order/receipt) wired.
Exit: an `InventoryItem` (qty-aware) can be published, reserved, and purchased via Stripe test mode (with tax); inventory decrements, a `Sale` + receipt email are produced, and images serve from the CDN.

**Phase 4 — Storefront (Overhaul Trading).**
Repoint storefront to the API; host→tenant middleware; PDP/catalog/**search (Postgres FTS)**/cart/checkout/account; design-token theming; SEO + performance budgets met; **backups on + restore tested before go-live**; point `overhaultrading.com` DNS.
Exit: `overhaultrading.com` serves real inventory, completes a test purchase, hits LCP<2.0s/CLS<0.05/INP<200ms, and has a verified backup/restore.

**Phase 5 — Admin & Command Center.**
Repoint admin to the API; inventory/orders/customers/suppliers/consignments/repairs/blog/analytics/settings; RBAC gating; Command Center real-time home.
Exit: an operator runs the full sell-side from admin; permissions enforced; command center tiles live.

**Phase 6 — Pricing, promotions & offers.**
Coupons/promo codes; automatic promotions; bundles; **make-an-offer (item + bundle)**; markdown/aging rules; gift cards/store credit; loyalty + referrals.
Exit: an operator launches a coupon and a bundle; a buyer's accepted offer converts to a purchase at the agreed price; discounts resolve to one auditable cart total.

**Phase 7 — Fulfillment, shipping & returns.**
Carrier rates + label purchase + tracking; pick/pack; partial shipments; local pickup; returns/RMA + exchanges; refunds; restocking.
Exit: order goes paid→packed→labeled→shipped→delivered with customer tracking; a return restocks inventory and issues refund/credit.

**Phase 8 — Finance, accounting & reporting.**
Specific-ID COGS; gross/net profit & margin (after fees); expenses & budget-vs-actual; inventory valuation/aging/turnover; cash flow; sales tax; payouts/liabilities; accounting export (QuickBooks/Xero/CSV); full KPI report library + scheduled reports.
Exit: a period P&L reconciles to line items; inventory valuation + tax-collected reports export; Command Center shows live profit.

**Phase 9 — Adjacent & marketing.**
Repairs intake+workflow; Consignment intake→approval→inventory + consignor payouts/statements; Blog/CMS; Reviews moderation; Notifications (email/SMS/push); marketing campaigns + attribution.
Exit: storefront repair/consignment requests flow into admin queues and convert; a campaign's revenue is attributed.

**Phase 10 — Scale hardening.**
Backfill any remaining legacy `bytea` photos to object storage; per-tenant/endpoint rate limits; OpenTelemetry observability + Sentry; search upgrade to Typesense/Meilisearch if FTS outgrown; load test; scheduled-backup + restore drill formalized.
Exit: no image bytes left in Postgres; observability dashboards live; load test passes target throughput.

**Phase 11 — Public SaaS (PARKED — out of current scope).**
Only if ScoutHaul is later opened to outside resellers. Tenant self-onboarding; Stripe Connect; platform super-admin; billing/subscriptions; wildcard + custom-domain automation; marketing site. Multi-tenant seams already in place make this additive later.

**Phase 12 — Lending/Pawn (GATED).**
Only after legal/compliance review. Out of default scope.

---

## 15. Business decisions — RESOLVED (2026-06-14)

1. **Scope** — ScoutHaul is an **internal company tool**, not public SaaS. SaaS surfaces parked (Phase 11).
2. **SaaS domain** — none needed. Admin → `admin.overhaultrading.com`.
3. **Payments** — Stripe single-account (the company's) + Stripe Tax. Connect parked with Phase 11.
4. **Hosting** — start Scrappy (Cloudflare/Fly + Supabase Free), flip to Supabase Pro at first real order.
5. **Pawn/lending** — **out**, gated to Phase 12 (compliance-first).
6. **Repo merge** — copy into ScoutHaul (OverhaulTrading has **no git history** — nothing to subtree-merge); leave OverhaulTrading in place as a backup until the monorepo builds, then delete.

Remaining low-stakes defaults (proceeding unless you object): object storage = Supabase Storage; API client codegen = `openapi-typescript` + `openapi-fetch`; transactional email = Resend/Postmark; search = Postgres FTS → Typesense; CI = Turbo lane + .NET lane + contract-drift check + a **staging environment** before prod.

---

## 16. Guiding principles

- **One source of truth.** The .NET API owns all state. Surfaces are dumb clients.
- **Tenant isolation is non-negotiable.** Enforced in the data layer, not by remembering to filter.
- **Architect multi-tenant, ship single-tenant.** Build the seams now; deliver Overhaul Trading's value first.
- **Cheap until it earns.** Free tiers through development; pay only where a real order demands it.
- **No premature microservices.** Modular monolith until a context proves it must split.
- **Salvage, don't hoard.** Keep OverhaulTrading's UI; delete its data layer without sentiment.
