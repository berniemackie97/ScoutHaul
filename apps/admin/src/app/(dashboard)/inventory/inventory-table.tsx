"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useMemo, useState, useTransition } from "react";
import { Archive, ArrowDown, ArrowUp, ChevronLeft, ChevronRight, ImageIcon, Trash2 } from "lucide-react";
import { Badge, Button, formatCurrency } from "@overhaul/ui";
import {
  getInventoryCost,
  getProjectedMetrics,
  inventoryStatusLabel,
  type InventoryItem,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { archiveInventoryItems, type BulkInventoryDeleteResult } from "./actions";

export type InventoryTableRow = InventoryItem & {
  thumbnailUrl: string | null;
};


const badgeVariant = (status: string | undefined) => {
  if (["Listed", "Delivered"].includes(status ?? "")) return "success" as const;
  if (["NeedsCleaning", "NeedsTesting", "NeedsRepair", "NeedsPhotos"].includes(status ?? "")) return "warning" as const;
  if (["Sold", "Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

const deleteBlockedStatuses = new Set<InventoryStatus>(["Bundled", "Listed", "Sold", "Packed", "Shipped", "Delivered"]);

function deleteBlockReason(item: InventoryTableRow) {
  if (item.status === "Listed") return "Delist and move out of Listed first.";
  if (item.status === "Bundled") return "Edit/remove it from the bundle first.";
  if (item.status && deleteBlockedStatuses.has(item.status)) return "Resolve the sale/fulfillment workflow first.";
  return null;
}

export function InventoryTable({
  items,
  page = 1,
  pageCount = 1,
  sort = "updatedAt",
  desc = true,
}: {
  items: InventoryTableRow[];
  page?: number;
  pageCount?: number;
  sort?: string;
  desc?: boolean;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [result, setResult] = useState<BulkInventoryDeleteResult | null>(null);
  const [pending, startTransition] = useTransition();

  const selectableItems = useMemo(() => items.filter((item) => Boolean(item.id)), [items]);
  const selectedItems = useMemo(
    () => selectableItems.filter((item) => item.id && selectedIds.has(item.id)),
    [selectableItems, selectedIds],
  );
  const blockedItems = selectedItems
    .map((item) => ({ item, reason: deleteBlockReason(item) }))
    .filter((entry): entry is { item: InventoryTableRow; reason: string } => Boolean(entry.reason));
  const allVisibleSelected = selectableItems.length > 0 && selectableItems.every((item) => item.id && selectedIds.has(item.id));

  function toggleAll() {
    setResult(null);
    setSelectedIds(allVisibleSelected ? new Set() : new Set(selectableItems.map((item) => item.id!).filter(Boolean)));
  }

  function toggleOne(id: string) {
    setResult(null);
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function navigate(updates: Record<string, string | null>) {
    const next = new URLSearchParams(searchParams.toString());
    for (const [key, value] of Object.entries(updates)) {
      if (value === null || value === "") next.delete(key);
      else next.set(key, value);
    }
    router.push(`${pathname}?${next.toString()}`);
  }

  function applySort(column: string) {
    const nextDesc = sort === column ? !desc : true;
    navigate({ sort: column, desc: nextDesc ? null : "false", page: null });
  }

  const SortHeader = ({ column, label, align = "left", className = "" }: { column: string; label: string; align?: "left" | "right"; className?: string }) => (
    <th className={`px-4 py-3 font-medium ${align === "right" ? "text-right" : ""} ${className}`}>
      <button onClick={() => applySort(column)} className={`inline-flex items-center gap-1 hover:text-foreground ${align === "right" ? "flex-row-reverse" : ""}`}>
        {label}
        {sort === column && (desc ? <ArrowDown className="h-3 w-3" /> : <ArrowUp className="h-3 w-3" />)}
      </button>
    </th>
  );

  function archiveSelected() {
    if (selectedItems.length === 0 || blockedItems.length > 0) return;
    const label = selectedItems.length === 1 ? selectedItems[0]?.title ?? "this item" : `${selectedItems.length} items`;
    if (!window.confirm(`Archive ${label}? Archived records are hidden from the active inventory list but remain available for history and audit reporting.`)) {
      return;
    }

    startTransition(async () => {
      const next = await archiveInventoryItems(selectedItems.map((item) => item.id!).filter(Boolean));
      setResult(next);
      if (next.ok) {
        setSelectedIds(new Set());
        router.refresh();
      }
    });
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-col gap-3 rounded-xl border bg-card p-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="text-sm">
          <span className="font-medium">{selectedItems.length} selected</span>
          {blockedItems.length > 0 && (
            <p className="text-destructive mt-1">
              {blockedItems.length} selected item{blockedItems.length === 1 ? "" : "s"} must be delisted, removed from a bundle, or resolved in fulfillment before deletion.
            </p>
          )}
          {result?.message && (
            <p className={`mt-1 ${result.ok ? "text-green-700" : "text-destructive"}`}>
              {result.message}
            </p>
          )}
        </div>
        <Button
          type="button"
          variant="destructive"
          disabled={pending || selectedItems.length === 0 || blockedItems.length > 0}
          onClick={archiveSelected}
          className="justify-center"
        >
          <Trash2 className="mr-2 h-4 w-4" aria-hidden />
          {pending ? "Deleting…" : "Delete selected"}
        </Button>
      </div>

      <div className="overflow-x-auto rounded-xl border bg-card">
        <table className="w-full text-sm">
          <thead className="border-b bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
            <tr>
              <th className="w-10 px-4 py-3 font-medium">
                <input
                  type="checkbox"
                  aria-label="Select all visible inventory"
                  checked={allVisibleSelected}
                  onChange={toggleAll}
                  className="h-4 w-4 rounded border-input"
                />
              </th>
              <SortHeader column="title" label="Item" />
              <th className="px-4 py-3 font-medium">Stage</th>
              <th className="hidden px-4 py-3 font-medium xl:table-cell">Location</th>
              <SortHeader column="purchasePrice" label="Cost" align="right" className="hidden md:table-cell" />
              <SortHeader column="estimatedSalePrice" label="Expected" align="right" className="hidden lg:table-cell" />
              <th className="px-4 py-3 text-right font-medium">Projected profit</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {items.map((item) => {
              const cost = getInventoryCost(item);
              const { profit: projectedProfit } = getProjectedMetrics(item);
              const subtitle = [
                item.brand,
                item.model,
                item.category,
                item.isBundle ? `${item.bundleComponentCount ?? 0} components` : null,
              ].filter(Boolean).join(" · ") || "Uncategorized";
              const checked = Boolean(item.id && selectedIds.has(item.id));
              const blockReason = deleteBlockReason(item);

              return (
                <tr key={item.id} className={`transition-colors hover:bg-muted/30 ${checked ? "bg-brand-50/40" : ""}`}>
                  <td className="px-4 py-3 align-middle">
                    <input
                      type="checkbox"
                      aria-label={`Select ${item.title}`}
                      checked={checked}
                      onChange={() => item.id && toggleOne(item.id)}
                      className="h-4 w-4 rounded border-input"
                    />
                  </td>
                  <td className="p-0">
                    <Link href={`/inventory/${item.id}`} className="flex min-w-0 items-center gap-3 px-4 py-3 sm:min-w-64">
                      <div className="flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-lg border bg-muted">
                        {item.thumbnailUrl ? (
                          // eslint-disable-next-line @next/next/no-img-element
                          <img src={item.thumbnailUrl} alt="" className="h-full w-full object-cover" />
                        ) : <ImageIcon className="h-5 w-5 text-muted-foreground" />}
                      </div>
                      <div className="min-w-0">
                        <p className="truncate font-medium text-foreground">{item.title}</p>
                        <p className="truncate text-xs text-muted-foreground">{subtitle}</p>
                        {blockReason && <p className="mt-0.5 flex items-center gap-1 text-xs text-muted-foreground"><Archive className="h-3 w-3" /> {blockReason}</p>}
                      </div>
                    </Link>
                  </td>
                  <td className="px-4 py-3"><Badge variant={badgeVariant(item.status)}>{inventoryStatusLabel(item.status)}</Badge></td>
                  <td className="hidden px-4 py-3 text-muted-foreground xl:table-cell">{item.storageLocation ?? "—"}</td>
                  <td className="hidden px-4 py-3 text-right tabular-nums md:table-cell">{formatCurrency(cost)}</td>
                  <td className="hidden px-4 py-3 text-right tabular-nums lg:table-cell">{formatCurrency(item.estimatedSalePrice)}</td>
                  <td className={`px-4 py-3 text-right font-medium tabular-nums ${projectedProfit != null && projectedProfit < 0 ? "text-destructive" : ""}`}>{formatCurrency(projectedProfit)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {pageCount > 1 && (
        <div className="flex items-center justify-between text-sm">
          <span className="text-muted-foreground">Page {page} of {pageCount}</span>
          <div className="flex gap-2">
            <button
              disabled={page <= 1}
              onClick={() => navigate({ page: String(page - 1) })}
              className="inline-flex h-8 items-center gap-1 rounded-lg border px-3 hover:bg-muted disabled:opacity-40"
            >
              <ChevronLeft className="h-4 w-4" /> Prev
            </button>
            <button
              disabled={page >= pageCount}
              onClick={() => navigate({ page: String(page + 1) })}
              className="inline-flex h-8 items-center gap-1 rounded-lg border px-3 hover:bg-muted disabled:opacity-40"
            >
              Next <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
