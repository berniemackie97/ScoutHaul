# Storefront Commerce — purchase system design

Status: **approved design, build in progress** (2026-07-16)
Owner: storefront go-live arc. Companion to `COMMERCE_CONSOLIDATION.md` (fulfillment SSOT = `Order`).

## 1. Requirements

**Functional.** A shopper with no account can: add one-of-one items to a cart, check out, pay by card /
Apple Pay / Google Pay, and receive a confirmation with an order number. The resulting order is a normal
`Order` (`Channel = "Storefront"`) in the same admin Orders surface used for every channel — fulfillment,
refunds, and returns already exist there. The purchased listing shows Sold on the storefront; the backing
`InventoryItem` leaves sellable stock.

**Non-functional.** Inventory is specific-identification, quantity 1 per listing — so the hard requirement
is **contention correctness, not throughput**: two shoppers must never both pay for the same item. Money
paths must be idempotent (Stripe retries webhooks for days). PCI scope stays SAQ-A. No new infrastructure:
Railway API + Postgres, Vercel storefront, existing Supabase projects.

**Constraints.** Solo operator. Additive migrations only. The layering contract holds: use cases in
`Application/Storefront` behind ports; Stripe concrete only in Infrastructure; controllers stay HTTP
adapters. Stripe is already committed in the schema (`Order.StripeCheckoutSessionId` / `StripePaymentIntentId`).

## 2. High-level design

```
Browser ──(shop)── Next.js storefront (Vercel)
   │ cart cookie (httpOnly, minted by Next route handler)
   ▼
Next server ──X-HaulScout-Store + X-HaulScout-Cart──▶ API /api/storefront/*
                                                        │
              ┌─────────────────────────────────────────┤
              ▼                                         ▼
        Stripe Checkout (hosted page)            Postgres (Cart/Order/StoreListing)
              │ pay
              ▼
        Stripe ──webhook: checkout.session.completed / expired──▶ API /api/storefront/stripe/webhook
                                                                    │ finalize / release
                                                                    ▼
                                                       Order Paid · listings Sold · cart Converted
```

**Purchase flow.**
1. **Cart** — server-side `Cart` keyed by an `AnonymousId` token in an httpOnly cookie owned by the Next
   app. Lines snapshot `ListPrice` at add time; a listing may appear in many carts (adding reserves nothing).
2. **Checkout** — `POST /api/storefront/checkout`: inside one transaction, atomically flip every line's
   listing `Active → Reserved` (guarded update — see §4), create the `Order` (`Pending`/`Pending`, lines with
   `UnitCostSnapshot` from the item), create the Stripe Checkout Session (`expires_at` ≈ 30 min, metadata
   `orderId`, success URL carries `session_id`), save `StripeCheckoutSessionId`, return the session URL.
3. **Pay** — shopper pays on Stripe's hosted page (cards + wallets, receipt email from Stripe).
4. **Webhook** — signature-verified, idempotent:
   - `checkout.session.completed` → order `Paid` (+`PaidAt`, `StripePaymentIntentId`, ship-to + email from
     the session), listings `Reserved → Sold`, inventory `Sold` (bundle propagation applies), cart
     `Converted`, `Customer` created/attached by email with a Storefront channel identity.
   - `checkout.session.expired` → this order's still-`Reserved` listings return to `Active`; order `Cancelled`.
5. **Confirmation** — success page reads the order by `session_id` (unguessable) and shows the order number.

## 3. API surface (all anonymous, store resolved by `X-HaulScout-Store`)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/storefront/cart` | Current cart, priced + validated (`X-HaulScout-Cart` token) |
| POST | `/api/storefront/cart/lines` | Add a listing (409 if not `Active`); mints cart when token absent |
| DELETE | `/api/storefront/cart/lines/{storeListingId}` | Remove a line |
| POST | `/api/storefront/checkout` | Reserve + create order + Stripe session → `{ checkoutUrl }` |
| POST | `/api/storefront/stripe/webhook` | Stripe events (raw body + signature; no store header — org from order) |
| GET | `/api/storefront/orders/by-session/{sessionId}` | Confirmation-page order summary |
| POST | `/api/storefront/offers` | Capture a buyer offer lead; does not reserve inventory or move money |

Next.js keeps thin `/api/cart`, `/api/checkout`, and `/api/offers` route-handler proxies. The main job is
cookie custody: the cart token lives in a `httpOnly` cookie and is forwarded to the API as
`X-HaulScout-Cart`, never exposed to browser JavaScript.

## 4. The one-of-one contention guard

Reservation is a **guarded bulk update, not a read-then-write**:

