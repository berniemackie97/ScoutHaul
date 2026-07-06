import Link from "next/link";
import { Boxes, DollarSign, PackageCheck, Plus, Search } from "lucide-react";
import { Card, Input } from "@overhaul/ui";
import {
  INVENTORY_STATUSES,
  LIFECYCLE_FILTERS,
  LIFECYCLE_GROUP_VALUES,
  inventoryStatusLabel,
  matchesInventorySearch,
  type InventoryItem,
  type InventoryLifecycleGroup,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { getApiAssetUrl, getApiClient } from "@/lib/api";
import { InventoryTable, type InventoryTableRow } from "./inventory-table";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

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
  const selectedLifecycle = typeof query.lifecycle === "string" && LIFECYCLE_GROUP_VALUES.includes(query.lifecycle as InventoryLifecycleGroup)
    ? query.lifecycle as InventoryLifecycleGroup
    : undefined;

  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory", {
    params: { query: { status: selectedStatus as never, lifecycle: selectedLifecycle, includeArchived: false } },
  });
  const allItems = (data ?? []) as InventoryItem[];
  const items = search
    ? allItems.filter((item) => matchesInventorySearch(item, search))
    : allItems;
  const rows: InventoryTableRow[] = items.map((item) => ({
    ...item,
    thumbnailUrl: getApiAssetUrl(item.heroPhotoThumbnailUrl),
  }));

  const activeItems = allItems.filter((item) => !["Sold", "Delivered", "Donated", "Disposed"].includes(item.status ?? "")).length;
  const readyItems = allItems.filter((item) => ["ReadyToList", "Listed"].includes(item.status ?? "")).length;
  const inventoryCost = allItems.reduce((sum, item) => sum + (item.purchasePrice ?? 0), 0);

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <p className="text-sm text-muted-foreground">Operations</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Inventory control</h1>
          <p className="mt-1 text-sm text-muted-foreground">The authoritative record for every item across intake, preparation, listing, sale, and fulfillment.</p>
        </div>
        <Link href="/inventory/add" className="inline-flex h-10 items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 text-sm font-medium text-white hover:bg-brand-700">
          <Plus className="h-4 w-4" /> Add inventory
        </Link>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        <Card className="p-4"><div className="flex items-center justify-between text-sm text-muted-foreground"><span>Active items</span><Boxes className="h-4 w-4" /></div><p className="mt-2 text-2xl font-semibold tabular-nums">{activeItems}</p></Card>
        <Card className="p-4"><div className="flex items-center justify-between text-sm text-muted-foreground"><span>Ready / listed</span><PackageCheck className="h-4 w-4" /></div><p className="mt-2 text-2xl font-semibold tabular-nums">{readyItems}</p></Card>
        <Card className="p-4"><div className="flex items-center justify-between text-sm text-muted-foreground"><span>Capital deployed</span><DollarSign className="h-4 w-4" /></div><p className="mt-2 text-2xl font-semibold tabular-nums">{money(inventoryCost)}</p></Card>
      </div>

      <form className="grid gap-3 rounded-xl border bg-card p-4 sm:grid-cols-[minmax(0,1fr)_180px_200px_auto]">
        <label className="relative">
          <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
          <Input name="q" defaultValue={typeof query.q === "string" ? query.q : ""} placeholder="Search title, brand, category, barcode, or location" className="pl-9" />
        </label>
        <select name="lifecycle" defaultValue={selectedLifecycle ?? ""} className="h-10 rounded-lg border border-input bg-background px-3 text-sm">
          <option value="">All lifecycle groups</option>
          {LIFECYCLE_FILTERS.map((group) => <option key={group.value} value={group.value}>{group.label}</option>)}
        </select>
        <select name="status" defaultValue={selectedStatus ?? ""} className="h-10 rounded-lg border border-input bg-background px-3 text-sm">
          <option value="">Any exact stage</option>
          {INVENTORY_STATUSES.map((status) => <option key={status} value={status}>{inventoryStatusLabel(status)}</option>)}
        </select>
        <button type="submit" className="h-10 rounded-lg bg-brand-600 px-5 text-sm font-medium text-white hover:bg-brand-700">Apply</button>
      </form>

      <div className="flex items-center justify-between text-sm">
        <span className="text-muted-foreground">{items.length} matching records</span>
        {(search || selectedStatus || selectedLifecycle) && <Link href="/inventory" className="text-brand-600 hover:underline">Clear filters</Link>}
      </div>

      {items.length === 0 ? (
        <p className="rounded-xl border border-dashed bg-muted/20 p-12 text-center text-sm text-muted-foreground">No inventory matches these filters.</p>
      ) : (
        <InventoryTable items={rows} />
      )}
    </div>
  );
}
