import Link from "next/link";
import { Boxes, DollarSign, PackageCheck, Plus, Search, TrendingUp, X } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Input, formatCurrency } from "@overhaul/ui";
import { PageHeader } from "@/components/list/list-search";
import { Pager } from "@/components/list/pager";
import { StatCard, StatRow } from "@/components/list/stat-card";
import {
  INVENTORY_STATUSES,
  LIFECYCLE_GROUP_VALUES,
  inventoryStatusLabel,
  type InventoryItem,
  type InventoryLifecycleGroup,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { getApiAssetUrl, getApiClient } from "@/lib/api";
import { refusal } from "@/lib/page-access";
import { InventoryTable, type InventoryTableRow } from "./inventory-table";

export const dynamic = "force-dynamic";

type InventoryAttention = components["schemas"]["InventoryAttention"];
const ATTENTION_LABELS: Record<InventoryAttention, string> = {
  Unlisted: "Not listed",
  Aged: "Held over 60 days",
  MissingPhotos: "Missing photos",
  MissingCost: "Missing cost",
  MissingValue: "Missing expected price",
  MissingLocation: "Missing storage location",
};

const TABS: Array<{ key: InventoryLifecycleGroup | "All"; label: string }> = [
  { key: "Active", label: "In stock" },
  { key: "Sold", label: "Sold" },
  { key: "Returned", label: "Returned" },
  { key: "Removed", label: "Removed" },
  { key: "All", label: "All" },
];

export default async function InventoryPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const refused = await refusal("/inventory");
  if (refused) return refused;

  const query = await searchParams;
  const one = (value: string | string[] | undefined) => (typeof value === "string" ? value : undefined);
  const search = one(query.q)?.trim() ?? "";
  const status = INVENTORY_STATUSES.includes(one(query.status) as InventoryStatus) ? (one(query.status) as InventoryStatus) : undefined;
  const attention = one(query.attention) && one(query.attention)! in ATTENTION_LABELS ? (one(query.attention) as InventoryAttention) : undefined;
  // A link that already names a stage or a problem means "all of those", whatever lifecycle they are in.
  const requested = one(query.lifecycle);
  const tab: InventoryLifecycleGroup | "All" =
    requested === "All" || LIFECYCLE_GROUP_VALUES.includes(requested as InventoryLifecycleGroup)
      ? (requested as InventoryLifecycleGroup | "All")
      : status || attention
        ? "All"
        : "Active";
  const sort = one(query.sort) ?? "updatedAt";
  const desc = query.desc !== "false";
  const page = Math.max(1, Number(query.page) || 1);

  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory/browse", {
    params: {
      query: {
        status,
        lifecycle: tab === "All" ? undefined : tab,
        includeArchived: false,
        search: search || undefined,
        attention,
        sort,
        desc,
        page,
        pageSize: 25,
      },
    },
  });

  const factsById = new Map((data?.facts ?? []).map((facts) => [facts.inventoryItemId, facts]));
  const rows: InventoryTableRow[] = ((data?.items ?? []) as InventoryItem[]).map((item) => ({
    ...item,
    thumbnailUrl: getApiAssetUrl(item.heroPhotoThumbnailUrl),
    facts: item.id ? factsById.get(item.id) : undefined,
  }));
  const summary = data?.summary;
  const counts = new Map((data?.lifecycleCounts ?? []).map((entry) => [entry.group, entry.count ?? 0]));
  const countFor = (key: InventoryLifecycleGroup | "All") =>
    key === "All" ? [...counts.values()].reduce((sum, value) => sum + value, 0) : (counts.get(key) ?? 0);

  // "In stock" is the default only with no stage or problem filter, so name it whenever it is not implied.
  const lifecycleParam = (key: InventoryLifecycleGroup | "All") => (key === "Active" && !status && !attention ? undefined : key);
  const href = (updates: Record<string, string | undefined>) => {
    const params = new URLSearchParams();
    const merged = { q: search || undefined, status, attention, lifecycle: lifecycleParam(tab), ...updates };
    for (const [key, value] of Object.entries(merged)) if (value) params.set(key, value);
    const text = params.toString();
    return text ? `/inventory?${text}` : "/inventory";
  };
  const filtered = Boolean(search || status || attention);

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Operations"
        title="Inventory"
        description="Every item from intake to sale. Select items to move them to a shelf, set their stage, or archive them."
        action={
          <Link href="/inventory/add" className="bg-brand-600 hover:bg-brand-700 inline-flex h-10 shrink-0 items-center justify-center gap-2 rounded-lg px-4 text-sm font-medium text-white">
            <Plus className="h-4 w-4" /> Add inventory
          </Link>
        }
      />

      <StatRow columns={4}>
        <StatCard label="In stock" value={String(summary?.activeItems ?? 0)} detail="Unsold items matching this view" icon={Boxes} />
        <StatCard label="Ready or listed" value={String(summary?.readyOrListed ?? 0)} detail="Can be sold today" icon={PackageCheck} />
        <StatCard label="Cost tied up" value={formatCurrency(summary?.capitalDeployed ?? 0)} detail="What these items cost you" icon={DollarSign} />
        <StatCard label="Expected value" value={formatCurrency(summary?.estimatedResaleValue ?? 0)} detail="At your expected prices" icon={TrendingUp} />
      </StatRow>

      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <nav className="flex flex-wrap gap-2" aria-label="Lifecycle">
          {TABS.map((entry) => (
            <Link
              key={entry.key}
              href={href({ lifecycle: lifecycleParam(entry.key), page: undefined })}
              aria-current={tab === entry.key ? "page" : undefined}
              className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm transition ${
                tab === entry.key ? "bg-foreground text-background border-foreground" : "text-muted-foreground hover:bg-muted"
              }`}
            >
              {entry.label}
              <span className="tabular-nums opacity-70">{countFor(entry.key)}</span>
            </Link>
          ))}
        </nav>
        <form className="flex flex-1 flex-wrap items-center gap-2 lg:max-w-xl lg:justify-end" action="/inventory">
          {lifecycleParam(tab) && <input type="hidden" name="lifecycle" value={tab} />}
          {attention && <input type="hidden" name="attention" value={attention} />}
          <label className="relative min-w-[220px] flex-1">
            <Search className="text-muted-foreground absolute left-3 top-2.5 h-4 w-4" aria-hidden />
            <Input name="q" defaultValue={search} placeholder="Search title, brand, SKU or location" className="h-9 pl-9" />
          </label>
          <select name="status" defaultValue={status ?? ""} aria-label="Stage" className="border-input bg-background h-9 rounded-md border px-2 text-sm">
            <option value="">Any stage</option>
            {INVENTORY_STATUSES.map((value) => (
              <option key={value} value={value}>{inventoryStatusLabel(value)}</option>
            ))}
          </select>
          <button type="submit" className="hover:bg-muted h-9 rounded-md border px-3 text-sm font-medium">Search</button>
        </form>
      </div>

      {filtered && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          {search && <FilterChip label={`“${search}”`} clear={href({ q: undefined, page: undefined })} />}
          {status && <FilterChip label={inventoryStatusLabel(status)} clear={href({ status: undefined, page: undefined })} />}
          {attention && <FilterChip label={ATTENTION_LABELS[attention]} clear={href({ attention: undefined, page: undefined })} />}
          <Link href="/inventory" className="text-brand-700 text-xs hover:underline">Clear all</Link>
        </div>
      )}

      {rows.length === 0 ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          {filtered ? "Nothing matches these filters." : tab === "Active" ? "No items in stock yet. Add your first item to get started." : "Nothing here yet."}
        </div>
      ) : (
        <InventoryTable items={rows} sort={sort} desc={desc} />
      )}

      <Pager page={data?.page ?? 1} pageCount={data?.pageCount ?? 1} total={data?.total ?? 0} noun="item" />
    </div>
  );
}

function FilterChip({ label, clear }: { label: string; clear: string }) {
  return (
    <span className="bg-muted inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium">
      {label}
      <Link href={clear} aria-label={`Remove ${label}`} className="hover:text-foreground text-muted-foreground">
        <X className="h-3 w-3" />
      </Link>
    </span>
  );
}
