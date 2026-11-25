import Link from "next/link";
import type { components } from "@overhaul/api-client";
import { ArrowUpRight, CalendarRange, CheckCircle2, Download, PackageSearch } from "lucide-react";
import { Card, formatCurrency } from "@overhaul/ui";
import {
  RANGE_PRESETS,
  humanize,
  isRangePreset,
  moneyBreakdown,
  percentChange,
  reportDay,
  type RangePreset,
} from "@/features/reports/report-format";
import { getApiClient } from "@/lib/api";
import { When } from "@/components/when";
import { refusal } from "@/lib/page-access";
import { BreakdownBar, Delta, LegendKey, RowBar, Sparkline } from "./report-visuals";
import { TrendChart } from "./trend-chart";

export const dynamic = "force-dynamic";

type Report = components["schemas"]["BusinessReportDto"];
type Kpis = components["schemas"]["ReportKpiDto"];
type SaleRow = components["schemas"]["ReportSaleRowDto"];

const money = (value: number | null | undefined, digits = 0) => formatCurrency(value ?? 0, { maximumFractionDigits: digits });
const count = (value: number | null | undefined) => new Intl.NumberFormat("en-US").format(value ?? 0);
const isDay = (value: unknown): value is string => typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value);

/** A calendar day as sent, never shifted by the server's own zone. */
function dayLabel(day: string | undefined, withYear = true) {
  if (!day) return "—";
  return new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: withYear ? "numeric" : undefined, timeZone: "UTC" }).format(
    new Date(`${day}T00:00:00Z`),
  );
}

/** "Pacific Daylight Time" rather than "America/Los_Angeles". */
function zoneName(zone: string | null | undefined) {
  if (!zone) return "UTC";
  try {
    return new Intl.DateTimeFormat("en-US", { timeZone: zone, timeZoneName: "long" }).formatToParts(new Date()).find((part) => part.type === "timeZoneName")?.value ?? zone;
  } catch {
    return zone;
  }
}

