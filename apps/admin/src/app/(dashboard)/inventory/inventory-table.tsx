"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useMemo, useState, useTransition } from "react";
import { Archive, ArrowDown, ArrowUp, GitMerge, ImageIcon, MapPin, Tags, X } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Input, formatCurrency } from "@overhaul/ui";
import { When } from "@/components/when";
import {
  MANUAL_INVENTORY_STATUSES,
  TERMINAL_STATUSES,
  getInventoryCost,
  getProjectedMetrics,
  inventoryStatusLabel,
  type InventoryItem,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { daysHeld, presenceLabel } from "@/features/inventory/inventory-rows";
import { archiveInventoryItems, moveInventoryItems, setInventoryStage, type BulkInventoryResult } from "./actions";
import { MAX_MERGE, MergeDialog } from "./merge-dialog";

type Facts = components["schemas"]["InventoryRowFactsDto"];

export type InventoryTableRow = InventoryItem & { thumbnailUrl: string | null; facts?: Facts };

const badgeVariant = (status: string | undefined) => {
  if (["Listed", "Delivered"].includes(status ?? "")) return "success" as const;
  if (["NeedsCleaning", "NeedsTesting", "NeedsRepair", "NeedsPhotos"].includes(status ?? "")) return "warning" as const;
  if (["Sold", "Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

type Tool = "location" | "stage" | "merge" | null;

export function InventoryTable({ items, sort = "updatedAt", desc = true }: { items: InventoryTableRow[]; sort?: string; desc?: boolean }) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [result, setResult] = useState<BulkInventoryResult | null>(null);
  const [tool, setTool] = useState<Tool>(null);
  const [location, setLocation] = useState("");
  const [stage, setStage] = useState<InventoryStatus>("ReadyToList");
  const [pending, startTransition] = useTransition();

  const selected = useMemo(() => items.filter((item) => item.id && selectedIds.has(item.id)), [items, selectedIds]);
  const allSelected = items.length > 0 && items.every((item) => item.id && selectedIds.has(item.id));
  const targets = selected.map((item) => ({ id: item.id!, title: item.title ?? "Item" }));

  function toggle(id: string) {
    setResult(null);
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAll() {
    setResult(null);
    setSelectedIds(allSelected ? new Set() : new Set(items.map((item) => item.id!).filter(Boolean)));
  }

  function finish(next: BulkInventoryResult) {
    setResult(next);
    if (next.ok) {
      setSelectedIds(new Set());
      setTool(null);
      router.refresh();
    }
  }

  function run(work: () => Promise<BulkInventoryResult>) {
    setResult(null);
    startTransition(async () => finish(await work()));
  }

  function archive() {
    if (!window.confirm(`Archive ${selected.length} item${selected.length === 1 ? "" : "s"}? They leave the active list but stay in history and reports. Listed or sold items are refused.`)) return;
    run(() => archiveInventoryItems(targets));
  }

  function applyStage() {
    if (TERMINAL_STATUSES.includes(stage) && !window.confirm(`Mark ${selected.length} item${selected.length === 1 ? "" : "s"} ${inventoryStatusLabel(stage).toLowerCase()}? This removes them from active stock.`)) return;
    run(() => setInventoryStage(targets, stage));
  }

  function applySort(column: string) {
    const next = new URLSearchParams(searchParams.toString());
    next.set("sort", column);
    if (sort === column && desc) next.set("desc", "false");
    else next.delete("desc");
    next.delete("page");
    router.push(`${pathname}?${next.toString()}`);
  }

  const SortHeader = ({ column, label, right = false, className = "" }: { column: string; label: string; right?: boolean; className?: string }) => (
    <th className={`px-4 py-3 font-medium ${right ? "text-right" : ""} ${className}`}>
      <button type="button" onClick={() => applySort(column)} className={`hover:text-foreground inline-flex items-center gap-1 uppercase ${right ? "flex-row-reverse" : ""}`}>
        {label}
        {sort === column && (desc ? <ArrowDown className="h-3 w-3" /> : <ArrowUp className="h-3 w-3" />)}
      </button>
    </th>
  );

  return (
    <div className="space-y-3">
      {(selected.length > 0 || result) && (
        <div className="bg-card sticky top-2 z-10 space-y-3 rounded-xl border p-3 shadow-sm">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <div className="flex min-w-0 items-center gap-3 text-sm">
              {selected.length > 0 && (
                <>
                  <span className="font-medium">{selected.length} selected</span>
                  <button type="button" onClick={() => setSelectedIds(new Set())} className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-xs">
                    <X className="h-3.5 w-3.5" /> Clear
                  </button>
                </>
              )}
              {result?.message && <span className={result.ok ? "text-success" : "text-destructive"}>{result.message}</span>}
            </div>
            {selected.length > 0 && (
              <div className="flex flex-wrap gap-2">
                <Button size="sm" variant={tool === "location" ? "secondary" : "outline"} onClick={() => setTool(tool === "location" ? null : "location")}>
                  <MapPin className="mr-1.5 h-4 w-4" aria-hidden /> Move to…
                </Button>
                <Button size="sm" variant={tool === "stage" ? "secondary" : "outline"} onClick={() => setTool(tool === "stage" ? null : "stage")}>
                  <Tags className="mr-1.5 h-4 w-4" aria-hidden /> Set stage
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={pending || selected.length < 2 || selected.length > MAX_MERGE}
                  title={selected.length < 2 ? "Select the duplicate records of one item" : selected.length > MAX_MERGE ? `Merge up to ${MAX_MERGE} records at a time` : undefined}
                  onClick={() => setTool("merge")}
                >
                  <GitMerge className="mr-1.5 h-4 w-4" aria-hidden /> Merge
                </Button>
                <Button size="sm" variant="outline" disabled={pending} onClick={archive}>
                  <Archive className="mr-1.5 h-4 w-4" aria-hidden /> Archive
                </Button>
              </div>
            )}
          </div>
          {tool === "location" && selected.length > 0 && (
            <form
              className="flex flex-wrap items-center gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                run(() => moveInventoryItems(targets.map((target) => target.id), location));
              }}
            >
              <Input autoFocus value={location} onChange={(event) => setLocation(event.target.value)} placeholder="Bin, shelf or tote — blank clears it" className="h-9 max-w-xs" maxLength={160} />
              <Button type="submit" size="sm" disabled={pending}>{pending ? "Moving…" : `Move ${selected.length}`}</Button>
            </form>
          )}
          {tool === "stage" && selected.length > 0 && (
            <div className="flex flex-wrap items-center gap-2">
              <select
                value={stage}
                onChange={(event) => setStage(event.target.value as InventoryStatus)}
                className="border-input bg-background h-9 rounded-md border px-3 text-sm"
                aria-label="Stage"
              >
                {MANUAL_INVENTORY_STATUSES.map((option) => (
                  <option key={option.value} value={option.value}>{option.label}</option>
                ))}
              </select>
              <Button size="sm" disabled={pending} onClick={applyStage}>{pending ? "Updating…" : `Set ${selected.length}`}</Button>
              <span className="text-muted-foreground text-xs">Items with a sale, or not ready to list, are refused and named.</span>
            </div>
          )}
        </div>
      )}
      {tool === "merge" && selected.length > 1 && <MergeDialog rows={selected} onClose={() => setTool(null)} />}

      <div className="bg-card overflow-x-auto rounded-xl border">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
            <tr>
              <th className="w-10 px-4 py-3">
                <input type="checkbox" aria-label="Select every item on this page" checked={allSelected} onChange={toggleAll} className="border-input h-4 w-4 rounded" />
              </th>
              <SortHeader column="title" label="Item" />
              <th className="px-4 py-3 font-medium">Stage</th>
              <th className="hidden px-4 py-3 font-medium lg:table-cell">Channels</th>
              <SortHeader column="acquiredAt" label="Held" className="hidden md:table-cell" />
              <SortHeader column="purchasePrice" label="Cost" right className="hidden md:table-cell" />
              <SortHeader column="estimatedSalePrice" label="Price" right />
              <th className="px-4 py-3 text-right font-medium">Profit</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {items.map((item) => {
              const facts = item.facts;
              const sold = Boolean(item.isSold && facts?.soldPrice != null);
              const cost = getInventoryCost(item);
              const { profit: projected } = getProjectedMetrics(item);
              const profit = sold ? facts!.netProfit : projected;
              const subtitle = [item.brand, item.category, item.isBundle ? `${item.bundleComponentCount ?? 0} in bundle` : null]
                .filter(Boolean)
                .join(" · ");
              const checked = Boolean(item.id && selectedIds.has(item.id));
              const days = facts ? daysHeld(facts.acquiredAt) : null;

              return (
                <tr key={item.id} className={`hover:bg-muted/30 transition-colors ${checked ? "bg-brand-500/5" : ""}`}>
                  <td className="px-4 py-3">
                    <input type="checkbox" aria-label={`Select ${item.title}`} checked={checked} onChange={() => item.id && toggle(item.id)} className="border-input h-4 w-4 rounded" />
                  </td>
                  <td className="p-0">
                    <Link href={`/inventory/${item.id}`} className="flex min-w-0 items-center gap-3 px-4 py-3 sm:min-w-64">
                      <div className="bg-muted flex h-11 w-11 shrink-0 items-center justify-center overflow-hidden rounded-md border">
                        {item.thumbnailUrl ? (
                          // eslint-disable-next-line @next/next/no-img-element
                          <img src={item.thumbnailUrl} alt="" className="h-full w-full object-cover" loading="lazy" />
                        ) : (
                          <ImageIcon className="text-muted-foreground h-5 w-5" />
                        )}
                      </div>
                      <div className="min-w-0">
                        <p className="text-foreground max-w-[340px] truncate font-medium">{item.title}</p>
                        {(subtitle || item.storageLocation) && (
                          <p className="text-muted-foreground flex max-w-[340px] items-center gap-2 truncate text-xs">
                            {subtitle && <span className="truncate">{subtitle}</span>}
                            {item.storageLocation && (
                              <span className="inline-flex shrink-0 items-center gap-0.5">
                                <MapPin className="h-3 w-3" aria-hidden /> {item.storageLocation}
                              </span>
                            )}
                          </p>
                        )}
                      </div>
                    </Link>
                  </td>
                  <td className="whitespace-nowrap px-4 py-3">
                    <Badge variant={badgeVariant(item.status)}>{inventoryStatusLabel(item.status)}</Badge>
                  </td>
                  <td className="hidden px-4 py-3 lg:table-cell">
                    <Channels facts={facts} />
                  </td>
                  <td className="text-muted-foreground hidden whitespace-nowrap px-4 py-3 md:table-cell">
                    {sold && facts?.soldAt ? (
                      <span className="text-xs">
                        Sold <When value={facts.soldAt} />
                      </span>
                    ) : days != null ? (
                      <span className={days > 90 ? "font-medium text-warning" : ""}>{days}d</span>
                    ) : (
                      "—"
                    )}
                  </td>
                  <td className="text-muted-foreground hidden whitespace-nowrap px-4 py-3 text-right tabular-nums md:table-cell">{formatCurrency(cost)}</td>
                  <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                    {sold ? (
                      facts?.orderId ? (
                        <Link href={`/orders/${facts.orderId}`} className="hover:underline">
                          {formatCurrency(facts.soldPrice)}
                        </Link>
                      ) : (
                        formatCurrency(facts?.soldPrice)
                      )
                    ) : (
                      formatCurrency(item.estimatedSalePrice)
                    )}
                    <span className="text-muted-foreground block text-[11px]">{sold ? `sold on ${facts?.soldOn}` : "expected"}</span>
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                    <span className={`font-medium ${profit != null && profit < 0 ? "text-destructive" : ""}`}>{formatCurrency(profit)}</span>
                    <span className="text-muted-foreground block text-[11px]">{sold ? "net" : "projected"}</span>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function Channels({ facts }: { facts?: Facts }) {
  const channels = facts?.channels ?? [];
  if (channels.length === 0) return <span className="text-muted-foreground text-xs">Not listed</span>;
  const shown = channels.slice(0, 3);
  return (
    <div className="flex max-w-[240px] flex-wrap gap-1">
      {shown.map((channel) => {
        const { text, tone } = presenceLabel(channel);
        const className = `inline-flex items-center rounded border px-1.5 py-0.5 text-[11px] font-medium ${
          tone === "warning" ? "border-warning-border bg-warning-surface text-warning" : tone === "muted" ? "text-muted-foreground" : ""
        }`;
        return channel.url ? (
          <a key={`${channel.platform}-${channel.status}`} href={channel.url} target="_blank" rel="noreferrer" className={`${className} hover:underline`}>
            {text}
          </a>
        ) : (
          <span key={`${channel.platform}-${channel.status}`} className={className}>{text}</span>
        );
      })}
      {channels.length > shown.length && <span className="text-muted-foreground text-[11px]">+{channels.length - shown.length}</span>}
    </div>
  );
}
