import Link from "next/link";
import { Mail, Users, Wallet } from "lucide-react";
import { Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";
const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

export default async function CustomersPage({ searchParams }: { searchParams: Promise<{ q?: string }> }) {
  const { q } = await searchParams;
  const api = await getApiClient();
  const { data } = await api.GET("/api/customers", { params: { query: q ? { search: q } : {} } });
  const customers = data ?? [];

  const marketingOptIns = customers.filter((customer) => customer.acceptsMarketing).length;
  const creditLiability = customers.reduce((sum, customer) => sum + (customer.storeCreditBalance ?? 0), 0);

  const kpis = [
    { label: "Customers", value: String(customers.length), detail: q ? `Matching “${q}”` : "Storefront shoppers", icon: Users },
    { label: "Marketing opt-ins", value: String(marketingOptIns), detail: "Consented to marketing", icon: Mail },
    { label: "Store credit liability", value: money(creditLiability), detail: "Outstanding customer credit", icon: Wallet },
  ];

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Storefront</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Customers</h1>
        <p className="text-muted-foreground mt-1 max-w-3xl text-sm">Storefront shoppers, their order history, lifetime value, and store credit.</p>
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

      <form className="flex gap-2" action="/customers">
        <input
          name="q"
          defaultValue={q ?? ""}
          placeholder="Search by name or email…"
          className="border-input bg-background focus:ring-ring h-10 w-full max-w-sm rounded-lg border px-3 text-sm focus:outline-none focus:ring-2"
        />
        <button type="submit" className="hover:bg-muted h-10 rounded-lg border px-4 text-sm font-medium">Search</button>
        {q && (
          <Link href="/customers" className="text-muted-foreground inline-flex h-10 items-center px-2 text-sm hover:text-foreground">
            Clear
          </Link>
        )}
      </form>

      {customers.length === 0 ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          {q ? `No customers matching “${q}”.` : "No customers yet."}
        </div>
      ) : (
        <div className="bg-card overflow-x-auto rounded-xl border">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
              <tr>
                <th className="px-4 py-3 font-medium">Customer</th>
                <th className="px-4 py-3 text-right font-medium">Orders</th>
                <th className="px-4 py-3 text-right font-medium">Lifetime spend</th>
                <th className="px-4 py-3 text-right font-medium">Store credit</th>
                <th className="px-4 py-3 font-medium">Last order</th>
                <th className="px-4 py-3 font-medium">Joined</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {customers.map((customer) => (
                <tr key={customer.id} className="hover:bg-muted/30 transition-colors">
                  <td className="p-0">
                    <Link href={`/customers/${customer.id}`} className="block px-4 py-3">
                      <span className="text-foreground font-medium">{customer.displayName || customer.email}</span>
                      {customer.displayName && <span className="text-muted-foreground block text-xs">{customer.email}</span>}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{customer.orderCount}</td>
                  <td className="px-4 py-3 text-right font-medium tabular-nums">{money(customer.lifetimeSpend)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{money(customer.storeCreditBalance)}</td>
                  <td className="text-muted-foreground px-4 py-3">{date(customer.lastOrderAt)}</td>
                  <td className="text-muted-foreground px-4 py-3">{date(customer.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
