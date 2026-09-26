import Link from "next/link";
import { Building2, ReceiptText, TrendingUp } from "lucide-react";
import { Card, formatCurrency, formatDate } from "@overhaul/ui";
import { ListEmpty, PageHeader } from "@/components/list/list-search";
import { StatCard, StatRow } from "@/components/list/stat-card";
import { getApiClient } from "@/lib/api";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

/**
 * Where the stock comes from.
 *
 * Suppliers are not a first-class entity yet — they are the name attributed on each acquisition lot.
 * This page used to fetch lots and roll them up in the browser, which meant "Total sourced spend"
 * was the total of however many lots the list endpoint returned: 200 at most, newest first. The
 * rollup now happens over every lot in one query.
 */
export default async function SuppliersPage() {
  const refused = await refusal("/suppliers");
  if (refused) return refused;

  const api = await getApiClient();
  const { data } = await api.GET("/api/acquisition-lots/suppliers", {});

  const suppliers = data?.suppliers ?? [];
  const totalSpend = data?.totalSpend ?? 0;
  const totalLots = data?.totalLots ?? 0;
  const attributed = data?.attributedSuppliers ?? 0;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Sourcing"
        title="Suppliers"
        description="Where your inventory comes from, rolled up from every acquisition lot — spend, volume, and recency per source."
        action={
          <Link
            href="/purchases"
            className="hover:bg-muted inline-flex h-10 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium"
          >
            Record a purchase
          </Link>
        }
      />

      <StatRow>
        <StatCard label="Suppliers" value={String(attributed)} detail="Distinct attributed sources" icon={Building2} />
        <StatCard
          label="Total sourced spend"
          value={formatCurrency(totalSpend)}
          detail={`Landed cost across all ${totalLots} lots`}
          icon={ReceiptText}
        />
        <StatCard
          label="Avg per supplier"
          value={formatCurrency(attributed ? totalSpend / attributed : 0)}
          detail="Landed cost per attributed source"
          icon={TrendingUp}
        />
      </StatRow>

      {suppliers.length === 0 ? (
        <ListEmpty>No suppliers yet. Record an acquisition lot with a supplier to build this list.</ListEmpty>
      ) : (
        <Card className="overflow-x-auto p-0">
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
                    {supplier.attributed ? (
                      supplier.supplier
                    ) : (
                      <span className="text-muted-foreground italic">Unattributed</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{supplier.lots}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{supplier.items}</td>
                  <td className="px-4 py-3 text-right font-medium tabular-nums">{formatCurrency(supplier.spend)}</td>
                  <td className="text-muted-foreground px-4 py-3">{formatDate(supplier.lastPurchase) ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </div>
  );
}
