# HaulScout Admin Command Center Roadmap

## Mission

The admin is the operating system for Overhaul Trading. It owns every controlled business workflow from sourcing through financial close. The storefront and mobile app are specialized surfaces over the same records; they do not create parallel inventory, customer, order, or accounting truth.

## Product rules

- Every mutation is authenticated, organization-scoped, permission-checked, validated, and auditable.
- Money is decimal-safe and traceable to source documents and ledger events.
- Inventory, listing, order, shipment, return, and payment states advance through explicit workflows. Operators cannot bypass required financial or fulfillment records by changing a label.
- Destructive and irreversible actions require confirmation, reason capture, and an audit record.
- Tables use server-side filtering, sorting, pagination, saved views, bulk actions, and exports.
- Reports reconcile to underlying transactions and expose drill-down evidence.
- No placeholder metrics, fake save buttons, duplicate data entry, or fabricated market values.
- Storefront content is managed under Marketing/Content when that module is built. A standalone Blog is not a primary operating module.
- Categories, conditions, colors, ownership, functional state, completeness, authenticity, packaging, units, locations, channels, and other reporting dimensions use API-owned controlled vocabularies. Item forms select canonical values; they do not invent new master data inline.
- Brands, models, warehouse locations, category schemas, and channel accounts have governed create/merge/retire workflows with aliases for imported legacy values. Free text is reserved for inherently unique identifiers or prose such as titles, serials, descriptions, disclosures, and notes.
- Admin, mobile, storefront, imports, and integrations consume the same reference-data contract. The API canonicalizes casing, rejects unknown values, preserves flagged legacy values for correction, and prevents unsafe records from becoming publicly sellable.

## Historical records and channel-accounting invariants

- Authorized operators can create backdated records and correct historical inventory acquisitions, listings, sales, fees, shipments, refunds, expenses, payouts, and tax entries. Historical entry is a first-class workflow, not a database repair procedure.
- A correction never erases the prior state. It records the actor, timestamp, reason, before/after values, source evidence, and record version. Posted periods can be reopened only through an explicit privileged workflow.
- Financial records distinguish effective date, recorded date, source, import identifier, reconciliation status, and accounting period. Reports use effective dates while audit and close controls retain recording history.
- A sale snapshots its specific-item COGS and calculated profit inputs. Later inventory edits do not silently rewrite the sale, but an authorized sale correction can intentionally replace the snapshot and recalculate profit with a complete audit trail.
- Channels are modeled as organization-owned channel accounts, not free-text labels. Listings, orders, sales, refunds, disputes, fees, taxes, shipments, and payouts retain channel/account identifiers plus stable external IDs for idempotent imports.
- Estimated fees and profit remain separate from actual assessed fees and reconciled profit. Variances are visible per sale, order, payout, account, channel, and reporting period.
- Payout reconciliation ties channel settlements to their component sales, fees, refunds, reserves, adjustments, and bank deposits. Unmatched and partially matched amounts remain in an exception queue.
- Every tenant-scoped key, import, report, job, webhook, and audit query is isolated by organization. Adding organizations or multiple accounts for the same channel must not require schema or API redesign.

## Information architecture

### Command Center

- Revenue, gross profit, net profit, cash, inventory value, liabilities, tax collected, and budget variance.
- Orders requiring action, fulfillment SLA, returns, aged inventory, repair/consignment queues, low cash, and exception alerts.
- Targets, goals, forecasts, owner scorecards, and drill-down from every KPI.

### Inventory and warehouse

- Intake, identification, acquisition cost, valuation, condition, defects, measurements, provenance, storage/bin location, photos, documents, and notes.
- Controlled lifecycle: research, purchased, preparation, ready, listed, reserved, sold, packed, shipped, delivered, returned, donated, disposed.
- Bulk edit, CSV import/export, labels/barcodes, stock counts, adjustments, aging, turnover, dead-stock actions, and full item history.
- One record publishes to the storefront and external channels without re-entry or overselling.

