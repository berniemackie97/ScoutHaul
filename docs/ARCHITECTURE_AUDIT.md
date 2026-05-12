# Architecture Audit

Date: 2026-05-12

## Scope

This stabilization pass focused on architecture, correctness, maintainability, developer experience, and test coverage. It intentionally avoided major new product features, paid services, AI dependencies, marketplace scraping, and stack changes.

## What Improved

- Kept the existing repo shape: `apps/mobile` for Expo and `services/api` for the ASP.NET Core API. No disruptive folder moves were needed.
- Centralized core resale calculations in application services:
  - `DealAnalyzer` remains the deterministic buy-analysis entry point.
  - `MarketValuation` now owns sold-comp versus active-listing valuation rules.
  - `ResaleFinancialCalculator` now owns realized sale profit, ROI, and margin math.
- Added `InventoryStatusRules` so dashboard lifecycle calculations do not duplicate status buckets inline.
- Hardened API startup:
  - Swagger is development-only.
  - Production uses the exception handler/problem details path.
  - Production no longer carries a committed default database connection string.
  - Development connection string matches Docker Compose host port `5433`.
- Added `.env.example` files for the API and mobile app.
- Cleaned mobile package metadata and synchronized `package-lock.json`.
- Improved mobile API error parsing for string, problem-details, and validation responses.
- Added typed mobile access for sourcing trip summaries.
- Fixed the reusable `TextField` wrapper so compact row layouts can flex the whole field.

## Bugs Fixed

- Corrected target-margin deal analysis. The previous max-buy calculation ignored platform fees, payment fees, shipping, supplies, and risk reserve when applying target margin. Target margin is now based on net profit after variable costs.
- Rejected negative deal-analysis fixed/cost inputs instead of silently clamping them.
- Market valuation now prefers current sold comp median over stale/manual inventory estimated value.
- Active listings and manual active observations remain demand context and are not used as sold-price valuation.
- Market observations now validate referenced product/inventory IDs against the current organization.
- Sold observations normalize `SoldAt`; active observations clear `SoldAt`.
- Sale recording now rejects duplicate non-returned sales for the same inventory item.
- Sale recording now rejects a listing ID that belongs to a different inventory item.
- Realized sale profit/ROI/margin now use centralized decimal-safe rounding.
- Budget summary expenses are scoped to the active budget period instead of all historical expenses.
- Mobile deal analyzer no longer forces `ManualEstimate` pricing basis by default; `Auto` lets the API derive basis from comps/internal history.
- Mobile comp capture now blocks unattached sync attempts because the API requires a product or inventory link.
- Mobile budget, task, trip, and inventory status actions now handle API errors instead of dropping unhandled promise failures.
- Marketplace links screen now updates local query state when route params change.

## Tests Added

- Deal analyzer:
  - target margin ceiling after variable costs
  - missing/unavailable pricing
  - negative cost input rejection
- Market valuation:
  - sold comp median beats manual inventory estimate
  - active listings do not produce valuation
  - explicit expected price defaults to manual estimate
- Confidence scoring:
  - active-listing-only input stays capped under unavailable confidence
- Realized sale financials:
  - net profit, ROI, margin, and total cost calculations

## Remaining Risks

- Authentication is still MVP stub-header auth. Organization scoping exists in most business endpoints, but real auth/authorization must replace this before multi-user production use.
- Controllers still contain validation logic. It is acceptable for the MVP, but repeated validation should move toward explicit request validators or command handlers as endpoint count grows.
- OpenAPI response metadata is basic; many endpoints return `200` in Swagger even when runtime returns `201`, `400`, `404`, or `409`.
- EF queries are acceptable for MVP volume, but dashboard/list endpoints still load more rows into memory than a production analytics view should.
- Inventory lifecycle transitions are still permissive. The app needs explicit transition rules before warehouse-like workflows or multi-user operations.
- Offline sync is a simple queue. It does not yet have idempotency keys, server-side duplicate detection, conflict handling, or local draft reconciliation after successful sync.
- Money is decimal-safe on the backend, but mobile SQLite stores local draft amounts as `real`. That is acceptable for drafts but not for authoritative accounting.
- npm audit reports moderate Expo transitive advisories. `npm audit --audit-level=high` passes; force fixing currently suggests a breaking Expo downgrade and was not applied.

## Technical Debt

- DTO/request contracts are concentrated in one large file. Keep it for now, but split by feature as contracts grow.
- Several controllers still use ad hoc `BadRequest(string)` responses. Move toward consistent problem-details responses.
- Product identity creation/search is still minimal and does not enforce uniqueness by barcode/SKU.
- Listing and sale flows are backend-ready but not fully surfaced in mobile UX.
- Budget updates are expense-driven only; inventory purchase and sale cash events are not yet unified into a ledger.
- Mobile screens share patterns manually. As forms grow, introduce small feature hooks, not a large state framework.

## Recommended Next Refactors

1. Add real authentication and authorization middleware, then remove default stub auth from non-development environments.
2. Introduce idempotency keys for offline sync writes, especially inventory creates, comps, expenses, and sales.
3. Add explicit inventory/listing/sale state-transition rules and tests.
4. Move repeated controller validation into small feature-level validators or command handlers.
5. Add pagination parameters to list endpoints and aggregate dashboard numbers in database queries.
6. Model cash ledger events so budget, inventory purchases, expenses, refunds, and sales reconcile consistently.
7. Add integration tests around org scoping and market-analysis persistence once test database infrastructure is in place.

## Route-Planning Readiness

The codebase is closer to ready for the sourcing route feature, but not fully ready for a heavy route-planning implementation.

Ready:

- Repo structure is clear enough.
- Sourcing trip, source location, inventory item, and budget concepts are separated enough to extend.
- API/mobile configuration is more predictable.
- Market valuation and deal math are safer and tested.

Not ready:

- `SourceLocation` has address fields but no geocoding coordinates, normalization, or validation.
- There is no route entity, stop ordering, travel estimate model, or local/offline conflict strategy for route edits.
- Auth and org membership remain stubbed.

Recommendation: add the route-planning feature only after a small preparatory refactor that adds route/stop domain entities, source-location coordinate fields, idempotent sync keys, and lifecycle tests. Do not start by embedding route state inside `SourcingTrip` as loose JSON.
