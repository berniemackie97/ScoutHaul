import Link from "next/link";
import { ArrowLeft, FileSpreadsheet, PlugZap, ScanLine, Wallet } from "lucide-react";
import { Badge, Card, formatCurrency, formatDate } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { PlatformOrderReconciler } from "./reconciler";
import { AutoMatchPanel } from "./auto-match-panel";

export const dynamic = "force-dynamic";


export default async function PlatformOrdersPage() {
  const api = await getApiClient();
  const [openResult, reconciledResult, inventoryResult, autoMatchResult] = await Promise.all([
    api.GET("/api/platform-orders", { params: { query: { status: "Imported" } } }),
    api.GET("/api/platform-orders", { params: { query: { status: "Reconciled" } } }),
    api.GET("/api/inventory", { params: { query: { includeArchived: false } } }),
    // A preview only — it reports what could be settled and writes nothing.
    api.GET("/api/platform-orders/auto-match/preview", {}),
  ]);

  const openOrders = openResult.data ?? [];
  const reconciled = reconciledResult.data ?? [];
  // Match against ALL inventory (sold + unsold): unsold → new sale, already-recorded → correct that sale.
  const targets = (inventoryResult.data ?? [])
    .filter((item) => item.id && (!item.isBundle || item.bundleComponentCount === 0))
    .map((item) => ({
      id: item.id as string,
      title: item.title ?? "Untitled item",
      isSold: item.isSold ?? false,
    }));

  const flagged = openOrders.filter(
    (order) => typeof order.payoutAmount === "number" && Math.abs(order.payoutVariance ?? 0) >= 0.01
  ).length;

  const kpis = [
    {
      label: "Needs your decision",
      value: String(openOrders.length),
      detail: "Imported platform orders not yet posted to sales",
      icon: ScanLine,
    },
    {
      label: "Payout variances",
      value: String(flagged),
      detail: "Where imported payout ≠ computed expected payout",
      icon: Wallet,
    },
    {
      label: "Reconciled",
      value: String(reconciled.length),
      detail: "Posted with marketplace order + fee data",
      icon: PlugZap,
    },
  ];

  return (
    <div className="space-y-6">
      <header className="space-y-3">
        <Link
          href="/imports"
          className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm"
        >
          <ArrowLeft className="h-4 w-4" /> Imports
        </Link>
        <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
          <div>
            <p className="text-muted-foreground text-sm">Data</p>
            <h1 className="mt-1 text-2xl font-semibold tracking-tight">
              Platform orders &amp; reconciliation
            </h1>
            <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
              Marketplace orders imported from platform reports and normalized manual data.
              Reconciling one posts the platform&apos;s own order number, buyer, item price, buyer
              shipping, sales tax, tracking, and fee details onto ScoutHaul sales — so customers,
              orders, sales, and payouts resolve against the same source data.
            </p>
          </div>
          <Link
            href="/imports"
            className="hover:bg-muted inline-flex h-10 shrink-0 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium"
          >
            <FileSpreadsheet className="h-4 w-4" /> Import reports
          </Link>
        </div>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
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

      <AutoMatchPanel canSettle={autoMatchResult.data?.canSettle ?? 0} />

      <section className="space-y-3">
        <h2 className="text-sm font-semibold">Needs your decision</h2>
        <PlatformOrderReconciler orders={openOrders} targets={targets} />
      </section>

      {reconciled.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-semibold">Recently reconciled</h2>
          <div className="bg-card overflow-x-auto rounded-xl border">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
                <tr>
                  <th className="px-4 py-3 font-medium">Order</th>
                  <th className="px-4 py-3 font-medium">Platform</th>
                  <th className="px-4 py-3 font-medium">Buyer</th>
                  <th className="px-4 py-3 font-medium">Ordered</th>
                  <th className="px-4 py-3 font-medium">Tracking</th>
                  <th className="px-4 py-3 text-right font-medium">Items</th>
                  <th className="px-4 py-3 text-right font-medium">Fees</th>
                  <th className="px-4 py-3 text-right font-medium">Payout</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {reconciled.map((order) => (
                  <tr key={order.id} className="hover:bg-muted/30 transition-colors">
                    <td className="px-4 py-3 font-medium">{order.externalOrderId}</td>
                    <td className="text-muted-foreground px-4 py-3">{order.platform}</td>
                    <td className="text-muted-foreground px-4 py-3">
                      {order.buyerName ?? order.buyerReference ?? "—"}
                    </td>
                    <td className="text-muted-foreground px-4 py-3">{formatDate(order.orderedAt)}</td>
                    <td className="text-muted-foreground px-4 py-3">
                      {order.trackingNumber ?? "—"}
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">
                      {formatCurrency(order.itemsSubtotal)}
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">-{formatCurrency(order.feeTotal)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">
                      {formatCurrency(order.payoutAmount)}
                    </td>
                    <td className="px-4 py-3">
                      <Badge variant="success">Reconciled</Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  );
}
