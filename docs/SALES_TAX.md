# Sales tax: Florida DR-15

How ScoutHaul prepares the business's Florida sales and use tax return, knows which returns are owed,
and keeps a copy of what was filed. First arc of the tax work; federal income tax (Schedule C, COGS,
1099-K reconciliation) and the compliance calendar follow as their own slices.

The output is a **worksheet**, not a return. DR-15N: "you may not use software to create paper
(alternative or substitute) returns". The operator transfers the lines into Florida eServices (or
the personalized paper DR-15) and records the filing here.

## Sources (read 2026-10-19)

| Source | What it settles |
|---|---|
| DR-15 R. 01/25 | Return layout: Lines A–E × columns 1–4, Lines 5–14, back-page Lines 15(a)–21 |
| DR-15N R. 10/25 (eff. 12/25) | Line instructions, rounding, collection allowance, penalty, due dates, amended returns, 3-year retention, final return within 15 days of closing. Commercial rentals (Line C) repealed 10/1/2025 |
| DR-15DSS R. 11/25 | 2026 county surtax rates, components with effective/expiration dates |
| DR-659 R. 01/26 | 2026 e-payment deadlines and the state/federal holiday schedule |
| TIP 26ADM-02 | Interest: daily factors by interval and the day-count method |
| TIP 21A01-03, s. 212.05965(4)(a) F.S. | Marketplace sellers **exclude** sales made through a certifying marketplace provider from their return |
| s. 212.11(1) F.S. | Frequencies: quarterly ≤ $1,000, semiannual ≤ $500, annual ≤ $100 of tax over the prior four quarters; alternate quarters ending Feb/May/Aug/Nov and halves ending May/Nov; a return for every period even with no tax due |
| s. 213.756(1) F.S. | Tax collected is held in trust for the state: over-collected tax is owed unless refunded to the buyer |
| Rule 12A-1.045 F.A.C. | Separately stated delivery charges the buyer can avoid (local pickup offered) are not part of the taxable price |

## What goes on the return

All 102 production sales to date are eBay, Depop, Poshmark or Vinted. Each is a registered Florida
marketplace provider that collects and remits the tax, so **those sales are excluded** — not entered as
exempt sales, left off entirely (s. 212.05965(4)(a)). They still appear on the worksheet's exclusion
schedule, by channel, as the audit trail for why the return is smaller than the bank deposits.

What the business reports:

- **Line A** — sales where the business is the dealer: storefront (Stripe Tax collects under the
  business's registration), local/in-person, Craigslist, wholesale, and any marketplace sale the
  platform did not collect on.
  - Column 1 gross: item price after discounts + shipping charged. Tax collected is never included.
  - Column 2 exempt: deliveries outside Florida, sales for resale (Annual Resale Certificate on file),
    sales to exempt organizations (DR-14), and avoidable separately stated shipping.
  - Column 4 tax due: per sale, `round(taxable × 6% + min(taxable, $5,000) × destination county surtax)`,
    half-up at the cent (DR-15N: carry to the third decimal, round up when it is 5 or more). When the
    buyer was charged more than that, the amount charged is due (s. 213.756). Either difference is
    flagged.
- **Line B** — use tax on taxable purchases used by the business that no seller taxed (and inventory
  bought for resale then used). Entered as use-tax entries at the business county rate unless stated.
- **Lines C, D, E** — commercial rentals (repealed), transient rentals, vending: not applicable, zero.
- **Line 6** — tax refunded to buyers on reported sales, dated when refunded; credit for tax paid to
  another state on Line B purchases; manual deductions. Capped at Line 5; the excess carries forward.
- **Line 8 / 9** — estimated tax applies only after $200,000 of state tax in the prior fiscal year.
  Manual entry, normally zero. Line 8 is capped at Line 7.
- **Line 10** = max(0, 7 − 8 + 9).
- **Line 11** — collection allowance, e-file and e-pay on time only: 2.5% of the first $1,200 of
  Line 10, max $30. Zero when donated to education.
- **Line 12** — late return or payment: 10% of Line 10, minimum $50 even with no tax due.
- **Line 13** — late payment: Line 10 × days late × daily factor, day count from the day after the
  late-after date through the payment date inclusive, one rounded segment per rate interval.
- **Line 14** — on time: 10 − 11. Late: 10 + 12 + 13.
- **15(a)** taxable amount over $5,000 per item; **15(b)** taxable amounts delivered into counties with no
  surtax; **15(c)** taxable amounts at a surtax rate other than the business county's; **15(d)** surtax
  included in column 4. Lines 16–21 do not apply to this business and stay zero.

### Where a sale was delivered

In order: an operator decision on the sale → Stripe Tax's county jurisdiction for storefront orders
→ the order's ship-to (outside Florida = exempt; Florida local pickup = business county) → in-person
channels with no address = business county (shown as an assumption). A Florida delivery with no
county blocks the return until someone decides it.

## Periods and deadlines

The registration effective date and a frequency schedule (frequency + alignment, each with the date it
starts) generate every period owed from registration to today, plus the next one. A period's return is:

- **due** the 1st of the month after it ends;
- **late after** the 20th, moved forward past weekends and state or federal holidays;
- for electronic payment, on time only if initiated by **5 p.m. ET on the business day before the
  late-after date** (matches all twelve 2026 dates on DR-659).

Holidays are Florida's (s. 110.117) plus federal (Presidents' Day, Juneteenth, Columbus Day), Saturday
observed Friday and Sunday observed Monday, with Thanksgiving Friday. Closing the business produces a
final period ending on the closing date, late after 15 days.

Status per period: open → ready to file → due soon (7 days) → overdue; or filed / filed late /
amended; and **changed since filed** when the source data no longer matches the filed snapshot.

## Filing record

Filing stores an immutable snapshot: every line, every transaction that fed it (included and excluded,
with the reason), the inputs (rates, settings), a fingerprint of the sources, who filed, when, how
(e-file, paper, telefile), the confirmation number, amount and date paid. A later change to any source
row changes the fingerprint and the period shows "changed since filed" with the line-by-line difference.
An amended return is a new complete return that replaces the earlier one (DR-15N: amended replacement),
linked to it.

## Documents

- DR-15 worksheet: the form's layout, every line with a copy button for eServices, and the readiness
  checks that must pass first.
- Transaction schedule: each sale and use-tax entry with destination, rates, tax computed and charged,
  and the exclusion schedule by channel. Printable and CSV.
- Filed copy: the snapshot as filed, with confirmation details. Keep three years (DR-15N).

## Slices

1. **This PR** — domain engine (calendar, holidays, surtax table, return math) with the official examples
   as tests; tax profile, use-tax entries, sale tax decisions, filing records; API; admin Tax center.
2. Stripe Tax breakdown captured per storefront order (county, taxable amount, rate) and the expense
   review queue (each purchase: taxed by seller / for resale / owes use tax).
3. Federal: Schedule C worksheet with COGS (Part III), 1099-K reconciliation per platform, quarterly
   estimates.
4. Compliance calendar: Annual Resale Certificate, Sunbiz annual report, DR-405, opt-in reminders.

## Maintenance

Rates and the interest table are data in `Domain/Tax`. Each January: load the new DR-15DSS (surtax),
check DR-659 against the calendar tests, and add the TIP's interest factors (published each January
and July). A period whose dates fall past the loaded tables computes with the last known values and
says so on the worksheet.