function shiftDay(day: string, days: number) {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

export default async function AnalyticsPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const refused = await refusal("/analytics");
  if (refused) return refused;

  const query = await searchParams;
  const custom = isDay(query.from) && isDay(query.to);
  const preset: RangePreset | undefined = isRangePreset(query.preset) ? query.preset : custom ? undefined : "90d";
  const api = await getApiClient();
  const { data: report } = await api.GET("/api/reports/overview", {
    params: { query: preset ? { preset } : { from: query.from as string, to: query.to as string } },
  });

  if (!report?.kpis || !report.inventory) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold">Reports</h1>
        <div className="border-destructive-border bg-destructive-surface text-destructive rounded-xl border p-6 text-sm">
          The report could not be produced. Check the dates (a range can be up to two years) and try again.
        </div>
      </div>
    );
  }

  const kpis = report.kpis;
  const previous = report.previousKpis;
  const from = reportDay(report.from)!;
  const to = reportDay(report.to)!;
  const days = Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000) + 1;
  const { segments, loss } = moneyBreakdown(kpis);
  const series = report.series ?? [];
  const margin = kpis.netMarginPercent ?? 0;
  const marginChange = previous && (previous.grossSales ?? 0) > 0 ? margin - (previous.netMarginPercent ?? 0) : null;
  const exportQuery = new URLSearchParams({ from, to }).toString();

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <p className="text-muted-foreground text-sm">Intelligence</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Reports</h1>
          <p className="text-muted-foreground mt-1 text-sm">What you sold, what it cost, and what you kept.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link href="/analytics/listing-quality" className="bg-background hover:bg-muted inline-flex h-9 items-center gap-2 rounded-md border px-3 text-sm font-medium">
            <PackageSearch className="h-4 w-4" aria-hidden /> Listing quality
          </Link>
          <a href={`/api/reports/export?${exportQuery}`} download className="bg-background hover:bg-muted inline-flex h-9 items-center gap-2 rounded-md border px-3 text-sm font-medium">
            <Download className="h-4 w-4" aria-hidden /> Export CSV
          </a>
        </div>
      </header>

      {/* One filter row scopes everything below it. */}
      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex flex-wrap items-center gap-3">
          <nav className="bg-muted/60 inline-flex rounded-lg p-1" aria-label="Report range">
            {RANGE_PRESETS.map((option) => (
              <Link
                key={option.key}
                href={`/analytics?preset=${option.key}`}
                title={option.title}
                aria-current={preset === option.key ? "page" : undefined}
                className={`rounded-md px-3 py-1.5 text-sm font-medium transition ${
                  preset === option.key ? "bg-card text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground"
                }`}
              >
                {option.label}
              </Link>
            ))}
          </nav>
          <p className="text-muted-foreground text-sm">
            <span className="text-foreground font-medium">
              {dayLabel(from, false)} – {dayLabel(to)}
            </span>{" "}
            · compared with {dayLabel(shiftDay(from, -days), false)} – {dayLabel(shiftDay(from, -1))}
          </p>
        </div>
        <form className="flex flex-wrap items-center gap-2" action="/analytics">
          <input type="date" name="from" defaultValue={from} aria-label="From" className="bg-background h-9 rounded-md border px-2 text-sm" />
          <span className="text-muted-foreground text-sm">to</span>
          <input type="date" name="to" defaultValue={to} aria-label="Through" className="bg-background h-9 rounded-md border px-2 text-sm" />
          <button type="submit" className="hover:bg-muted inline-flex h-9 items-center gap-1.5 rounded-md border px-3 text-sm font-medium">
            <CalendarRange className="h-4 w-4" aria-hidden /> Apply
          </button>
        </form>
      </div>

      <Card className="grid gap-8 p-6 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.4fr)]">
        <div>
          <p className="text-muted-foreground text-sm">Net profit</p>
          <p className={`mt-2 font-sans text-5xl font-semibold tracking-tight ${(kpis.realizedNetProfit ?? 0) < 0 ? "text-destructive" : ""}`}>
            {money(kpis.realizedNetProfit)}
          </p>
          <Delta change={percentChange(kpis.realizedNetProfit, previous?.realizedNetProfit)} className="mt-2" />
          <p className="text-muted-foreground mt-4 max-w-sm text-sm leading-relaxed">
            Kept <span className="text-foreground font-medium">{margin.toFixed(1)}%</span> of{" "}
            <span className="text-foreground font-medium">{money(kpis.grossSales)}</span> in sales across {count(kpis.salesCount)} sale
            {kpis.salesCount === 1 ? "" : "s"}
            {(kpis.returnedSalesCount ?? 0) > 0 ? `, after ${count(kpis.returnedSalesCount)} returned or refunded` : ""}.
          </p>
          {(kpis.operatingExpenses ?? 0) > 0 && (
            <p className="text-muted-foreground mt-2 text-sm">
              After {money(kpis.operatingExpenses)} of overhead:{" "}
              <span className="text-foreground font-medium">{money(kpis.netIncomeAfterExpenses)}</span> net income.
            </p>
          )}
        </div>
        <div>
          <p className="mb-4 text-sm font-medium">Where every sales dollar went</p>
          <BreakdownBar segments={segments} total={segments.reduce((sum, segment) => sum + segment.value, 0)} />
          {loss > 0 && (
            <p className="text-destructive mt-3 text-sm">Costs were {money(loss)} more than sales brought in.</p>
          )}
        </div>
      </Card>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile
          label="Gross sales"
          value={money(kpis.grossSales)}
          change={percentChange(kpis.grossSales, previous?.grossSales)}
          trend={series.map((point) => point.grossSales ?? 0)}
        />
        <StatTile
          label="Sales"
          value={count(kpis.salesCount)}
          change={percentChange(kpis.salesCount, previous?.salesCount)}
          trend={series.map((point) => point.salesCount ?? 0)}
        />
        <StatTile label="Average sale" value={money(kpis.averageSaleValue, 2)} change={percentChange(kpis.averageSaleValue, previous?.averageSaleValue)} />
        <StatTile label="Net margin" value={`${margin.toFixed(1)}%`} change={marginChange} points />
      </div>

      <Card className="p-6">
        <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 className="font-semibold">Sales and profit over time</h2>
            <p className="text-muted-foreground text-sm">By {report.granularity}, in {zoneName(report.timeZone)}.</p>
          </div>
          <div className="flex gap-4">
            <LegendKey color="var(--viz-1)" label="Net profit" line />
            <LegendKey color="var(--viz-context)" label="Gross sales" line />
          </div>
        </div>
        <TrendChart series={series} />
        <details className="mt-4">
          <summary className="text-muted-foreground hover:text-foreground cursor-pointer text-xs">Show as a table</summary>
          <div className="mt-3 max-h-80 overflow-auto rounded-lg border">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-muted-foreground sticky top-0 text-left text-xs uppercase tracking-wide">
                <tr>
                  <th className="px-3 py-2 font-medium">Period</th>
                  <th className="px-3 py-2 text-right font-medium">Sales</th>
                  <th className="px-3 py-2 text-right font-medium">Gross sales</th>
                  <th className="px-3 py-2 text-right font-medium">Net profit</th>
                </tr>
              </thead>
              <tbody className="divide-y tabular-nums">
                {series.map((point) => (
                  <tr key={point.label}>
                    <td className="px-3 py-1.5">{point.label}</td>
                    <td className="px-3 py-1.5 text-right">{point.salesCount}</td>
                    <td className="px-3 py-1.5 text-right">{money(point.grossSales, 2)}</td>
                    <td className="px-3 py-1.5 text-right">{money(point.netProfit, 2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      </Card>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
        <ProfitAndLoss kpis={kpis} previous={previous} expenses={report.expenseCategories ?? []} />
        <Channels platforms={report.platforms ?? []} />
      </div>

      <section className="space-y-4">
        <div>
          <h2 className="text-lg font-semibold">Inventory on hand</h2>
          <p className="text-muted-foreground text-sm">Everything not yet sold, as of now — not limited to the dates above.</p>
        </div>
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatTile label="Items" value={count(report.inventory.itemCount)} detail={`${count(report.inventory.unlistedCount)} not listed yet`} />
          <StatTile label="Cost tied up" value={money(report.inventory.cost)} detail="What you paid for it" />
          <StatTile label="Expected value" value={money(report.inventory.estimatedValue)} detail="At your estimated prices" />
          <StatTile
            label="Projected spread"
            value={money((report.inventory.estimatedValue ?? 0) - (report.inventory.cost ?? 0))}
            detail="Expected value less cost, before fees"
          />
        </div>
        <div className="grid gap-6 xl:grid-cols-3">
          <Aging report={report} />
          <Stages report={report} />
          <DataQuality inventory={report.inventory} />
        </div>
      </section>

      <div className="grid gap-6 xl:grid-cols-2">
        <SalesTable title="Most profitable sales" rows={report.topSales ?? []} empty="No completed sales in this period." />
        <SalesTable
          title="Sales that lost money"
          rows={report.losingSales ?? []}
          empty={
            <span className="inline-flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 text-[color:var(--viz-up)]" aria-hidden /> No sale lost money in this period.
            </span>
          }
        />
      </div>

      <details className="bg-muted/20 text-muted-foreground rounded-xl border p-4 text-xs leading-relaxed">
        <summary className="text-foreground cursor-pointer font-medium">How these figures are counted</summary>
        <p className="mt-2">
          Gross sales are item prices plus the shipping buyers paid, for sales that were not cancelled, returned or fully refunded.
          Cost of goods is each sold item&apos;s recorded cost. Net profit is gross sales less cost of goods, fees, shipping labels,
          supplies and refunds. Operating expenses are recorded overhead, excluding inventory purchases. Sales tax you collected is a
          liability you owe, never income. Days are counted in {zoneName(report.timeZone)}.
        </p>
      </details>
    </div>
  );
}

function StatTile({
  label,
  value,
  change,
  trend,
  detail,
  points = false,
  upIsGood = true,
}: {
  label: string;
  value: string;
  change?: number | null;
  trend?: number[];
  detail?: string;
  points?: boolean;
  upIsGood?: boolean;
}) {
  return (
    <Card className="flex flex-col justify-between gap-3 p-5">
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="text-muted-foreground text-sm">{label}</p>
          <p className="mt-2 font-sans text-3xl font-semibold tracking-tight">{value}</p>
        </div>
        {trend && <Sparkline values={trend} label={`${label} trend`} />}
      </div>
      {change !== undefined ? <Delta change={change} points={points} upIsGood={upIsGood} /> : null}
      {detail && <p className="text-muted-foreground text-xs">{detail}</p>}
    </Card>
  );
}

function ProfitAndLoss({ kpis, previous, expenses }: { kpis: Kpis; previous?: Kpis | null; expenses: components["schemas"]["ReportDimensionDto"][] }) {
  type Line = { label: string; key: keyof Kpis; cost?: boolean; total?: boolean; hideWhenZero?: boolean };
  const lines: Line[] = [
    { label: "Item sales", key: "itemSales" },
    { label: "Buyer-paid shipping", key: "buyerShipping" },
    { label: "Gross sales", key: "grossSales", total: true },
    { label: "Cost of goods", key: "costOfGoods", cost: true },
    { label: "Gross profit", key: "grossProfit", total: true },
    { label: "Platform fees", key: "platformFees", cost: true },
    { label: "Payment fees", key: "paymentFees", cost: true, hideWhenZero: true },
    { label: "Shipping labels", key: "shippingCosts", cost: true },
    { label: "Supplies & other", key: "suppliesAndOther", cost: true, hideWhenZero: true },
    { label: "Refunds & adjustments", key: "refundsAndAdjustments", cost: true, hideWhenZero: true },
    { label: "Net profit on sales", key: "realizedNetProfit", total: true },
    { label: "Operating expenses", key: "operatingExpenses", cost: true },
    { label: "Net income", key: "netIncomeAfterExpenses", total: true },
  ];
  const read = (source: Kpis | null | undefined, key: keyof Kpis) => Number(source?.[key] ?? 0);
  const overhead = expenses.filter((entry) => entry.name !== "InventoryPurchase" && (entry.amount ?? 0) > 0);

  return (
    <Card className="overflow-hidden">
      <div className="p-6 pb-3">
        <h2 className="font-semibold">Profit and loss</h2>
        <p className="text-muted-foreground text-sm">This period against the one before it.</p>
      </div>
      <table className="w-full text-sm">
        <thead className="text-muted-foreground border-y text-left text-xs uppercase tracking-wide">
          <tr>
            <th className="px-6 py-2 font-medium" />
            <th className="px-3 py-2 text-right font-medium">This period</th>
            <th className="px-3 py-2 text-right font-medium">Previous</th>
            <th className="px-6 py-2 text-right font-medium">Change</th>
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {lines
            .filter((line) => !line.hideWhenZero || read(kpis, line.key) !== 0 || read(previous, line.key) !== 0)
            .map((line) => {
              const current = read(kpis, line.key);
              const prior = read(previous, line.key);
              const show = (value: number) => (line.cost && value > 0 ? `−${money(value, 2)}` : money(value, 2));
              return (
                <tr key={line.key} className={line.total ? "bg-muted/30 font-semibold" : ""}>
                  <td className={`px-6 py-2 ${line.total ? "" : "text-muted-foreground pl-9"}`}>{line.label}</td>
                  <td className={`px-3 py-2 text-right ${line.total && current < 0 ? "text-destructive" : ""}`}>{show(current)}</td>
                  <td className="text-muted-foreground px-3 py-2 text-right font-normal">{previous ? show(prior) : "—"}</td>
                  <td className="px-6 py-2 text-right font-normal">
                    {previous ? <ChangeCell change={percentChange(current, prior)} upIsGood={!line.cost} /> : "—"}
                  </td>
                </tr>
              );
            })}
        </tbody>
      </table>
      <div className="text-muted-foreground space-y-1 border-t px-6 py-4 text-xs">
        <p className="flex justify-between">
          <span>Sales tax collected and owed (a liability, not income)</span>
          <span className="tabular-nums">{money(kpis.salesTaxOwed, 2)}</span>
        </p>
        <p className="flex justify-between">
          <span>Inventory bought (shows up later as cost of goods)</span>
          <span className="tabular-nums">{money(kpis.inventorySpend, 2)}</span>
        </p>
        {overhead.length > 0 && (
          <p className="pt-1">
            Overhead: {overhead.map((entry) => `${humanize(entry.name)} ${money(entry.amount)}`).join(" · ")}
          </p>
        )}
      </div>
    </Card>
  );
}

function ChangeCell({ change, upIsGood }: { change: number | null; upIsGood: boolean }) {
  if (change == null) return <span className="text-muted-foreground">—</span>;
  const good = Math.abs(change) < 0.05 ? null : change > 0 === upIsGood;
  return (
    <span className={good == null ? "text-muted-foreground" : good ? "text-[color:var(--viz-up)]" : "text-[color:var(--viz-down)]"}>
      {change > 0 ? "+" : change < 0 ? "−" : ""}
      {Math.abs(change).toFixed(1)}%
    </span>
  );
}

function Channels({ platforms }: { platforms: components["schemas"]["ReportDimensionDto"][] }) {
  const max = Math.max(...platforms.map((platform) => platform.amount ?? 0), 0);
  return (
    <Card className="p-6">
      <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="font-semibold">Channels</h2>
          <p className="text-muted-foreground text-sm">Sales on each marketplace, and how much of it you kept.</p>
        </div>
        <div className="flex gap-4">
          <LegendKey color="var(--viz-1)" label="Net profit" />
          <LegendKey color="var(--viz-context)" label="Gross sales" />
        </div>
      </div>
      {platforms.length === 0 ? (
        <p className="text-muted-foreground text-sm">No completed sales in this period.</p>
      ) : (
        <ul className="space-y-4">
          {platforms.map((platform) => {
            const gross = platform.amount ?? 0;
            const net = platform.secondaryAmount ?? 0;
            return (
              <li key={platform.name} className="space-y-1.5">
                <div className="flex items-baseline justify-between gap-3 text-sm">
                  <span className="font-medium">
                    {platform.name}
                    <span className="text-muted-foreground ml-2 text-xs font-normal">
                      {count(platform.count)} sale{platform.count === 1 ? "" : "s"}
                    </span>
                  </span>
                  <span className="whitespace-nowrap tabular-nums">
                    <span className="font-medium">{money(net)}</span>
                    <span className="text-muted-foreground"> of {money(gross)}</span>
                    <span className="text-muted-foreground ml-2 inline-block w-12 text-right text-xs">
                      {gross > 0 ? `${((net / gross) * 100).toFixed(0)}%` : "—"}
                    </span>
                  </span>
                </div>
                <RowBar ratio={max > 0 ? Math.max(0, net) / max : 0} color="var(--viz-1)" track={max > 0 ? gross / max : 0} />
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

function Aging({ report }: { report: Report }) {
  const buckets = report.aging ?? [];
  const max = Math.max(...buckets.map((bucket) => bucket.cost ?? 0), 0);
  return (
    <Card className="p-6">
      <h3 className="font-semibold">Age of stock</h3>
      <p className="text-muted-foreground mb-5 text-sm">Cost tied up, by days since you bought it.</p>
      <ul className="space-y-3.5">
        {buckets.map((bucket) => (
          <li key={bucket.label} className="space-y-1.5">
            <div className="flex justify-between gap-3 text-sm">
              <span>
                {bucket.label}
                <span className="text-muted-foreground ml-2 text-xs">{count(bucket.count)} items</span>
              </span>
              <span className="font-medium tabular-nums">{money(bucket.cost)}</span>
            </div>
            <RowBar ratio={max > 0 ? (bucket.cost ?? 0) / max : 0} color="var(--viz-2)" />
          </li>
        ))}
      </ul>
    </Card>
  );
}

function Stages({ report }: { report: Report }) {
  const stages = report.inventoryStatuses ?? [];
  const max = Math.max(...stages.map((stage) => stage.count ?? 0), 0);
  return (
    <Card className="p-6">
      <h3 className="font-semibold">Where stock is</h3>
      <p className="text-muted-foreground mb-5 text-sm">Items at each stage before sale.</p>
      {stages.length === 0 ? (
        <p className="text-muted-foreground text-sm">No unsold stock.</p>
      ) : (
        <ul className="space-y-3.5">
          {stages.map((stage) => (
            <li key={stage.name} className="space-y-1.5">
              <Link href={`/inventory?status=${stage.name}`} className="flex justify-between gap-3 text-sm hover:underline">
                <span>{humanize(stage.name)}</span>
                <span className="font-medium tabular-nums">{count(stage.count)}</span>
              </Link>
              <RowBar ratio={max > 0 ? (stage.count ?? 0) / max : 0} color="var(--viz-context)" />
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

function DataQuality({ inventory }: { inventory: components["schemas"]["ReportInventorySnapshotDto"] }) {
  const checks = [
    { label: "Missing cost", value: inventory.missingCostCount, attention: "MissingCost", why: "Profit can't be known" },
    { label: "Missing expected price", value: inventory.missingValueCount, attention: "MissingValue", why: "Stock value is understated" },
    { label: "Missing storage location", value: inventory.missingLocationCount, attention: "MissingLocation", why: "Slower to pick and pack" },
    { label: "Missing photos", value: inventory.missingPhotoCount, attention: "MissingPhotos", why: "Can't be listed" },
  ];
  const clean = checks.every((check) => (check.value ?? 0) === 0);
  return (
    <Card className="p-6">
      <h3 className="font-semibold">Records to finish</h3>
      <p className="text-muted-foreground mb-4 text-sm">Gaps that make these numbers less exact.</p>
      {clean ? (
        <p className="inline-flex items-center gap-2 text-sm">
          <CheckCircle2 className="h-4 w-4 text-[color:var(--viz-up)]" aria-hidden /> Every item has what it needs.
        </p>
      ) : (
        <ul className="divide-y">
          {checks.map((check) => (
            <li key={check.attention}>
              <Link href={`/inventory?attention=${check.attention}`} className="hover:bg-muted/40 -mx-2 flex items-center justify-between gap-3 rounded-md px-2 py-2.5">
                <span>
                  <span className="block text-sm">{check.label}</span>
                  <span className="text-muted-foreground block text-xs">{check.why}</span>
                </span>
                <span className={`inline-flex items-center gap-1 text-sm font-semibold tabular-nums ${(check.value ?? 0) === 0 ? "text-muted-foreground" : ""}`}>
                  {count(check.value)}
                  <ArrowUpRight className="h-3.5 w-3.5" aria-hidden />
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

function SalesTable({ title, rows, empty }: { title: string; rows: SaleRow[]; empty: React.ReactNode }) {
  return (
    <Card className="overflow-hidden">
      <div className="p-6 pb-3">
        <h2 className="font-semibold">{title}</h2>
      </div>
      {rows.length === 0 ? (
        <p className="text-muted-foreground px-6 pb-6 text-sm">{empty}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-muted-foreground border-y text-left text-xs uppercase tracking-wide">
              <tr>
                <th className="px-6 py-2 font-medium">Item</th>
                <th className="px-3 py-2 text-right font-medium">Sold for</th>
                <th className="px-3 py-2 text-right font-medium">Net</th>
                <th className="px-6 py-2 text-right font-medium">Margin</th>
              </tr>
            </thead>
            <tbody className="divide-y tabular-nums">
              {rows.map((sale) => (
                <tr key={sale.saleId} className="hover:bg-muted/30">
                  <td className="max-w-[280px] px-6 py-2.5">
                    <Link href={sale.orderId ? `/orders/${sale.orderId}` : `/inventory/${sale.inventoryItemId}`} className="block truncate font-medium hover:underline">
                      {sale.title}
                    </Link>
                    <span className="text-muted-foreground text-xs">
                      {sale.platform} · <When value={sale.soldAt} />
                    </span>
                  </td>
                  <td className="px-3 py-2.5 text-right">{money(sale.soldPrice, 2)}</td>
                  <td className={`px-3 py-2.5 text-right font-semibold ${(sale.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}>
                    {money(sale.netProfit, 2)}
                  </td>
                  <td className="text-muted-foreground px-6 py-2.5 text-right">{(sale.marginPercent ?? 0).toFixed(0)}%</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
