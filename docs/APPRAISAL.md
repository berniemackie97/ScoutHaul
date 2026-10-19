# Appraisal: identify, comp, price

How HaulScout answers "what is this, and what is it worth?" — from a barcode, a photo or typed
details — and how that answer becomes a buy decision and a list price. This is the first arc of the
mobile remodel (see `MOBILE_REMODEL.md`) because everything a buyer does in the field depends on it.

## Where it stood (audit, 2026-10-18)

Production runs on one pricing source: Gemini. No eBay, PriceCharting or UPC provider keys are set,
so every value a user has seen came from a language model.

| Problem | Effect |
|---|---|
| Three pricing engines: Gemini's own low/high on the scanner, `AverageSoldPrice` on lookups, a median of saved observations in the deal analyzer | The same item shows a different value on each screen |
| The vision prompt asks the model for "representative comps from training knowledge" | Invented prices are displayed as comps |
| The scanner saves those as `ObservationType = Sold` with `SoldAt = now` | The deal analyzer would count them as real, recent sales and mark the basis `LiveValidated` (0 rows in prod yet: a landmine, not damage) |
| `AverageSoldPrice` averages sold **and active** listings | Asking prices reported as sale prices |
| PriceCharting comps carry no tier | Dropped from every price statistic |
| `ListingPriceAdvisor` (variant matching, outlier removal, price endings) is used only by listing generation | The best rules never reach the scanner |
| 103 of the business's own sales are stored as `InternalSale` observations, but 0 of 268 items link to a product identity | The most trustworthy comps never appear on a new scan |
| Orchestration lives in the phone: identify, then search, then retry each alternate phrasing, each retry able to trigger a grounded Gemini call with its own server retries | 15–40 s waits, quota burn, and logic duplicated across scanner and manual search |
| Fixed 1.1–1.6 s minimum delays "so it feels deliberate" | Latency added on purpose |
| Deal analyzer uses hard-coded 13% / 3% / $0.30 fees and lets the user pick the pricing basis | Ignores the org's real channel fee profiles; the basis can be claimed rather than earned |

## Vocabulary

- **Identification** — what the item is: title, brand, model, category, color, condition grade,
  identifiers (barcode, card number, model number, size), and how sure we are. Sources: the org's own
  catalog, barcode registries, a photo, typed facts.
- **Comp** — one piece of price evidence, always with provenance.
  - `YourSale` — the business sold a matching item (an `InternalSale` observation). Strongest signal.
  - `Sold` — a realised sale on a marketplace.
  - `Asking` — a current listing. Competition, never a sale price.
  - `Guide` — a price guide aggregate (PriceCharting condition prices).
  - Each comp is `verified` (own sale, marketplace API, or a comp a person recorded) or not (found by
    web search and read by a model — shown, weighted lower, labelled).
- **Estimate** — a model's knowledge-based range. Not a comp, never stored as one. Used only when
  there is no evidence at all, and labelled as a guess.
- **Appraisal** — identification + comps (kept and excluded, with the reason) + a price + confidence.
- **Buy check** — the appraisal's price run through the org's real channel fees and costs: max buy
  price, profit at the asking price, decision.

## Design

### One pipeline, on the server

```
POST /api/appraisals/identify   photo | barcode | details      → Identification
POST /api/appraisals            Identification (+ condition,    → Appraisal
                                asking price, channel)
```

Two calls so the phone can show what the item is the moment it is known, and fill the price in
behind it. Correcting the identification (brand, model, condition) re-prices without re-identifying.

`AppraisalService` (Application) owns the orchestration. Comp sources run concurrently, each under its
own deadline, and report whether they ran, found nothing, or failed — the result says which sources
were asked. Sources are ports (`ICompSource`); adapters live in Infrastructure:

| Source | Kind | Verified | Notes |
|---|---|---|---|
| Your sales | `YourSale` | yes | `InternalSale` observations matched by title similarity, brand and category — not by product identity, which nothing links |
| Saved comps | as recorded | yes | Observations on the product identity, last 365 days |
| Web search (Gemini + Google Search) | `Sold` / `Asking` | no | One grounded call per appraisal, cached 24 h by query |
| eBay Browse | `Asking` | yes | Free developer keys; fixed-price only; barcode first, then the item's name (never the digits); off until keys are set. The old provider was never registered, so this is eBay's first real use |
| PriceCharting | `Guide` | yes | Collectibles; off until a token is set |

### One pricing engine, deterministic

`CompPricing` (pure, unit-tested) replaces `MarketValuation`, `ConfidenceScorer` and
`ListingPriceAdvisor`'s private math. Same input, same answer, explainable line by line.

1. **Match** — `CompMatching` keeps the advisor's rules (graded vs raw, grader and grade, lots,
   language, proxies/reprints, card numbers) and adds condition bands (new / used / for parts),
   title similarity, and model conflicts: "1000XM4" is not "1000XM5", while "WH1000XM5" is
   "WH-1000XM5". Every excluded comp keeps its reason; the phone can show them.
2. **Trim** — outliers removed on log price (Tukey fences from 4 comps; a ×2.5 / ÷2.5 band below).
3. **Price** — from the strongest tier present:
   - Sales (`YourSale`, `Sold`): list = 65th percentile, quick sale = 25th. Weights favour recent,
     verified and own sales.
   - Guide only: the guide price for the matching condition band.
   - Asking only: list = 25th percentile of compatible asking prices; listings sit above sales.
   - Estimate only: the middle of the range; quick sale = the low end.
   Prices end in .99 (advisor's rounding).
4. **Confidence** — 0–100 from the evidence, never from a model's self-report: count, spread,
   recency, verified share, condition match, identification certainty. Capped by basis: asking-only
   45, guide-only 60, estimate-only 25. Shown as Strong (75+), Fair (50+), Thin (25+), Guess.
   Every point carries a reason.

### Honesty rules (enforced in code)

- A model's knowledge never becomes a comp. The vision schema drops `comps`.
- Only real evidence is stored as an observation. The API refuses `Sold`/`ActiveListing`
  observations whose source is a model estimate.
- Asking prices are never averaged into a sale price.
- The price basis is derived from the evidence. Nobody picks it.
- When nothing is known, the answer says so. No minimum delays, no placeholder values.

## Slices

| # | Branch | Scope |
|---|---|---|
| 1 | `bernie/AppraisalEngine` | Server: vocabulary, `CompMatching` + `CompPricing`, comp sources, `AppraisalService`, the two endpoints, honesty guards, tests. Old endpoints stay for installed builds |
| 2 | `bernie/ScoutOnAppraisals` | Mobile: Scout rebuilt on the endpoints — one flow for barcode, photo and typed details; one result screen; refine re-prices; decomposed scanner; no client orchestration |
| 3 | `bernie/BuyCheckWithRealFees` | Buy check on the org's channel fee profiles; deal analyzer folded in; recording a comp you found; marketplace links on the result |
| 4 | `bernie/OneWayToPrice` | Listing generation and the item page price from the same engine; appraisal kept on the item when it is added; retire `MarketValuation`, `ConfidenceScorer`, old lookup endpoints |

Owed outside this arc: eBay developer keys (free) would add real asking prices — a setting for the
user to fill in, not code.
