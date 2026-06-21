import Link from "next/link";
import { CircleDollarSign, Landmark, Wallet } from "lucide-react";
import { Badge, Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

const statusVariant = (status: string | undefined) => {
  if (["Delivered"].includes(status ?? "")) return "success" as const;
  if (["Returned"].includes(status ?? "")) return "destructive" as const;
  if (["Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

export default async function FinancePage() {
  const api = await getApiClient();
  const { data } = await api.GET("/api/sales", {});
  const sales = data ?? [];

  const grossSales = sales.reduce((sum, sale) => sum + (sale.soldPrice ?? 0), 0);
  const netProfit = sales.reduce((sum, sale) => sum + (sale.netProfit ?? 0), 0);
  const taxOwed = sales.reduce((sum, sale) => sum + (sale.salesTaxOwed ?? 0), 0);

  const kpis = [
    { label: "Gross sales", value: money(grossSales), detail: `${sales.length} recorded sales`, icon: CircleDollarSign },
    { label: "True net profit", value: money(netProfit), detail: "After every fee — tax excluded", icon: Wallet },
    { label: "Sales tax owed", value: money(taxOwed), detail: "Collected by us, owed to the state", icon: Landmark },
  ];

  return (
    <div className="space-y-6">
      <header>
        <p className="text-sm text-muted-foreground">Finance</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Sales &amp; profit</h1>
        <p className="mt-1 text-sm text-muted-foreground">Every recorded sale with its true net after fees and tax. Open a sale to correct it — every change is audited.</p>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        {kpis.map((kpi) => (
          <Card key={kpi.label} className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">{kpi.label}</span>
              <kpi.icon className="h-4 w-4 text-muted-foreground" aria-hidden />
            </div>
            <p className="mt-3 text-3xl font-semibold tabular-nums">{kpi.value}</p>
            <p className="mt-1 text-xs text-muted-foreground">{kpi.detail}</p>
          </Card>
        ))}
      </div>

      {sales.length === 0 ? (
        <p className="rounded-xl border border-dashed bg-muted/20 p-12 text-center text-sm text-muted-foreground">No sales recorded yet.</p>
      ) : (
        <div className="overflow-x-auto rounded-xl border bg-card">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-3 font-medium">Item</th>
                <th className="px-4 py-3 font-medium">Channel</th>
                <th className="px-4 py-3 font-medium">Sold</th>
                <th className="px-4 py-3 text-right font-medium">Price</th>
                <th className="px-4 py-3 text-right font-medium">Tax</th>
                <th className="px-4 py-3 text-right font-medium">True net</th>
                <th className="px-4 py-3 font-medium">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {sales.map((sale) => (
                <tr key={sale.id} className="transition-colors hover:bg-muted/30">
                  <td className="p-0">
                    <Link href={`/finance/${sale.id}`} className="block px-4 py-3 font-medium text-foreground">
                      {sale.inventoryItemTitle || "Item"}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{sale.platform}</td>
                  <td className="px-4 py-3 text-muted-foreground">{date(sale.soldAt)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{money(sale.soldPrice)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {sale.taxRemittedByPlatform ? (
                      <span className="text-xs text-muted-foreground">passed through</span>
                    ) : (
                      <span className="text-amber-700">{money(sale.salesTaxOwed)}</span>
                    )}
                  </td>
                  <td className={`px-4 py-3 text-right font-medium tabular-nums ${(sale.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}>{money(sale.netProfit)}</td>
                  <td className="px-4 py-3"><Badge variant={statusVariant(sale.status)}>{sale.status}</Badge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
