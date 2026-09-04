import Link from "next/link";
import { Boxes, DollarSign, PackageCheck, Plus, Search } from "lucide-react";
import { Card, Input, formatCurrency } from "@overhaul/ui";
import {
  INVENTORY_STATUSES,
  LIFECYCLE_FILTERS,
  LIFECYCLE_GROUP_VALUES,
  inventoryStatusLabel,
  type InventoryItem,
  type InventoryLifecycleGroup,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import type { components } from "@overhaul/api-client";
import { getApiAssetUrl, getApiClient } from "@/lib/api";
import { InventoryTable, type InventoryTableRow } from "./inventory-table";

export const dynamic = "force-dynamic";

type InventoryAttention = components["schemas"]["InventoryAttention"];
const ATTENTION_LABELS: Record<InventoryAttention, string> = {
  Unlisted: "Not listed",
  Aged: "Aged over 60 days",
  MissingPhotos: "Missing photos",
  MissingCost: "Missing acquisition cost",
  MissingValue: "Missing expected value",
  MissingLocation: "Missing storage location",
};


export default async function InventoryPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const query = await searchParams;
  const search = typeof query.q === "string" ? query.q.trim() : "";
  const selectedStatus = typeof query.status === "string" && INVENTORY_STATUSES.includes(query.status as InventoryStatus)
    ? query.status as InventoryStatus
    : undefined;
  const selectedLifecycle = typeof query.lifecycle === "string" && LIFECYCLE_GROUP_VALUES.includes(query.lifecycle as InventoryLifecycleGroup)
    ? query.lifecycle as InventoryLifecycleGroup
    : undefined;
  const selectedAttention = typeof query.attention === "string" && query.attention in ATTENTION_LABELS
    ? (query.attention as InventoryAttention)
    : undefined;
  const sort = typeof query.sort === "string" ? query.sort : "updatedAt";
  const desc = query.desc !== "false";
  const page = Math.max(1, Number(query.page) || 1);

  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory/browse", {
    params: {
      query: {
        status: selectedStatus as never,
        lifecycle: selectedLifecycle,
        includeArchived: false,
        search: search || undefined,
        attention: selectedAttention as never,
        sort,
        desc,
        page,
        pageSize: 25,
      },
    },
  });
  const items = (data?.items ?? []) as InventoryItem[];
  const summary = data?.summary;
  const rows: InventoryTableRow[] = items.map((item) => ({
    ...item,
    thumbnailUrl: getApiAssetUrl(item.heroPhotoThumbnailUrl),
  }));

  const activeItems = summary?.activeItems ?? 0;
  const readyItems = summary?.readyOrListed ?? 0;
  const inventoryCost = summary?.capitalDeployed ?? 0;
  const totalMatching = data?.total ?? 0;
  const pageCount = data?.pageCount ?? 1;

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
        <Card className="p-4"><div className="flex items-center justify-between text-sm text-muted-foreground"><span>Capital deployed</span><DollarSign className="h-4 w-4" /></div><p className="mt-2 text-2xl font-semibold tabular-nums">{formatCurrency(inventoryCost)}</p></Card>
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
        {selectedAttention && <input type="hidden" name="attention" value={selectedAttention} />}
        <button type="submit" className="h-10 rounded-lg bg-brand-600 px-5 text-sm font-medium text-white hover:bg-brand-700">Apply</button>
      </form>

      <div className="flex flex-wrap items-center gap-3 text-sm">
        <span className="text-muted-foreground">{totalMatching} matching records</span>
        {selectedAttention && (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-950 dark:text-amber-200">
            {ATTENTION_LABELS[selectedAttention]}
            <Link href="/inventory" aria-label="Clear this filter" className="leading-none hover:text-amber-950 dark:hover:text-amber-50">✕</Link>
          </span>
        )}
        {(search || selectedStatus || selectedLifecycle || selectedAttention) && <Link href="/inventory" className="text-brand-600 hover:underline">Clear filters</Link>}
      </div>

      {totalMatching === 0 ? (
        <p className="rounded-xl border border-dashed bg-muted/20 p-12 text-center text-sm text-muted-foreground">No inventory matches these filters.</p>
      ) : (
        <InventoryTable items={rows} page={page} pageCount={pageCount} sort={sort} desc={desc} />
      )}
    </div>
  );
}
