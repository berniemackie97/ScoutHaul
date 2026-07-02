import Link from "next/link";
import { Handshake, Package, Tag } from "lucide-react";
import { Badge, Card } from "@overhaul/ui";
import { inventoryStatusLabel } from "@/features/inventory/inventory-operations";
import { getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";

export default async function ConsignmentsPage() {
  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory", { params: { query: { includeArchived: false } } });
  const consigned = (data ?? []).filter((item) => item.ownershipType === "Consigned");

  const askingValue = consigned.reduce((sum, item) => sum + (item.sellerAskingPrice ?? 0), 0);
  const estimatedValue = consigned.reduce((sum, item) => sum + (item.estimatedSalePrice ?? 0), 0);

  const kpis = [
    { label: "Consigned items", value: String(consigned.length), detail: "Owned by a consignor", icon: Handshake },
    { label: "Asking value", value: money(askingValue), detail: "Sum of consignor asks", icon: Tag },
    { label: "Estimated resale", value: money(estimatedValue), detail: "Projected sale value", icon: Package },
  ];

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Sourcing</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Consignments</h1>
        <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
          Inventory you sell on someone else&rsquo;s behalf — items flagged as consigned ownership. Set an item&rsquo;s ownership to
          Consigned on its record to track it here.
        </p>
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

      {consigned.length === 0 ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          No consigned inventory. Set an item&rsquo;s ownership type to <strong>Consigned</strong> to surface it here.
        </div>
      ) : (
        <div className="bg-card overflow-x-auto rounded-xl border">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
              <tr>
                <th className="px-4 py-3 font-medium">Item</th>
                <th className="px-4 py-3 font-medium">Stage</th>
                <th className="px-4 py-3 text-right font-medium">Consignor ask</th>
                <th className="px-4 py-3 text-right font-medium">Est. resale</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {consigned.map((item) => (
                <tr key={item.id} className="hover:bg-muted/30">
                  <td className="p-0">
                    <Link href={`/inventory/${item.id}`} className="text-foreground block px-4 py-3 font-medium">
                      {item.title}
                      <span className="text-muted-foreground block text-xs">
                        {[item.category, item.brand, item.storageLocation].filter(Boolean).join(" · ") || "No classification"}
                      </span>
                    </Link>
                  </td>
                  <td className="px-4 py-3">
                    <Badge variant="secondary">{inventoryStatusLabel(item.status)}</Badge>
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{money(item.sellerAskingPrice)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{money(item.estimatedSalePrice)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
