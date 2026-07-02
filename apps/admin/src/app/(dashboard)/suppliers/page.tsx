import Link from "next/link";
import { Building2, ReceiptText, TrendingUp } from "lucide-react";
import { Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";
const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

type SupplierRollup = { supplier: string; attributed: boolean; lots: number; items: number; spend: number; lastPurchase: string | null };

export default async function SuppliersPage() {
  const api = await getApiClient();
  const { data } = await api.GET("/api/acquisition-lots", {});
  const lots = data ?? [];

  // Suppliers aren't a first-class entity yet — roll them up from the supplier attributed on each lot.
  const rollup = new Map<string, SupplierRollup>();
  for (const lot of lots) {
    const name = lot.supplierName?.trim();
    const key = name || "Unattributed";
    const entry = rollup.get(key) ?? { supplier: key, attributed: Boolean(name), lots: 0, items: 0, spend: 0, lastPurchase: null };
    entry.lots += 1;
    entry.items += lot.items?.length ?? 0;
    entry.spend += lot.totalLandedCost ?? 0;
    if (lot.purchasedAt && (!entry.lastPurchase || lot.purchasedAt > entry.lastPurchase)) entry.lastPurchase = lot.purchasedAt;
    rollup.set(key, entry);
  }
  const suppliers = [...rollup.values()].sort((a, b) => b.spend - a.spend);
  const totalSpend = suppliers.reduce((sum, supplier) => sum + supplier.spend, 0);
  const namedCount = suppliers.filter((supplier) => supplier.attributed).length;

  const kpis = [
    { label: "Suppliers", value: String(namedCount), detail: "Distinct attributed sources", icon: Building2 },
    { label: "Total sourced spend", value: money(totalSpend), detail: `${lots.length} acquisition lots`, icon: ReceiptText },
    { label: "Avg per supplier", value: money(namedCount ? totalSpend / namedCount : 0), detail: "Landed cost per source", icon: TrendingUp },
  ];

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <p className="text-muted-foreground text-sm">Sourcing</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Suppliers</h1>
          <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
            Where your inventory comes from, rolled up from acquisition lots — spend, volume, and recency per source.
          </p>
        </div>
        <Link href="/purchases" className="hover:bg-muted inline-flex h-10 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium">
          Record a purchase
        </Link>
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

      {suppliers.length === 0 ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          No suppliers yet. Record an acquisition lot with a supplier to build this list.
        </div>
      ) : (
        <div className="bg-card overflow-x-auto rounded-xl border">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
              <tr>
                <th className="px-4 py-3 font-medium">Supplier</th>
                <th className="px-4 py-3 text-right font-medium">Lots</th>
                <th className="px-4 py-3 text-right font-medium">Items</th>
                <th className="px-4 py-3 text-right font-medium">Total spend</th>
                <th className="px-4 py-3 font-medium">Last purchase</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {suppliers.map((supplier) => (
                <tr key={supplier.supplier} className="hover:bg-muted/30">
                  <td className="px-4 py-3 font-medium">
                    {supplier.attributed ? supplier.supplier : <span className="text-muted-foreground italic">Unattributed</span>}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{supplier.lots}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{supplier.items}</td>
                  <td className="px-4 py-3 text-right font-medium tabular-nums">{money(supplier.spend)}</td>
                  <td className="text-muted-foreground px-4 py-3">{date(supplier.lastPurchase)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
