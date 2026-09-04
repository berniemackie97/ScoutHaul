import Link from "next/link";
import { ArrowRight, ReceiptText } from "lucide-react";
import { Badge, Card, formatCurrency, formatDate } from "@overhaul/ui";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { getApiClient } from "@/lib/api";
import { PurchaseLotCreator } from "./purchase-lot-creator";

export const dynamic = "force-dynamic";



export default async function PurchasesPage() {
  const api = await getApiClient();
  const [{ data: lots }, { data: inventory }] = await Promise.all([
    api.GET("/api/acquisition-lots"),
    api.GET("/api/inventory", { params: { query: { lifecycle: "Active", includeArchived: false } } }),
  ]);

  const acquisitionLots = lots ?? [];
  const candidates = ((inventory ?? []) as InventoryItem[]).filter((item) => !item.acquisitionLotId && item.status !== "Bundled");
  const totalSpend = acquisitionLots.reduce((sum, lot) => sum + (lot.totalLandedCost ?? 0), 0);
  const allocated = acquisitionLots.reduce((sum, lot) => sum + (lot.totalAllocatedCost ?? 0), 0);

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <p className="text-sm text-muted-foreground">Operations</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Purchases &amp; acquisition lots</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            Record a single-item purchase or a multi-item receipt, pallet, haul, auction pickup, or loose lot, then allocate landed cost into inventory for accurate COGS and historical profit.
          </p>
        </div>
        <Link href="/inventory" className="inline-flex h-10 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium hover:bg-muted">
          Inventory <ArrowRight className="h-4 w-4" />
        </Link>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        <Card className="p-5"><div className="flex items-center justify-between text-sm text-muted-foreground"><span>Total purchase spend</span><ReceiptText className="h-4 w-4" /></div><p className="mt-3 text-3xl font-semibold tabular-nums">{formatCurrency(totalSpend)}</p></Card>
        <Card className="p-5"><p className="text-sm text-muted-foreground">Allocated COGS</p><p className="mt-3 text-3xl font-semibold tabular-nums">{formatCurrency(allocated)}</p></Card>
        <Card className="p-5"><p className="text-sm text-muted-foreground">Open candidates</p><p className="mt-3 text-3xl font-semibold tabular-nums">{candidates.length}</p></Card>
      </div>

      <PurchaseLotCreator candidates={candidates} />

      <section className="space-y-3">
        <div>
          <h2 className="text-lg font-semibold">Recorded purchase records</h2>
          <p className="text-sm text-muted-foreground">Each record creates one purchase expense and allocated item-level cost basis.</p>
        </div>
        {acquisitionLots.length === 0 ? (
          <p className="rounded-xl border border-dashed bg-muted/20 p-10 text-center text-sm text-muted-foreground">No purchase records recorded yet.</p>
        ) : (
          <div className="overflow-x-auto rounded-xl border bg-card">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-4 py-3 font-medium">Purchase record</th>
                  <th className="px-4 py-3 font-medium">Purchased</th>
                  <th className="px-4 py-3 font-medium">Allocation</th>
                  <th className="px-4 py-3 text-right font-medium">Items</th>
                  <th className="px-4 py-3 text-right font-medium">Landed cost</th>
                  <th className="px-4 py-3 text-right font-medium">Allocated</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {acquisitionLots.map((lot) => (
                  <tr key={lot.id} className="hover:bg-muted/30">
                    <td className="px-4 py-3">
                      <Link href={`/purchases/${lot.id}`} className="font-medium hover:underline">{lot.name || "Untitled purchase"}</Link>
                      <p className="text-xs text-muted-foreground">{[lot.supplierName, lot.externalReference].filter(Boolean).join(" · ") || "No supplier/reference"}</p>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{formatDate(lot.purchasedAt)}</td>
                    <td className="px-4 py-3"><Badge variant="secondary">{lot.allocationMethod}</Badge></td>
                    <td className="px-4 py-3 text-right tabular-nums">{lot.items?.length ?? 0}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(lot.totalLandedCost)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(lot.totalAllocatedCost)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
