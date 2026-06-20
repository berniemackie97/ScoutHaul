import { getApiClient } from "@/lib/api";
import { Badge } from "@overhaul/ui";

export const dynamic = "force-dynamic";

const money = (n: number | null | undefined) =>
  typeof n === "number" && n > 0 ? `$${n.toLocaleString()}` : "—";

export default async function InventoryPage() {
  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory", {});
  const items = data ?? [];

  return (
    <div className="space-y-6">
      <header className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Inventory</h1>
        <span className="text-sm text-muted-foreground">{items.length} items</span>
      </header>

      {items.length === 0 ? (
        <p className="rounded-lg border border-border bg-muted/30 p-8 text-center text-sm text-muted-foreground">
          No inventory yet.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full text-sm">
            <thead className="border-b border-border bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-3 font-medium">Item</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium">Category</th>
                <th className="px-4 py-3 font-medium">Condition</th>
                <th className="px-4 py-3 text-right font-medium">Est. price</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {items.map((it) => (
                <tr key={it.id} className="transition-colors hover:bg-muted/30">
                  <td className="px-4 py-3">
                    <span className="font-medium">{it.title}</span>
                    {it.brand ? <span className="ml-2 text-muted-foreground">{it.brand}</span> : null}
                  </td>
                  <td className="px-4 py-3">
                    <Badge variant="secondary">{it.status}</Badge>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{it.category ?? "—"}</td>
                  <td className="px-4 py-3 text-muted-foreground">{it.condition ?? "—"}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{money(it.estimatedSalePrice)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
