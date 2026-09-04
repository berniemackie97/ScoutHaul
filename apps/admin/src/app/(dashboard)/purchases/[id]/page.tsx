import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { Badge, Card, formatCurrency, formatDate } from "@overhaul/ui";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { getApiClient } from "@/lib/api";
import { ItemizeForm } from "./itemize-form";
import { LotEditor } from "./lot-editor";
import { AttachItems } from "./attach-items";

export const dynamic = "force-dynamic";

const methodLabel: Record<string, string> = {
  Equal: "Equal split",
  WeightedByExpectedValue: "Weighted by expected value",
  Manual: "Manual allocation",
};

export default async function AcquisitionLotDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const api = await getApiClient();
  const [{ data: lot }, { data: inventory }] = await Promise.all([
    api.GET("/api/acquisition-lots/{id}", { params: { path: { id } } }),
    api.GET("/api/inventory", { params: { query: { lifecycle: "Active", includeArchived: false } } }),
  ]);
  if (!lot) notFound();

  const attachCandidates = ((inventory ?? []) as InventoryItem[]).filter((item) => !item.acquisitionLotId && item.status !== "Bundled" && !item.isSold);

  const items = lot.items ?? [];
  const allocated = items.reduce((sum, item) => sum + (item.allocatedCost ?? 0), 0);
  const unallocated = (lot.totalLandedCost ?? 0) - allocated;
  const costLines: Array<[string, number | null | undefined]> = [
    ["Subtotal", lot.purchaseSubtotal],
    ["Shipping", lot.shippingCost],
    ["Tax", lot.taxCost],
    ["Buyer fees", lot.feeCost],
    ["Other", lot.otherCost],
  ];

  return (
    <div className="space-y-6">
      <div>
        <Link href="/purchases" className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Purchases
        </Link>
        <div className="mt-2 flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">{lot.name}</h1>
            <p className="text-muted-foreground mt-1 text-sm">
              {[lot.supplierName, lot.externalReference].filter(Boolean).join(" · ") || "No supplier / reference"} · purchased {formatDate(lot.purchasedAt)}
            </p>
          </div>
          <Badge variant="secondary">{methodLabel[lot.allocationMethod ?? "Equal"] ?? lot.allocationMethod}</Badge>
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <Card className="p-5">
          <p className="text-muted-foreground text-sm">Total landed cost</p>
          <p className="mt-3 text-3xl font-semibold tabular-nums">{formatCurrency(lot.totalLandedCost)}</p>
          <div className="mt-4 space-y-1 border-t pt-3 text-sm">
            {costLines.map(([label, value]) => (
              <div key={label} className="flex justify-between">
                <span className="text-muted-foreground">{label}</span>
                <span className="tabular-nums">{formatCurrency(value)}</span>
              </div>
            ))}
          </div>
        </Card>
        <Card className="p-5">
          <p className="text-muted-foreground text-sm">Allocated to items</p>
          <p className="mt-3 text-3xl font-semibold tabular-nums">{formatCurrency(allocated)}</p>
          <p className="text-muted-foreground mt-2 text-xs">
            {Math.abs(unallocated) < 0.01 ? "Fully allocated across all items." : `${formatCurrency(unallocated)} not yet allocated.`}
          </p>
        </Card>
        <Card className="p-5">
          <p className="text-muted-foreground text-sm">Items in lot</p>
          <p className="mt-3 text-3xl font-semibold tabular-nums">{items.length}</p>
          <p className="text-muted-foreground mt-2 text-xs">Each carries its allocated share as cost basis.</p>
        </Card>
      </div>

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Items sourced from this lot</h2>
        {items.length === 0 ? (
          <p className="text-muted-foreground rounded-xl border border-dashed p-8 text-center text-sm">
            No items yet. Use “Itemize this lot” below to inventory each piece.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-xl border bg-card">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-4 py-3 font-medium">Item</th>
                  <th className="px-4 py-3 font-medium">Stage</th>
                  <th className="px-4 py-3 text-right font-medium">Allocated cost</th>
                  <th className="px-4 py-3 text-right font-medium">Weight</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {items.map((item) => (
                  <tr key={item.inventoryItemId} className="hover:bg-muted/30">
                    <td className="px-4 py-3">
                      <Link href={`/inventory/${item.inventoryItemId}`} className="font-medium hover:underline">
                        {item.inventoryItemTitle}
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{item.inventoryItemStatus}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(item.allocatedCost)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{item.allocationWeight ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <AttachItems lotId={lot.id!} candidates={attachCandidates} />
      <ItemizeForm lotId={lot.id!} allocationMethod={(lot.allocationMethod ?? "Equal") as "Equal" | "WeightedByExpectedValue" | "Manual"} />
      <LotEditor lot={lot} />
    </div>
  );
}
