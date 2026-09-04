import Link from "next/link";
import type { components } from "@overhaul/api-client";
import {
  ArrowDownRight,
  ArrowUpRight,
  Boxes,
  CalendarRange,
  CircleDollarSign,
  Download,
  PackageSearch,
  ReceiptText,
  ShieldAlert,
  TrendingUp,
  WalletCards,
} from "lucide-react";
import { Badge, Card, formatCurrency, formatDate } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { AgingChart, InventoryStatusChart, PerformanceChart, PlatformChart } from "./report-charts";

export const dynamic = "force-dynamic";

type ReportChange = components["schemas"]["ReportChangeDto"];

// Reported totals: an aggregate of nothing is nought, not unknown.
const money = (value: number | null | undefined, digits = 0) =>
  formatCurrency(value ?? 0, { maximumFractionDigits: digits });
const number = (value: number | null | undefined) => new Intl.NumberFormat("en-US").format(value ?? 0);
const percent = (value: number | null | undefined) => `${(value ?? 0).toFixed(1)}%`;
const dateLabel = (value: string | null | undefined) => formatDate(value);

function isoDaysAgo(days: number) {
  const value = new Date();
  value.setUTCDate(value.getUTCDate() - days);
  return value.toISOString().slice(0, 10);
}

function validDate(value: unknown, fallback: string) {
  return typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : fallback;
}

function Change({ change, invert = false }: { change: ReportChange | null | undefined; invert?: boolean }) {
  if (change?.percentChange == null) return <span className="text-xs text-muted-foreground">No prior-period baseline</span>;
  const positive = invert ? change.percentChange <= 0 : change.percentChange >= 0;
  const Icon = change.percentChange >= 0 ? ArrowUpRight : ArrowDownRight;
  return <span className={`inline-flex items-center gap-1 text-xs font-medium ${positive ? "text-green-700" : "text-red-700"}`}><Icon className="h-3.5 w-3.5" />{Math.abs(change.percentChange).toFixed(1)}% vs prior period</span>;
}

function Kpi({ label, value, detail, icon: Icon, change, invert }: { label: string; value: string; detail: string; icon: typeof TrendingUp; change?: ReportChange | null; invert?: boolean }) {
  return (
    <Card className="p-5">
      <div className="flex items-center justify-between"><span className="text-sm text-muted-foreground">{label}</span><Icon className="h-4 w-4 text-muted-foreground" /></div>
      <p className="mt-3 text-3xl font-semibold tracking-tight tabular-nums">{value}</p>
      <p className="mt-1 text-xs text-muted-foreground">{detail}</p>
      {change && <div className="mt-3"><Change change={change} invert={invert} /></div>}
    </Card>
  );
}