### Sourcing and purchasing

- Sourcing trips, suppliers, purchase orders, receipts, landed costs, mileage, routes, budgets, approvals, and expected-vs-realized ROI.
- Receiving reconciles purchased items, cash movement, expense evidence, and inventory cost basis.

### Catalog, pricing, and channels

- Store categories, listing readiness, publication, channel status, price history, markdown rules, offers, bundles, promotions, and channel fee models.
- Listing quality checks prevent publication with missing price, condition, disclosures, or required photography.

### Orders, fulfillment, and returns

- Order timeline, payment/tax status, fraud/risk flags, pick lists, packing slips, shipping labels, tracking, partial shipments, local pickup, and delivery confirmation.
- Cancellation, return merchandise authorization, refunds, exchanges, restocking, write-offs, and customer notifications.
- Every transition is idempotent and webhook-safe.

### Customers and service

- Customer identity, addresses, consent, orders, lifetime value, returns, repairs, consignments, notes, communication history, segments, and service issues.
- Customer data is isolated from operator identities and protected by least-privilege access.

### Repairs and consignments

- Repair intake, diagnosis, estimate approval, parts/labor, work stages, invoices, payments, and customer updates.
- Consignment intake, contracts, ownership, split terms, listing attribution, liabilities, statements, and payouts.

### Finance, accounting, and tax

- Specific-identification COGS, landed cost allocation, revenue, discounts, refunds, fees, expenses, liabilities, payouts, and double-entry-ready ledger events.
- Budgets, cash flow, inventory valuation, aging reserves, P&L, balance-sheet inputs, sales-tax liability, 1099-K reconciliation, and period close.
- Receipt/document attachment, approval controls, reconciliation status, accounting exports, and immutable audit history.

### Reports, targets, and goals

- KPI library across sales, margin, inventory, customers, sourcing, fulfillment, returns, repairs, consignment, staff, and channels.
- Date comparisons, dimensions, saved reports, scheduled delivery, CSV/PDF export, targets, forecasts, variance explanations, and drill-down.

### Settings and governance

- Store identity, branding, contact details, domains, policies, tax, shipping, payments, notifications, integrations, numbering sequences, and document templates.
- Staff, roles, teams, granular permissions, approvals, sessions, security events, API keys, webhooks, retention, backups, and audit log.

## Delivery sequence and exit criteria

### 1. Inventory control

Complete detail/edit, photos, defects, status workflow, listing readiness, history, bulk operations, pagination, saved views, archive/restore, and labels.

Exit: an operator can receive, prepare, locate, value, publish, and audit an item without using SQL or the mobile app.

### 2. Financial foundation

Introduce an auditable cash/ledger event model, governed historical-entry and correction commands, document attachments, expenses, budgets, approvals, item cost allocation, inventory valuation, channel accounts, actual-versus-estimated fee capture, payout matching, exception queues, and reconciled dashboard metrics.

Exit: every dollar of inventory acquisition, operating expense, sale, fee, refund, and payout has a traceable source and report impact; authorized corrections preserve complete version history; channel payout totals reconcile to bank deposits or remain as explicit exceptions.

### 3. Commerce and fulfillment

Complete customer/order/payment models, Stripe and tax webhooks, reservation/oversell protection, order operations, label purchase, tracking, returns, receipts, and notifications.

Exit: a paid order moves through delivery or return with inventory, customer, tax, cash, and profit records synchronized.

### 4. Business operations

Complete sourcing, suppliers, purchase orders, repairs, consignments, staff workflows, task queues, and approval controls.

Exit: all recurring operating workflows run inside HaulScout with ownership, due dates, evidence, and audit trails.

### 5. Intelligence and governance

Complete reports, targets, forecasting, scheduled exports, tax packages, accounting integration, security administration, observability, backup/restore testing, and performance hardening.

Exit: management can operate, measure, reconcile, audit, and recover the business from the command center.
