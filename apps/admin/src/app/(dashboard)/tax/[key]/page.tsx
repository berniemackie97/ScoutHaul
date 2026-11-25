import Link from "next/link";
import type { components } from "@overhaul/api-client";
import { AlertTriangle, ArrowLeft, Download, Info, OctagonAlert } from "lucide-react";
import { Badge, Card, buttonVariants } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { refusal } from "@/lib/page-access";
import { cents, countdown, day, entry, percent, severityTone, statusLabel, statusVariant } from "@/features/tax/tax-format";
import {
  AdjustmentForm,
  CollectorForm,
  CopyValue,
  FilingForm,
  PrintButton,
  RemoveButton,
  SaleDecisionForm,
  UseTaxForm,
  VoidForm,
} from "./worksheet-controls";

export const dynamic = "force-dynamic";

type Worksheet = components["schemas"]["SalesTaxWorksheetDto"];
type Lines = components["schemas"]["Dr15Lines"];
type Issue = components["schemas"]["SalesTaxIssueDto"];

const isDay = (value: unknown): value is string => typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value);

export default async function WorksheetPage({
  params,
  searchParams,
}: {
  params: Promise<{ key: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const refused = await refusal("/tax");
  if (refused) return refused;

  const { key } = await params;
  const query = await searchParams;
  const filedOn = isDay(query.filedOn) ? query.filedOn : undefined;

  const api = await getApiClient();
  const [{ data: worksheet, error }, { data: profile }] = await Promise.all([
    api.GET("/api/sales-tax/periods/{key}", { params: { path: { key }, query: filedOn ? { filedOn } : {} } }),
    api.GET("/api/sales-tax/profile"),
  ]);

  if (!worksheet?.lines || !worksheet.period) {
    const detail = (error as { detail?: string } | undefined)?.detail;
    return (
      <div className="space-y-4">
        <BackLink />
        <div className="border-destructive-border bg-destructive-surface text-destructive rounded-xl border p-6 text-sm">{detail ?? "This period could not be loaded."}</div>
      </div>
    );
  }

  const period = worksheet.period;
  const status = period.status ?? "Open";
  const lines = worksheet.lines;
  const issues = worksheet.issues ?? [];
  const blocking = issues.filter((issue) => issue.severity === "Blocking");
  const warnings = issues.filter((issue) => issue.severity === "Warning");
  const effective = worksheet.filings?.find((filing) => filing.isEffective);
  const counties = profile?.counties ?? [];
  const ended = status !== "Open";
  const telefileEligible = (lines.line5 ?? 0) === 0 && (lines.line6 ?? 0) === 0 && (lines.line8 ?? 0) === 0;
  const today = new Date().toISOString().slice(0, 10);

  return (
    <div className="space-y-6">
      <div className="print:hidden">
        <BackLink />
      </div>

      <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
        <div>
          <p className="text-muted-foreground text-sm">Florida DR-15 worksheet · {worksheet.header?.formVersion}</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">{period.label}</h1>
          <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
            <Badge variant={statusVariant[status]}>{statusLabel[status]}</Badge>
            <span className="text-muted-foreground">
              {day(period.start)} – {day(period.end)} · due {day(period.dueDate)} · late after {day(period.lateAfter)}
              {ended && !effective ? ` · ${countdown(period.daysUntilLateAfter, status)}` : ""}
            </span>
          </div>
          {ended && !effective && status !== "Overdue" ? (
            <p className="text-muted-foreground mt-1 text-xs">
              To pay electronically on time, start the payment and get a confirmation number by 5:00 p.m. ET on {day(period.electronicPaymentDeadline, "long")}.
            </p>
          ) : null}
        </div>
        <div className="flex flex-wrap gap-2 print:hidden">
          <form className="flex items-center gap-2" action={`/tax/${key}`}>
            <label htmlFor="projection" className="text-muted-foreground text-xs">If filed on</label>
            <input id="projection" name="filedOn" type="date" defaultValue={worksheet.timeliness?.filedOn} className="border-input bg-background h-9 rounded-md border px-2 text-sm" />
            <button type="submit" className={buttonVariants({ variant: "outline", size: "sm" })}>Recalculate</button>
          </form>
          <a href={`/api/tax/${key}/schedule`} download className={buttonVariants({ variant: "outline", size: "sm" })}>
            <Download /> Schedule CSV
          </a>
          <PrintButton />
        </div>
      </header>

      <p className="text-muted-foreground rounded-lg border border-dashed px-4 py-2 text-xs">
        Worksheet for review and your records, not a return. DOR does not accept software-made substitute returns: enter these
        figures in eServices (or on your personalized DR-15) and record the filing below. Keep it with the schedule for three years.
      </p>

      {issues.length > 0 ? <Readiness issues={issues} periodKey={key} /> : null}

      {worksheet.changesSinceFiled && worksheet.changesSinceFiled.length > 0 ? (
        <Card className="border-destructive/50 p-5">
          <h2 className="font-semibold">Changed since it was filed</h2>
          <p className="text-muted-foreground text-sm">
            The source data no longer matches what was filed on {day(effective?.filedOn)}. If the changes are real, file an amended
            replacement return in eServices with the complete corrected figures, then record it below.
          </p>
          <table className="mt-3 w-full max-w-md text-sm">
            <thead className="text-muted-foreground text-left text-xs">
              <tr><th className="py-1">Line</th><th className="py-1 text-right">Filed</th><th className="py-1 text-right">Now</th></tr>
            </thead>
            <tbody>
              {worksheet.changesSinceFiled.map((change) => (
                <tr key={change.line} className="border-t">
                  <td className="py-1">{change.line}</td>
                  <td className="py-1 text-right tabular-nums">{cents(change.filed)}</td>
                  <td className="py-1 text-right font-medium tabular-nums">{cents(change.now)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      ) : null}

      <Dr15 worksheet={worksheet} lines={lines} />

      <Timeliness worksheet={worksheet} />

      <Section title="Line A · Sales the business reports" description="Invoices where the business is the dealer: storefront, in-person and any sale no marketplace collected on.">
        {(worksheet.invoices ?? []).length === 0 ? (
          <Empty>No reportable sales this period.</Empty>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[900px] text-sm">
              <thead className="text-muted-foreground bg-muted/40 text-left text-xs uppercase tracking-wide">
                <tr>
                  <th className="px-4 py-2 font-medium">Sale</th>
                  <th className="px-3 py-2 font-medium">Delivered</th>
                  <th className="px-3 py-2 text-right font-medium">Gross</th>
                  <th className="px-3 py-2 text-right font-medium">Exempt</th>
                  <th className="px-3 py-2 text-right font-medium">Taxable</th>
                  <th className="px-3 py-2 text-right font-medium">Rate</th>
                  <th className="px-3 py-2 text-right font-medium">Computed</th>
                  <th className="px-3 py-2 text-right font-medium">Charged</th>
                  <th className="px-3 py-2 text-right font-medium">Tax due</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {(worksheet.invoices ?? []).map((invoice) => {
                  const sales = (worksheet.reportedSales ?? []).filter((sale) => sale.invoiceKey === invoice.invoiceKey);
                  const variance = Math.abs((invoice.computedTax ?? 0) - (invoice.taxCharged ?? 0)) >= 0.005;
                  return (
                    <tr key={invoice.invoiceKey} className="align-top">
                      <td className="px-4 py-3">
                        {sales.map((sale) => (
                          <div key={sale.saleId} id={`sale-${sale.saleId}`} className="scroll-mt-24">
                            <p className="font-medium">{sale.itemTitle}</p>
                            <p className="text-muted-foreground text-xs">
                              {day(sale.date)} · {sale.channel}
                              {sale.orderNumber ? ` · order ${sale.orderNumber}` : ""} · {cents(sale.itemAmount)}
                            </p>
                            <details className="mt-1 print:hidden">
                              <summary className="text-brand-700 dark:text-brand-300 cursor-pointer text-xs">
                                {sale.hasDecision ? "Edit delivery decision" : "Set delivery / exemption"}
                              </summary>
                              <div className="bg-muted/30 mt-2 rounded-lg border p-3">
                                <SaleDecisionForm sale={sale} periodKey={key} counties={counties} />
                              </div>
                            </details>
                          </div>
                        ))}
                      </td>
                      <td className="px-3 py-3">
                        {invoice.county ?? (invoice.treatment?.startsWith("Exempt") ? "Outside FL" : "Unknown")}
                        <p className="text-muted-foreground text-xs">{sales[0]?.destinationSource}</p>
                        <p className="text-muted-foreground text-xs">{invoice.treatment}</p>
                      </td>
                      <td className="px-3 py-3 text-right tabular-nums">{cents(invoice.gross)}</td>
                      <td className="px-3 py-3 text-right tabular-nums">{cents(invoice.exempt)}</td>
                      <td className="px-3 py-3 text-right tabular-nums">{cents(invoice.taxable)}</td>
                      <td className="px-3 py-3 text-right tabular-nums">{(invoice.taxable ?? 0) > 0 ? percent(0.06 + (invoice.surtaxRate ?? 0)) : "—"}</td>
                      <td className="px-3 py-3 text-right tabular-nums">{cents(invoice.computedTax)}</td>
                      <td className={`px-3 py-3 text-right tabular-nums ${variance ? "text-warning" : ""}`}>{cents(invoice.taxCharged)}</td>
                      <td className="px-3 py-3 text-right font-medium tabular-nums">{cents(invoice.taxDue)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Section>

      <Section title="Line B · Use tax" description="Taxable things the business bought and used without paying Florida tax to the seller: equipment or supplies from out-of-state sellers, or inventory taken for the business's own use. Inventory bought to resell is not taxable.">
        {(worksheet.useTax ?? []).length > 0 ? (
          <table className="mb-4 w-full text-sm">
            <thead className="text-muted-foreground text-left text-xs uppercase tracking-wide">
              <tr>
                <th className="py-2 font-medium">Date</th>
                <th className="py-2 font-medium">Purchase</th>
                <th className="py-2 font-medium">County</th>
                <th className="py-2 text-right font-medium">Amount</th>
                <th className="py-2 text-right font-medium">Use tax</th>
                <th className="py-2 print:hidden" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {(worksheet.useTax ?? []).map((use) => (
                <tr key={use.entry?.id}>
                  <td className="py-2">{day(use.entry?.occurredOn)}</td>
                  <td className="py-2">{use.entry?.description}</td>
                  <td className="py-2">{use.result?.county} ({percent(0.06 + (use.result?.surtaxRate ?? 0))})</td>
                  <td className="py-2 text-right tabular-nums">{cents(use.result?.taxable)}</td>
                  <td className="py-2 text-right tabular-nums">{cents(use.result?.tax)}</td>
                  <td className="py-2 text-right print:hidden">
                    <RemoveButton kind="use" id={use.entry?.id ?? ""} periodKey={key} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <Empty>No use tax recorded for this period.</Empty>
        )}
        <div className="mt-4 print:hidden">
          <UseTaxForm periodKey={key} counties={counties} defaultDate={period.end && period.end < today ? period.end : today} />
        </div>
      </Section>

      <Section title="Lines 6, 8, 9 · Deductions and credits" description="Tax refunded to buyers on reported sales is deducted automatically in the period of the refund. Add anything else from DOR's letters.">
        <dl className="mb-4 grid gap-2 text-sm sm:grid-cols-2">
          <Pair label="Tax refunded to buyers" value={cents((worksheet.refunds ?? []).reduce((sum, refund) => sum + (refund.taxRefunded ?? 0), 0))} />
          <Pair label="Deductions carried in" value={cents(worksheet.carry?.deductionsCarriedIn)} />
          <Pair label="Credits carried in" value={cents(worksheet.carry?.creditsCarriedIn)} />
          <Pair label="Carried to next return" value={`${cents(worksheet.carry?.deductionsCarriedOut)} deductions · ${cents(worksheet.carry?.creditsCarriedOut)} credits`} />
        </dl>
        {(worksheet.adjustments ?? []).map((adjustment) => (
          <div key={adjustment.id} className="flex items-center justify-between border-t py-2 text-sm">
            <span>
              <Badge variant="outline">{adjustment.kind === "LawfulDeduction" ? "Line 6" : adjustment.kind === "EstimatedTaxDue" ? "Line 9" : "Line 8"}</Badge>{" "}
              {adjustment.description}
            </span>
            <span className="flex items-center gap-2 tabular-nums">
              {cents(adjustment.amount)}
              <span className="print:hidden"><RemoveButton kind="adjustment" id={adjustment.id ?? ""} periodKey={key} /></span>
            </span>
          </div>
        ))}
        <div className="mt-4 print:hidden">
          <AdjustmentForm periodKey={key} />
        </div>
      </Section>

      <Section
        title="Excluded · Collected by the marketplace"
        description="Left off the return entirely: a registered marketplace provider collected and remitted Florida tax on these (s. 212.05965(4)(a), F.S.). Kept here so the return can be reconciled to deposits and 1099-Ks."
      >
        {(worksheet.excludedChannels ?? []).length === 0 ? (
          <Empty>No marketplace sales this period.</Empty>
        ) : (
          <>
            <table className="w-full text-sm">
              <thead className="text-muted-foreground text-left text-xs uppercase tracking-wide">
                <tr>
                  <th className="py-2 font-medium">Channel</th>
                  <th className="py-2 text-right font-medium">Sales</th>
                  <th className="py-2 text-right font-medium">Gross</th>
                  <th className="py-2 text-right font-medium">Tax the marketplace collected</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {(worksheet.excludedChannels ?? []).map((channel) => (
                  <tr key={channel.channel}>
                    <td className="py-2">{channel.channel}</td>
                    <td className="py-2 text-right tabular-nums">{channel.salesCount}</td>
                    <td className="py-2 text-right tabular-nums">{cents(channel.grossSales)}</td>
                    <td className="py-2 text-right tabular-nums">{cents(channel.taxCollectedByMarketplace)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <details className="mt-3">
              <summary className="text-muted-foreground cursor-pointer text-xs">Every excluded sale ({worksheet.excludedSales?.length})</summary>
              <ul className="mt-2 divide-y text-sm">
                {(worksheet.excludedSales ?? []).map((sale) => (
                  <li key={sale.saleId} id={`sale-${sale.saleId}`} className="flex justify-between gap-3 py-1.5">
                    <span>
                      {day(sale.date)} · {sale.channel} · {sale.itemTitle}
                    </span>
                    <span className="tabular-nums">{cents((sale.itemAmount ?? 0) + (sale.shippingCharged ?? 0))}</span>
                  </li>
                ))}
              </ul>
            </details>
          </>
        )}
      </Section>

      <Section title={effective ? "Filing record" : "Record the filing"} description={effective ? undefined : "After filing in eServices (or by telefile or mail), record it here. The worksheet is saved as filed, and later changes to its sources are flagged."}>
        {(worksheet.filings ?? []).map((filing) => (
          <div key={filing.id} className={`mb-4 rounded-lg border p-4 text-sm ${filing.voided ? "opacity-60" : ""}`}>
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={filing.voided ? "secondary" : filing.isEffective ? "success" : "outline"}>
                {filing.voided ? "Voided" : filing.isEffective ? "In effect" : "Replaced"}
              </Badge>
              <span className="font-medium">{filing.kind === "AmendedReplacement" ? "Amended replacement" : "Original return"}</span>
              <span className="text-muted-foreground">
                filed {day(filing.filedOn)} by {filing.method}
                {filing.confirmationNumber ? ` · confirmation ${filing.confirmationNumber}` : ""}
              </span>
            </div>
            <dl className="mt-3 grid gap-2 sm:grid-cols-3">
              <Pair label="Total tax due (Line 5)" value={cents(filing.totalTaxDue)} />
              <Pair label="Due with return (Line 14)" value={cents(filing.amountDueWithReturn)} />
              <Pair label={`Paid${filing.paidOn ? ` ${day(filing.paidOn)}` : ""}`} value={cents(filing.amountPaid)} />
            </dl>
            {!filing.voided && Math.abs((filing.amountPaid ?? 0) - (filing.amountDueWithReturn ?? 0)) >= 0.005 ? (
              <p className="mt-2 text-xs text-warning">Amount paid differs from Line 14 by {cents((filing.amountPaid ?? 0) - (filing.amountDueWithReturn ?? 0))}.</p>
            ) : null}
            {filing.notes ? <p className="text-muted-foreground mt-2 text-xs">{filing.notes}</p> : null}
            {filing.voided ? <p className="text-muted-foreground mt-2 text-xs">Voided: {filing.voidReason}</p> : null}
            {!filing.voided && filing.isEffective ? (
              <div className="mt-3 print:hidden">
                <VoidForm periodKey={key} returnId={filing.id ?? ""} />
              </div>
            ) : null}
          </div>
        ))}

        {!ended ? (
          <Empty>This period ends {day(period.end)}. It can be filed from the next day.</Empty>
        ) : !effective || status === "ChangedSinceFiled" ? (
          <div className="print:hidden">
            {blocking.length > 0 ? (
              <p className="text-destructive text-sm">Resolve the {blocking.length} blocking item{blocking.length === 1 ? "" : "s"} above before recording a filing.</p>
            ) : (
              <FilingForm
                periodKey={key}
                fingerprint={worksheet.sourceFingerprint ?? ""}
                warningCount={warnings.length}
                amountDue={lines.line14 ?? 0}
                amended={Boolean(effective)}
                defaultFiledOn={worksheet.timeliness?.filedOn ?? today}
                filesElectronically={worksheet.timeliness?.electronic ?? true}
                telefileEligible={telefileEligible}
              />
            )}
          </div>
        ) : null}
      </Section>

      <footer className="text-muted-foreground space-y-1 text-xs">
        <p>Sources: {(worksheet.sources ?? []).join(" · ")}</p>
        <p className="break-all">Source fingerprint {worksheet.sourceFingerprint}</p>
      </footer>
    </div>
  );
}

function BackLink() {
  return (
    <Link href="/tax" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm">
      <ArrowLeft className="h-4 w-4" aria-hidden /> Sales tax
    </Link>
  );
}

function Section({ title, description, children }: { title: string; description?: string; children: React.ReactNode }) {
  return (
    <Card className="p-5 print:break-inside-avoid print:shadow-none">
      <h2 className="font-semibold">{title}</h2>
      {description ? <p className="text-muted-foreground mb-4 mt-1 max-w-3xl text-sm">{description}</p> : <div className="mb-3" />}
      {children}
    </Card>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return <p className="text-muted-foreground bg-muted/20 rounded-lg border border-dashed px-4 py-3 text-sm">{children}</p>;
}

function Pair({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-muted-foreground text-xs">{label}</dt>
      <dd className="font-medium tabular-nums">{value}</dd>
    </div>
  );
}

function Readiness({ issues, periodKey }: { issues: Issue[]; periodKey: string }) {
  const order = { Blocking: 0, Warning: 1, Info: 2 } as const;
  return (
    <Card className="p-5 print:hidden">
      <h2 className="font-semibold">Before you file</h2>
      <ul className="mt-3 space-y-2">
        {[...issues]
          .sort((left, right) => order[left.severity ?? "Info"] - order[right.severity ?? "Info"])
          .map((issue, index) => {
            const severity = issue.severity ?? "Info";
            const Icon = severity === "Blocking" ? OctagonAlert : severity === "Warning" ? AlertTriangle : Info;
            const saleId = issue.saleIds?.[0];
            return (
              <li key={`${issue.code}-${index}`} className={`rounded-lg border px-4 py-3 text-sm ${severityTone[severity]}`}>
                <div className="flex items-start gap-3">
                  <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
                  <div className="flex-1">
                    <p>{issue.message}</p>
                    {saleId ? (
                      <a href={`#sale-${saleId}`} className="text-xs font-medium underline-offset-2 hover:underline">
                        Go to the sale
                      </a>
                    ) : null}
                    {saleId && issue.code === "collector_mismatch" ? (
                      <CollectorForm periodKey={periodKey} saleId={saleId} marketplaceCollected={false} label="Report it myself" />
                    ) : null}
                    {saleId && (issue.code === "reported_marketplace_sale" || issue.code === "collector_unknown") ? (
                      <CollectorForm
                        periodKey={periodKey}
                        saleId={saleId}
                        marketplaceCollected={issue.code === "reported_marketplace_sale"}
                        label={issue.code === "reported_marketplace_sale" ? "Marketplace collected it" : "I collected it; report it"}
                      />
                    ) : null}
                  </div>
                </div>
              </li>
            );
          })}
      </ul>
    </Card>
  );
}

function Money({ value, label }: { value: number | null | undefined; label: string }) {
  return (
    <span className="inline-flex items-center justify-end gap-1 tabular-nums">
      {value === null || value === undefined ? <span className="text-muted-foreground">n/a</span> : entry(value)}
      {value === null || value === undefined ? null : <CopyValue value={entry(value)} label={label} />}
    </span>
  );
}

function Dr15({ worksheet, lines }: { worksheet: Worksheet; lines: Lines }) {
  const header = worksheet.header;
  const rows: { key: string; label: string; row: Lines["lineA"]; note?: string }[] = [
    { key: "A", label: "Sales/Services/Electricity", row: lines.lineA },
    { key: "B", label: "Taxable Purchases", row: lines.lineB, note: "Use tax on internet / out-of-state untaxed purchases" },
    { key: "C", label: "Commercial Rentals", row: lines.lineC, note: "Repealed Oct 1, 2025" },
    { key: "D", label: "Transient Rentals", row: lines.lineD },
    { key: "E", label: "Food & Beverage Vending", row: lines.lineE },
  ];
  const right: [string, string, number | undefined][] = [
    ["5", "Total Amount of Tax Due", lines.line5],
    ["6", "Less Lawful Deductions", lines.line6],
    ["7", "Net Tax Due", lines.line7],
    ["8", "Less Est Tax Pd / DOR Cr Memo", lines.line8],
    ["9", "Plus Est Tax Due Current Month", lines.line9],
    ["10", "Amount Due", lines.line10],
    ["11", "Less Collection Allowance (e-file/e-pay only)", lines.line11],
    ["12", "Plus Penalty", lines.line12],
    ["13", "Plus Interest", lines.line13],
    ["14", "Amount Due with Return", lines.line14],
  ];
  const back: [string, string, number | undefined][] = [
    ["15(a)", "Exempt Amount of Items Over $5,000 (included in Column 3)", lines.line15a],
    ["15(b)", "Other Taxable Amounts NOT Subject to Surtax (included in Column 3)", lines.line15b],
    ["15(c)", "Amounts Subject to Surtax at a Rate Different Than Your County Surtax Rate (included in Column 3)", lines.line15c],
    ["15(d)", "Total Amount of Discretionary Sales Surtax Due (included in Column 4)", lines.line15d],
    ["16", "Florida Tax Credit Scholarship Program Motor Vehicle Sales Tax Credits (included in Line 6)", lines.line16],
    ["17", "Taxable Sales/Untaxed Purchases or Uses of Electricity (included in Line A)", lines.line17],
    ["18", "Taxable Sales/Untaxed Purchases of Dyed Diesel Fuel (included in Line A)", lines.line18],
    ["19", "Taxable Sales from Amusement Machines (included in Line A)", lines.line19],
    ["20", "Rural or Urban High Crime Area Job Tax Credits", lines.line20],
    ["21", "Other Authorized Credits", lines.line21],
  ];

  return (
    <Card className="overflow-hidden print:shadow-none">
      <div className="bg-muted/40 grid gap-3 border-b px-5 py-4 text-sm sm:grid-cols-4">
        <Pair label="Certificate number" value={header?.certificateNumber ?? "Not entered"} />
        <Pair label="Business" value={header?.legalName ?? ""} />
        <Pair label="Reporting period" value={`${day(worksheet.period?.start)} – ${day(worksheet.period?.end)}`} />
        <Pair label="Surtax rate" value={`${percent(header?.surtaxRate)} (${header?.county})`} />
      </div>
      <div className="overflow-x-auto px-5 py-4">
        <table className="w-full min-w-[720px] text-sm">
          <thead className="text-muted-foreground text-xs">
            <tr>
              <th className="py-2 text-left font-medium" />
              <th className="py-2 text-right font-medium">1. Gross Sales</th>
              <th className="py-2 text-right font-medium">2. Exempt Sales</th>
              <th className="py-2 text-right font-medium">3. Taxable Amount</th>
              <th className="py-2 text-right font-medium">4. Tax Due</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {rows.map(({ key, label, row, note }) => (
              <tr key={key}>
                <td className="py-2 pr-3">
                  <span className="font-medium">{key}.</span> {label}
                  {note ? <span className="text-muted-foreground block text-xs">{note}</span> : null}
                </td>
                <td className="py-2 text-right">{key === "B" ? <span className="text-muted-foreground">n/a</span> : <Money value={row?.gross ?? 0} label={`Line ${key} column 1`} />}</td>
                <td className="py-2 text-right">{key === "B" ? <span className="text-muted-foreground">n/a</span> : <Money value={row?.exempt ?? 0} label={`Line ${key} column 2`} />}</td>
                <td className="py-2 text-right"><Money value={row?.taxable ?? 0} label={`Line ${key} column 3`} /></td>
                <td className="py-2 text-right font-medium"><Money value={row?.taxDue ?? 0} label={`Line ${key} column 4`} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="grid gap-6 border-t px-5 py-4 lg:grid-cols-2">
        <table className="w-full text-sm">
          <tbody className="divide-y">
            {right.map(([number, label, value]) => (
              <tr key={number} className={number === "14" ? "font-semibold" : ""}>
                <td className="w-10 py-1.5 align-top">{number}.</td>
                <td className="py-1.5">{label}</td>
                <td className="py-1.5 text-right"><Money value={value ?? 0} label={`Line ${number}`} /></td>
              </tr>
            ))}
          </tbody>
        </table>
        <table className="w-full text-sm">
          <tbody className="divide-y">
            {back.map(([number, label, value]) => (
              <tr key={number}>
                <td className="w-12 py-1.5 align-top">{number}</td>
                <td className="text-muted-foreground py-1.5 text-xs">{label}</td>
                <td className="py-1.5 text-right"><Money value={value ?? 0} label={`Line ${number}`} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function Timeliness({ worksheet }: { worksheet: Worksheet }) {
  const timing = worksheet.timeliness;
  if (!timing) return null;
  const segments = timing.interestSegments ?? [];
  return (
    <Card className="p-5 text-sm print:break-inside-avoid">
      <h2 className="font-semibold">{timing.projected ? "If filed as shown" : "As filed"}</h2>
      <dl className="mt-3 grid gap-3 sm:grid-cols-4">
        <Pair label="Filed" value={`${day(timing.filedOn)}${timing.electronic ? " · electronic" : " · paper"}`} />
        <Pair label="Return" value={timing.returnLate ? "Late" : "On time"} />
        <Pair label="Payment" value={timing.paymentLate ? `Late (${timing.interestDays} days)` : "On time"} />
        <Pair label="Collection allowance" value={timing.collectionAllowanceEarned ? "Earned" : "Not earned"} />
      </dl>
      {segments.length > 0 ? (
        <p className="text-muted-foreground mt-3 text-xs">
          Interest:{" "}
          {segments.map((segment) => `${day(segment.from)}–${day(segment.to)} ${segment.days} days × ${segment.dailyFactor} = ${cents(segment.amount)}${segment.estimated ? " (estimated rate)" : ""}`).join("; ")}
          . eServices computes the exact penalty and interest when you file.
        </p>
      ) : null}
      {timing.projected ? (
        <p className="text-muted-foreground mt-2 text-xs">Projection: change &ldquo;If filed on&rdquo; above to see the return on another date.</p>
      ) : null}
    </Card>
  );
}
