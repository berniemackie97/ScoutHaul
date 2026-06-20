import Link from "next/link";
import { Boxes, DollarSign, ImageIcon, PackageCheck, Search } from "lucide-react";
import { Badge, Card, Input } from "@overhaul/ui";
import {
  INVENTORY_STATUSES,
  getInventoryCost,
  getProjectedMetrics,
  inventoryStatusLabel,
  matchesInventorySearch,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { getApiAssetUrl, getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

const badgeVariant = (status: string | undefined) => {
  if (["Listed", "Delivered"].includes(status ?? "")) return "success" as const;
  if (["NeedsCleaning", "NeedsTesting", "NeedsRepair", "NeedsPhotos"].includes(status ?? "")) return "warning" as const;
  if (["Sold", "Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

export default async function InventoryPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const query = await searchParams;
  const search = typeof query.q === "string" ? query.q.trim().toLowerCase() : "";
  const selectedStatus = typeof query.status === "string" && INVENTORY_STATUSES.includes(query.status as InventoryStatus)
    ? query.status as InventoryStatus
    : undefined;

  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory", {
    params: { query: { status: selectedStatus, includeArchived: false } },
  });
  const allItems = data ?? [];
  const items = search
    ? allItems.filter((item) => matchesInventorySearch(item, search))
    : allItems;

  const activeItems = allItems.filter((item) => !["Sold", "Delivered", "Donated", "Disposed"].includes(item.status ?? "")).length;
  const readyItems = allItems.filter((item) => ["ReadyToList", "Listed"].includes(item.status ?? "")).length;
  const inventoryCost = allItems.reduce((sum, item) => sum + (item.purchasePrice ?? 0), 0);

  return (
    <div className="space-y-6">
      <header>
        <p className="text-sm text-muted-foreground">Operations</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Inventory control</h1>
        <p className="mt-1 text-sm text-muted-foreground">The authoritative record for every item across intake, preparation, listing, sale, and fulfillment.</p>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        <Card className="p-4"><div className="flex items-center justify-between text-sm text-muted-foreground"><span>Active items</span><Boxes className="h-4 w-4" /></div><p className="mt-2 text-2xl font-semibold tabular-nums">{activeItems}</p></Card>
        <Card className="p-4"><div className="flex items-center justify-between text-sm text-muted-foreground"><span>Ready / listed</span><PackageCheck className="h-4 w-4" /></div><p className="mt-2 text-2xl font-semibold tabular-nums">{readyItems}</p></Card>
        <Card className="p-4"><div className="flex items-center justify-between text-sm text-muted-foreground"><span>Capital deployed</span><DollarSign className="h-4 w-4" /></div><p className="mt-2 text-2xl font-semibold tabular-nums">{money(inventoryCost)}</p></Card>
      </div>

      <form className="grid gap-3 rounded-xl border bg-card p-4 sm:grid-cols-[minmax(0,1fr)_220px_auto]">
        <label className="relative">
          <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
          <Input name="q" defaultValue={typeof query.q === "string" ? query.q : ""} placeholder="Search title, brand, category, barcode, or location" className="pl-9" />
        </label>
        <select name="status" defaultValue={selectedStatus ?? ""} className="h-10 rounded-lg border border-input bg-background px-3 text-sm">
          <option value="">All lifecycle stages</option>
          {INVENTORY_STATUSES.map((status) => <option key={status} value={status}>{inventoryStatusLabel(status)}</option>)}
        </select>
        <button type="submit" className="h-10 rounded-lg bg-brand-600 px-5 text-sm font-medium text-white hover:bg-brand-700">Apply</button>
      </form>

      <div className="flex items-center justify-between text-sm">
        <span className="text-muted-foreground">{items.length} matching records</span>
        {(search || selectedStatus) && <Link href="/inventory" className="text-brand-600 hover:underline">Clear filters</Link>}
      </div>

      {items.length === 0 ? (
        <p className="rounded-xl border border-dashed bg-muted/20 p-12 text-center text-sm text-muted-foreground">No inventory matches these filters.</p>
      ) : (
        <div className="overflow-x-auto rounded-xl border bg-card">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-3 font-medium">Item</th>
                <th className="px-4 py-3 font-medium">Stage</th>
                <th className="px-4 py-3 font-medium">Location</th>
                <th className="px-4 py-3 text-right font-medium">Cost</th>
                <th className="px-4 py-3 text-right font-medium">Expected</th>
                <th className="px-4 py-3 text-right font-medium">Projected profit</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((item) => {
                const thumbnail = getApiAssetUrl(item.heroPhotoThumbnailUrl);
                const cost = getInventoryCost(item);
                const { profit: projectedProfit } = getProjectedMetrics(item);
                return (
                  <tr key={item.id} className="transition-colors hover:bg-muted/30">
                    <td className="p-0">
                      <Link href={`/inventory/${item.id}`} className="flex min-w-72 items-center gap-3 px-4 py-3">
                        <div className="flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-lg border bg-muted">
                          {thumbnail ? (
                            // eslint-disable-next-line @next/next/no-img-element
                            <img src={thumbnail} alt="" className="h-full w-full object-cover" />
                          ) : <ImageIcon className="h-5 w-5 text-muted-foreground" />}
                        </div>
                        <div className="min-w-0">
                          <p className="truncate font-medium text-foreground">{item.title}</p>
                          <p className="truncate text-xs text-muted-foreground">{[item.brand, item.model, item.category].filter(Boolean).join(" · ") || "Uncategorized"}</p>
                        </div>
                      </Link>
                    </td>
                    <td className="px-4 py-3"><Badge variant={badgeVariant(item.status)}>{inventoryStatusLabel(item.status)}</Badge></td>
                    <td className="px-4 py-3 text-muted-foreground">{item.storageLocation ?? "—"}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{money(cost)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{money(item.estimatedSalePrice)}</td>
                    <td className={`px-4 py-3 text-right font-medium tabular-nums ${projectedProfit != null && projectedProfit < 0 ? "text-destructive" : ""}`}>{money(projectedProfit)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