```sql
UPDATE store_listings SET status = 'Reserved'
WHERE id IN (@lineListingIds) AND status = 'Active' AND organization_id = @org
```

If the affected-row count ≠ line count, roll back and return 409 with the names of the lines that lost the
race — the winner keeps its reservation. Postgres row-level locking makes concurrent updates serialize;
whichever transaction commits first wins, the loser's predicate no longer matches. No advisory locks, no
serializable isolation needed. (EF: `ExecuteUpdateAsync` inside the transaction, in the Infrastructure store.)

Reserved listings stay visible (PDP shows an honest "Reserved" state) but refuse add-to-cart and checkout.
Expiry is event-driven — Stripe's `checkout.session.expired` (~30 min) releases them; no cron. A defensive
admin release already exists (`unpublish`).

**Webhook edge:** if payment lands on an order whose listing was force-sold elsewhere in the window,
finalization detects the conflict and auto-refunds through the gateway port, marking the order `Cancelled`
with a note. Reservation makes this path near-unreachable; it exists so it is never silent.
**Amount check:** `session.amount_total` must equal the order's `GrandTotal` in cents; mismatch → refuse to
finalize, log loudly.

## 5. Layering (follows the module-move pattern)

```
Application/Storefront/
  CartWorkflowService          — add/remove/read; price + availability rules
  CheckoutWorkflowService      — validation, totals, reservation orchestration, session request
  StorefrontOrderService       — finalize(completed) / release(expired); idempotent state machine
  IStorefrontCartStore, IStorefrontCheckoutStore   — persistence ports
  IPaymentGateway              — CreateCheckoutSession / RefundPaymentIntent / (parse events lives infra-side)
Infrastructure/Storefront/
  EfStorefrontCartStore, EfStorefrontCheckoutStore
  StripePaymentGateway         — Stripe.net; the ONLY place Stripe types appear
Api/
  StorefrontCartController, StorefrontCheckoutController, StripeWebhookController — thin adapters
```

`TimeProvider` everywhere a moment is recorded. Money rules (totals, cents conversion) are pure Application
logic. Tests: pure rules (reservation outcomes, finalize idempotency, amount mismatch) + Testcontainers for
the actual race (two concurrent checkouts, exactly one wins).

## 6. Configuration & rollout

API (Railway): `Stripe__ApiKey`, `Stripe__WebhookSecret`. Storefront (Vercel): no Stripe key — hosted
checkout needs no publishable key. Success/cancel URLs derive from the org's `StoreDomain`.
Dev: `stripe listen --forward-to localhost:5000/api/storefront/stripe/webhook`. Prod: dashboard-registered
endpoint, events `checkout.session.completed`, `checkout.session.expired`.

Current code binds these API env vars with double-underscore environment names:
`Stripe__ApiKey`, `Stripe__WebhookSecret`, optional `StorefrontCheckout__PublicApiBaseUrl`, optional
`StorefrontCheckout__PublicBaseUrlOverride`. Storefront Vercel only needs `NEXT_PUBLIC_STORE_DOMAIN` and
`NEXT_PUBLIC_API_BASE_URL`.

Rollout: test-mode end-to-end (4242… card) → live keys → a real $1 listing bought by the operator → open.

## 7. Trade-offs (explicit)

- **Hosted Stripe Checkout over embedded Payment Element.** SAQ-A, wallets and receipts for free, one fewer
  bespoke money surface. Cost: the payment page is Stripe-branded (logo/accent configurable). Revisit only
  if brand/conversion data demands; the port boundary makes the swap additive.
- **Server cart over client-state cart.** Survives devices/sessions, enables login merge (`AnonymousId` was
  designed for this) and abandoned-cart later. Cost: an endpoint + cookie custody.
- **Reserve at checkout, not at add-to-cart.** Idle carts must not strand one-of-one inventory. Cost: a
  shopper can lose an item at the checkout door — the 409 says so honestly.
- **No tax calculation v1.** Stripe Tax is a dashboard toggle + `automatic_tax` flag behind the gateway when
  registrations exist. Zero until the operator has nexus obligations.
- **Stripe receipts over a transactional-email service v1.** One fewer vendor/key; custom mail later.

## 8. Revisit as it grows

Quantity >1 inventory (guarded update generalizes to a quantity decrement), discount codes
(`DiscountTotal` already on Order), embedded checkout, customer accounts w/ cart merge (Supabase customer
project; `Customer.ExternalSubject` ready), transactional email, abandoned-cart recovery
(`CartStatus.Abandoned` ready), Stripe Tax, webhook alerting.
