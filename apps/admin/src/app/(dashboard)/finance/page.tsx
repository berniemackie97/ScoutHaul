import Link from "next/link";
import { CircleDollarSign, Landmark, PlugZap, Plus, ReceiptText, Wallet } from "lucide-react";
import { Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { SalesTable } from "./sales-table";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";

export default async function FinancePage({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const params = await searchParams;
  const search = params.search?.trim() || undefined;
  const status = params.status || undefined;
  const sort = params.sort || undefined;
  const desc = params.desc !== "false";
  const page = Math.max(1, Number(params.page) || 1);

  const api = await getApiClient();
  const { data } = await api.GET("/api/sales/browse", {
    params: { query: { search, status: status as never, sort, desc, page, pageSize: 25 } },
  });
  const sales = data?.items ?? [];
  const raw = data?.summary;
  const summary = {
    realizedCount: raw?.realizedCount ?? 0,
    itemSales: raw?.itemSales ?? 0,
    buyerShipping: raw?.buyerShipping ?? 0,
    salesTaxCollected: raw?.salesTaxCollected ?? 0,
    grossBuyerSales: raw?.grossBuyerSales ?? 0,
    platformFees: raw?.platformFees ?? 0,
    paymentFees: raw?.paymentFees ?? 0,
    shippingLabels: raw?.shippingLabels ?? 0,
    suppliesAndOther: raw?.suppliesAndOther ?? 0,
    sellingCosts: raw?.sellingCosts ?? 0,
    marketplaceNetSales: raw?.marketplaceNetSales ?? 0,
    costOfGoods: raw?.costOfGoods ?? 0,
    netProfit: raw?.netProfit ?? 0,
    salesTaxOwed: raw?.salesTaxOwed ?? 0,
  };
  const hasQuery = Boolean(search || status || sort || page > 1);

  const kpis = [
    {
      label: "Gross buyer sales",
      value: money(summary.grossBuyerSales),
      detail: `${summary.realizedCount} realized sales · includes item, shipping, tax`,
      icon: CircleDollarSign,
    },
    {
      label: "Marketplace net sales",
      value: money(summary.marketplaceNetSales),
      detail: "Item + buyer shipping − selling costs",
      icon: ReceiptText,
    },
    {
      label: "True net profit",
      value: money(summary.netProfit),
      detail: "Marketplace net − COGS; tax excluded",
      icon: Wallet,
    },
    {
      label: "Sales tax owed",
      value: money(summary.salesTaxOwed),
      detail: "Collected by us, owed to the state",
      icon: Landmark,
    },
  ];

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <p className="text-muted-foreground text-sm">Finance</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Sales &amp; profit</h1>
          <p className="text-muted-foreground mt-1 text-sm">
            Every recorded sale with its true net after fees and tax. Open a sale to correct it — every change is audited.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link
            href="/finance/new"
            className="bg-brand-600 hover:bg-brand-700 inline-flex h-10 items-center justify-center gap-2 rounded-lg px-4 text-sm font-medium text-white"
          >
            <Plus className="h-4 w-4" /> Record sale
          </Link>
          <Link
            href="/finance/bundled"
            className="hover:bg-muted inline-flex h-10 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium"
          >
            <Plus className="h-4 w-4" /> Bundled sale
          </Link>
          <Link
            href="/finance/platform-orders"
            className="hover:bg-muted inline-flex h-10 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium"
          >
            <PlugZap className="h-4 w-4" /> Reconcile platform orders
          </Link>
        </div>
      </header>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {kpis.map((kpi) => (
          <Card key={kpi.label} className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-muted-foreground text-sm">{kpi.label}</span>
              <kpi.icon className="text-muted-foreground h-4 w-4" aria-hidden />
            </div>
            <p className="mt-3 text-3xl font-semibold tabular-nums">{kpi.value}</p>
            <p className="text-muted-foreground mt-1 text-xs">{kpi.detail}</p>
          </Card>
        ))}
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="p-5">
          <h2 className="text-sm font-semibold">Sales components</h2>
          <BreakdownRow label="Item sales" value={summary.itemSales} />
          <BreakdownRow label="Shipping & handling charged" value={summary.buyerShipping} />
          <BreakdownRow label="Taxes collected" value={summary.salesTaxCollected} muted />
          <BreakdownRow label="Gross buyer sales" value={summary.grossBuyerSales} strong />
        </Card>
        <Card className="p-5">
          <h2 className="text-sm font-semibold">Selling costs</h2>
          <BreakdownRow label="Platform fees" value={summary.platformFees} negative />
          <BreakdownRow label="Payment fees" value={summary.paymentFees} negative />
          <BreakdownRow label="Shipping labels" value={summary.shippingLabels} negative />
          <BreakdownRow label="Supplies + other" value={summary.suppliesAndOther} negative />
          <BreakdownRow label="Total selling costs" value={summary.sellingCosts} negative strong />
        </Card>
        <Card className="p-5">
          <h2 className="text-sm font-semibold">Profit bridge</h2>
          <BreakdownRow label="Item + shipping revenue" value={summary.itemSales + summary.buyerShipping} />
          <BreakdownRow label="Selling costs" value={summary.sellingCosts} negative />
          <BreakdownRow label="Marketplace net before COGS" value={summary.marketplaceNetSales} strong />
          <BreakdownRow label="Cost of goods" value={summary.costOfGoods} negative />
          <BreakdownRow label="True net profit" value={summary.netProfit} strong />
        </Card>
      </div>

      {sales.length === 0 && !hasQuery ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          <p>No sales recorded yet.</p>
          <Link href="/finance/new" className="text-foreground mt-3 inline-block font-medium underline">
            Record the first sale
          </Link>
        </div>
      ) : (
        <SalesTable
          sales={sales}
          total={data?.total ?? 0}
          page={data?.page ?? 1}
          pageCount={data?.pageCount ?? 1}
          query={{ search: search ?? "", status: status ?? "", sort: sort ?? "soldAt", desc }}
        />
      )}
    </div>
  );
}

function BreakdownRow({
  label,
  value,
  strong = false,
  negative = false,
  muted = false,
}: {
  label: string;
  value: number;
  strong?: boolean;
  negative?: boolean;
  muted?: boolean;
}) {
  return (
    <div className={`mt-3 flex justify-between gap-4 text-sm ${strong ? "border-t pt-3 font-semibold" : ""}`}>
      <span className={muted ? "text-muted-foreground/80" : "text-muted-foreground"}>{label}</span>
      <span className="tabular-nums">{negative ? "-" : ""}{money(value)}</span>
    </div>
  );
}
