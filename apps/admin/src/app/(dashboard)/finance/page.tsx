import Link from "next/link";
import { CircleDollarSign, Landmark, Plus, Wallet } from "lucide-react";
import { Badge, Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";

const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

const statusVariant = (status: string | undefined) => {
  if (["Delivered"].includes(status ?? "")) return "success" as const;
  if (["Returned", "Refunded", "Cancelled"].includes(status ?? "")) return "destructive" as const;
  if (["ReturnRequested"].includes(status ?? "")) return "warning" as const;
  if (["Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

export default async function FinancePage() {
  const api = await getApiClient();
  const { data } = await api.GET("/api/sales", {});
  const sales = data ?? [];
  const realizedSales = sales.filter((sale) => !["Returned", "Refunded", "Cancelled"].includes(sale.status ?? ""));

  const grossSales = realizedSales.reduce((sum, sale) => sum + (sale.soldPrice ?? 0), 0);
  const netProfit = realizedSales.reduce((sum, sale) => sum + (sale.netProfit ?? 0), 0);
  const taxOwed = realizedSales.reduce((sum, sale) => sum + (sale.salesTaxOwed ?? 0), 0);

  const kpis = [
    {
      label: "Gross sales",
      value: money(grossSales),
      detail: `${realizedSales.length} realized sales`,
      icon: CircleDollarSign,
    },
    {
      label: "True net profit",
      value: money(netProfit),
      detail: "After every fee — tax excluded",
      icon: Wallet,
    },
    {
      label: "Sales tax owed",
      value: money(taxOwed),
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
        <Link
          href="/finance/new"
          className="bg-brand-600 hover:bg-brand-700 inline-flex h-10 items-center justify-center gap-2 rounded-lg px-4 text-sm font-medium text-white"
        >
          <Plus className="h-4 w-4" /> Record sale
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

      {sales.length === 0 ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          <p>No sales recorded yet.</p>
          <Link href="/finance/new" className="text-foreground mt-3 inline-block font-medium underline">
            Record the first sale
          </Link>
        </div>
      ) : (
        <div className="bg-card overflow-x-auto rounded-xl border">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
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
                <tr key={sale.id} className="hover:bg-muted/30 transition-colors">
                  <td className="p-0">
                    <Link href={`/finance/${sale.id}`} className="text-foreground block px-4 py-3 font-medium">
                      {sale.inventoryItemTitle || "Item"}
                    </Link>
                  </td>
                  <td className="text-muted-foreground px-4 py-3">{sale.platform}</td>
                  <td className="text-muted-foreground px-4 py-3">{date(sale.soldAt)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{money(sale.soldPrice)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {sale.taxRemittedByPlatform ? (
                      <span className="text-muted-foreground text-xs">passed through</span>
                    ) : (
                      <span className="text-amber-700">{money(sale.salesTaxOwed)}</span>
                    )}
                  </td>
                  <td className={`px-4 py-3 text-right font-medium tabular-nums ${(sale.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}>
                    {money(sale.netProfit)}
                  </td>
                  <td className="px-4 py-3">
                    <Badge variant={statusVariant(sale.status)}>{sale.status}</Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