export default async function AnalyticsPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const query = await searchParams;
  const to = validDate(query.to, isoDaysAgo(0));
  const from = validDate(query.from, isoDaysAgo(89));
  const api = await getApiClient();
  const result = await api.GET("/api/reports/overview", { params: { query: { from, to } } });
  const report = result.data;

  if (!report?.kpis || !report.inventory || !report.comparison) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold">Reports</h1>
        <div className="rounded-xl border border-destructive/30 bg-destructive/5 p-6 text-sm text-destructive">The report could not be generated. Check the date range and your reporting permissions.</div>
      </div>
    );
  }

  const kpis = report.kpis;
  const comparison = report.comparison;
  const inventory = report.inventory;
  const exportQuery = new URLSearchParams({ from, to }).toString();

  return (
    <div className="space-y-8">
      <header className="flex flex-col justify-between gap-5 xl:flex-row xl:items-end">
        <div>
          <p className="text-sm text-muted-foreground">Intelligence</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Business reports</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">Auditable sales, profit, expense, inventory, aging, and data-quality reporting from the system of record.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {/* Listing quality is reporting data — performance observations per listing — so it lives
              here rather than on the Imports page, where nothing was ever done with it. */}
          <Link href="/analytics/listing-quality" className="inline-flex h-10 items-center gap-2 rounded-lg border bg-background px-4 text-sm font-medium hover:bg-muted"><PackageSearch className="h-4 w-4" /> Listing quality</Link>
          <Link href={`/api/reports/export?${exportQuery}`} className="inline-flex h-10 items-center gap-2 rounded-lg border bg-background px-4 text-sm font-medium hover:bg-muted"><Download className="h-4 w-4" /> Export CSV</Link>
        </div>
      </header>

      <form className="flex flex-col gap-3 rounded-xl border bg-card p-4 lg:flex-row lg:items-end">
        <div className="grid flex-1 gap-3 sm:grid-cols-2">
          <label className="space-y-1.5 text-sm"><span className="font-medium">From</span><input type="date" name="from" defaultValue={from} className="h-10 w-full rounded-lg border bg-background px-3" /></label>
          <label className="space-y-1.5 text-sm"><span className="font-medium">Through</span><input type="date" name="to" defaultValue={to} className="h-10 w-full rounded-lg border bg-background px-3" /></label>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link href={`/analytics?from=${isoDaysAgo(29)}&to=${isoDaysAgo(0)}`} className="inline-flex h-10 items-center rounded-lg border px-3 text-sm hover:bg-muted">30 days</Link>
          <Link href={`/analytics?from=${isoDaysAgo(89)}&to=${isoDaysAgo(0)}`} className="inline-flex h-10 items-center rounded-lg border px-3 text-sm hover:bg-muted">90 days</Link>
          <Link href={`/analytics?from=${isoDaysAgo(364)}&to=${isoDaysAgo(0)}`} className="inline-flex h-10 items-center rounded-lg border px-3 text-sm hover:bg-muted">1 year</Link>
          <button type="submit" className="inline-flex h-10 items-center gap-2 rounded-lg bg-brand-600 px-4 text-sm font-medium text-white hover:bg-brand-700"><CalendarRange className="h-4 w-4" /> Run report</button>
        </div>
      </form>

      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <Badge variant="outline">{dateLabel(report.from)} – {dateLabel(report.to)}</Badge>
        <span>Grouped by {report.granularity}</span><span>·</span><span>Generated {dateLabel(report.generatedAt)}</span>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Kpi label="Gross sales" value={money(kpis.grossSales)} detail={`${number(kpis.salesCount)} completed sales`} icon={CircleDollarSign} change={comparison.grossSales} />
        <Kpi label="Realized net profit" value={money(kpis.realizedNetProfit)} detail={`${percent(kpis.netMarginPercent)} net margin`} icon={TrendingUp} change={comparison.netProfit} />
        <Kpi label="Average sale value" value={money(kpis.averageSaleValue)} detail={`${number(kpis.returnedSalesCount)} returned sales excluded`} icon={WalletCards} change={comparison.averageSaleValue} />
        <Kpi label="Operating expenses" value={money(kpis.operatingExpenses)} detail="Recorded non-inventory expenses" icon={ReceiptText} change={comparison.operatingExpenses} invert />
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Kpi label="Net income after expenses" value={money(kpis.netIncomeAfterExpenses)} detail="Realized profit less operating overhead" icon={TrendingUp} />
        <Kpi label="Sales tax owed" value={money(kpis.salesTaxOwed)} detail="We collected it; owed to the state (not profit)" icon={ReceiptText} />
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Kpi label="Cost of goods" value={money(kpis.costOfGoods)} detail="Specific item acquisition costs" icon={Boxes} />
        <Kpi label="Gross profit" value={money(kpis.grossProfit)} detail="Sales less specific-item COGS" icon={TrendingUp} />
        <Kpi label="Transaction costs" value={money(kpis.saleCosts)} detail="Platform, payment, shipping, supplies, other" icon={ReceiptText} />
        <Kpi label="Inventory acquired" value={money(kpis.inventorySpend)} detail="Recorded inventory-purchase expenses" icon={PackageSearch} />
      </div>

      <Card className="p-6">
        <div className="mb-5"><h2 className="font-semibold">Performance over time</h2><p className="text-sm text-muted-foreground">Gross sales, realized net profit, and separately recorded operating expenses.</p></div>
        <PerformanceChart series={report.series ?? []} />
      </Card>

      <div className="grid gap-6 xl:grid-cols-2">
        <Card className="p-6"><div className="mb-4"><h2 className="font-semibold">Channel performance</h2><p className="text-sm text-muted-foreground">Revenue and realized net profit by selling platform.</p></div><PlatformChart data={report.platforms ?? []} /></Card>
        <Card className="p-6"><div className="mb-4"><h2 className="font-semibold">Inventory lifecycle</h2><p className="text-sm text-muted-foreground">Current inventory distribution across operational stages.</p></div><InventoryStatusChart data={report.inventoryStatuses ?? []} /></Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.35fr)_minmax(320px,0.65fr)]">
        <Card className="p-6"><div className="mb-4"><h2 className="font-semibold">Inventory aging exposure</h2><p className="text-sm text-muted-foreground">Cost basis and expected value by days held.</p></div><AgingChart data={report.aging ?? []} /></Card>
        <Card className="p-6">
          <div className="flex items-start justify-between"><div><h2 className="font-semibold">Inventory position</h2><p className="text-sm text-muted-foreground">Current unsold operating inventory.</p></div><Boxes className="h-5 w-5 text-muted-foreground" /></div>
          <div className="mt-6 space-y-4 text-sm">
            <div className="flex justify-between"><span className="text-muted-foreground">Items</span><strong>{number(inventory.itemCount)}</strong></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Cost basis</span><strong>{money(inventory.cost)}</strong></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Expected value</span><strong>{money(inventory.estimatedValue)}</strong></div>
            <div className="flex justify-between border-t pt-4"><span className="text-muted-foreground">Projected spread</span><strong>{money((inventory.estimatedValue ?? 0) - (inventory.cost ?? 0))}</strong></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Unlisted</span><strong>{number(inventory.unlistedCount)}</strong></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Aged over 60 days</span><strong>{number(inventory.staleCount)}</strong></div>
          </div>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <Card className="overflow-hidden">
          <div className="border-b p-6"><h2 className="font-semibold">Top realized profit</h2><p className="text-sm text-muted-foreground">Best-performing item sales in the selected period.</p></div>
          <div className="overflow-x-auto"><table className="w-full text-sm"><thead className="bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-4 py-3">Item</th><th className="px-4 py-3">Channel</th><th className="px-4 py-3 text-right">Sale</th><th className="px-4 py-3 text-right">Profit</th><th className="px-4 py-3 text-right">Margin</th></tr></thead><tbody className="divide-y">{(report.topSales ?? []).map((sale) => <tr key={sale.saleId}><td className="px-4 py-3"><Link href={`/inventory/${sale.inventoryItemId}`} className="font-medium hover:underline">{sale.title}</Link><p className="text-xs text-muted-foreground">{dateLabel(sale.soldAt)}</p></td><td className="px-4 py-3 text-muted-foreground">{sale.platform}</td><td className="px-4 py-3 text-right tabular-nums">{money(sale.soldPrice)}</td><td className="px-4 py-3 text-right font-medium tabular-nums">{money(sale.netProfit)}</td><td className="px-4 py-3 text-right tabular-nums">{percent(sale.marginPercent)}</td></tr>)}{(report.topSales ?? []).length === 0 && <tr><td colSpan={5} className="p-8 text-center text-muted-foreground">No completed sales in this period.</td></tr>}</tbody></table></div>
        </Card>

        <Card className="p-6">
          <div className="flex items-start justify-between"><div><h2 className="font-semibold">Data quality control</h2><p className="text-sm text-muted-foreground">Missing fields that weaken valuation, fulfillment, and reporting.</p></div><ShieldAlert className="h-5 w-5 text-amber-600" /></div>
          <div className="mt-5 space-y-3">
            {([
              ["Missing acquisition cost", inventory.missingCostCount, "MissingCost"],
              ["Missing expected value", inventory.missingValueCount, "MissingValue"],
              ["Missing storage location", inventory.missingLocationCount, "MissingLocation"],
              ["Missing photography", inventory.missingPhotoCount, "MissingPhotos"],
            ] as const).map(([label, count, attention]) => <Link key={label} href={`/inventory?attention=${attention}`} className="flex items-center justify-between rounded-lg border p-3 hover:bg-muted/40"><span className="text-sm">{label}</span><span className="inline-flex items-center gap-2 font-semibold tabular-nums">{number(count)}<ArrowUpRight className="h-3.5 w-3.5" /></span></Link>)}
          </div>
        </Card>
      </div>

      <div className="rounded-xl border bg-muted/20 p-4 text-xs leading-relaxed text-muted-foreground">
        <strong className="text-foreground">Report definitions:</strong> returned sales are excluded from realized sales metrics; COGS uses each sold item’s recorded acquisition cost; transaction costs come from the sale record; operating expenses exclude inventory-purchase expenses and are displayed separately to avoid presenting an unreconciled P&amp;L as final accounting truth.
      </div>
    </div>
  );
}
