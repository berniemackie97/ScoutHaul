"use client";

import { useMemo, useState, useTransition } from "react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import { ArrowDown, ArrowUp, ChevronLeft, ChevronRight, Search, Trash2 } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Input } from "@overhaul/ui";
import { combineSales, deleteSale, ungroupSale, type CombineResult } from "./combine-actions";

type Sale = components["schemas"]["SaleDto"];
type Query = { search: string; status: string; sort: string; desc: boolean };

const initial: CombineResult = { ok: false, message: "" };
const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";
const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

const STATUSES = ["Recorded", "Packed", "Shipped", "Delivered", "ReturnRequested", "PartiallyRefunded", "Returned", "Refunded", "Cancelled"];
const statusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Returned", "Refunded", "Cancelled"].includes(status ?? "")) return "destructive" as const;
  if (["ReturnRequested", "PartiallyRefunded"].includes(status ?? "")) return "warning" as const;
  if (["Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};
const groupAccents = ["#6366f1", "#0ea5e9", "#10b981", "#f59e0b", "#ec4899", "#8b5cf6"];

function CombineButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" size="sm" disabled={pending}>{pending ? "Combining…" : "Combine into one order"}</Button>;
}
function UngroupButton() {
  const { pending } = useFormStatus();
  return <button type="submit" disabled={pending} className="text-muted-foreground hover:text-destructive text-xs underline">{pending ? "…" : "ungroup"}</button>;
}

export function SalesTable({
  sales,
  total,
  page,
  pageCount,
  query,
}: {
  sales: Sale[];
  total: number;
  page: number;
  pageCount: number;
  query: Query;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [isPending, startTransition] = useTransition();

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [searchDraft, setSearchDraft] = useState(query.search);
  const [combineState, combineAction] = useActionState(combineSales, initial);

  const groupColor = useMemo(() => {
    const ids = [...new Set(sales.map((sale) => sale.saleGroupId).filter((id): id is string => !!id))];
    return new Map(ids.map((id, index) => [id, groupAccents[index % groupAccents.length]!]));
  }, [sales]);

  const navigate = (updates: Record<string, string | null>) => {
    const next = new URLSearchParams(searchParams.toString());
    for (const [key, value] of Object.entries(updates)) {
      if (value === null || value === "") next.delete(key);
      else next.set(key, value);
    }
    startTransition(() => router.push(`${pathname}?${next.toString()}`));
  };

  const applySort = (column: string) => {
    const nextDesc = query.sort === column ? !query.desc : true;
    navigate({ sort: column, desc: nextDesc ? null : "false", page: null });
  };

  const toggle = (id: string) =>
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const SortHeader = ({ column, label, align = "left" }: { column: string; label: string; align?: "left" | "right" }) => (
    <th className={`px-4 py-3 font-medium ${align === "right" ? "text-right" : ""}`}>
      <button onClick={() => applySort(column)} className={`hover:text-foreground inline-flex items-center gap-1 ${align === "right" ? "flex-row-reverse" : ""}`}>
        {label}
        {query.sort === column && (query.desc ? <ArrowDown className="h-3 w-3" /> : <ArrowUp className="h-3 w-3" />)}
      </button>
    </th>
  );

  return (
    <div className={`space-y-3 ${isPending ? "opacity-60" : ""}`}>
      {/* Filters */}
      <div className="flex flex-wrap items-center gap-2">
        <form
          onSubmit={(event) => {
            event.preventDefault();
            navigate({ search: searchDraft || null, page: null });
          }}
          className="relative flex-1 sm:max-w-xs"
        >
          <Search className="text-muted-foreground pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2" />
          <Input
            value={searchDraft}
            onChange={(event) => setSearchDraft(event.target.value)}
            placeholder="Search item, channel, order #"
            className="h-9 pl-9"
          />
        </form>
        <select
          value={query.status}
          onChange={(event) => navigate({ status: event.target.value || null, page: null })}
          className="border-input bg-background h-9 rounded-lg border px-2 text-sm"
        >
          <option value="">All statuses</option>
          {STATUSES.map((status) => (
            <option key={status} value={status}>{status}</option>
          ))}
        </select>
        {(query.search || query.status) && (
          <button onClick={() => navigate({ search: null, status: null, page: null })} className="text-muted-foreground hover:text-foreground text-xs underline">
            clear filters
          </button>
        )}
        <span className="text-muted-foreground ml-auto text-xs">{total} sales</span>
      </div>

      {/* Combine toolbar */}
      {selected.size > 0 && (
        <form action={combineAction} className="bg-muted/40 flex flex-wrap items-center gap-3 rounded-xl border p-3 text-sm">
          <input type="hidden" name="saleIds" value={JSON.stringify([...selected])} />
          <span className="font-medium">{selected.size} selected</span>
          <Input name="orderReference" placeholder="eBay order number (optional)" className="h-9 max-w-xs" />
          <CombineButton />
          <button type="button" onClick={() => setSelected(new Set())} className="text-muted-foreground hover:text-foreground text-xs underline">clear</button>
          {combineState.message && <span className={combineState.ok ? "text-emerald-700" : "text-destructive"}>{combineState.message}</span>}
        </form>
      )}

      <div className="bg-card overflow-x-auto rounded-xl border">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
            <tr>
              <th className="w-10 px-3 py-3" />
              <SortHeader column="item" label="Item" />
              <th className="px-4 py-3 font-medium">Order</th>
              <th className="px-4 py-3 font-medium">Channel</th>
              <SortHeader column="soldAt" label="Sold" />
              <SortHeader column="soldPrice" label="Item" align="right" />
              <th className="px-4 py-3 text-right font-medium">Ship</th>
              <th className="px-4 py-3 text-right font-medium">Refunded</th>
              <SortHeader column="netProfit" label="Profit" align="right" />
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="w-10 px-3 py-3" />
            </tr>
          </thead>
          <tbody className="divide-y">
            {sales.length === 0 ? (
              <tr>
                <td colSpan={11} className="text-muted-foreground px-4 py-10 text-center">No sales match these filters.</td>
              </tr>
            ) : (
              sales.map((sale) => {
                const id = sale.id ?? "";
                const accent = sale.saleGroupId ? groupColor.get(sale.saleGroupId) : undefined;
                return (
                  <tr key={id} className="hover:bg-muted/30 transition-colors" style={accent ? { boxShadow: `inset 3px 0 0 ${accent}` } : undefined}>
                    <td className="px-3 py-3">
                      <input type="checkbox" aria-label="Select sale" checked={selected.has(id)} onChange={() => toggle(id)} className="border-input h-4 w-4 rounded" />
                    </td>
                    <td className="p-0">
                      <Link href={`/finance/${id}`} className="text-foreground block px-4 py-3 font-medium">{sale.inventoryItemTitle || "Item"}</Link>
                    </td>
                    <td className="px-4 py-3">
                      {sale.saleGroupId ? (
                        <div className="flex items-center gap-2">
                          <Badge variant="outline" className="font-mono text-xs">{sale.groupOrderReference || "order"}</Badge>
                          <form action={ungroupSale.bind(null, id)}><UngroupButton /></form>
                        </div>
                      ) : (
                        <span className="text-muted-foreground text-xs">—</span>
                      )}
                    </td>
                    <td className="text-muted-foreground px-4 py-3">{sale.platform}</td>
                    <td className="text-muted-foreground px-4 py-3">{date(sale.soldAt)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{money(sale.soldPrice)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{money(sale.shippingCharged)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">
                      {(sale.totalRefunded ?? 0) > 0 ? <span className="text-amber-700">-{money(sale.totalRefunded)}</span> : <span className="text-muted-foreground text-xs">—</span>}
                    </td>
                    <td className={`px-4 py-3 text-right font-medium tabular-nums ${(sale.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}>{money(sale.netProfit)}</td>
                    <td className="px-4 py-3"><Badge variant={statusVariant(sale.status)}>{sale.status}</Badge></td>
                    <td className="px-3 py-3">
                      <DeleteSaleButton id={id} title={sale.inventoryItemTitle || "this sale"} />
                    </td>
                  </tr>
                );
              })
            )}
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
              className="hover:bg-muted inline-flex h-8 items-center gap-1 rounded-lg border px-3 disabled:opacity-40"
            >
              <ChevronLeft className="h-4 w-4" /> Prev
            </button>
            <button
              disabled={page >= pageCount}
              onClick={() => navigate({ page: String(page + 1) })}
              className="hover:bg-muted inline-flex h-8 items-center gap-1 rounded-lg border px-3 disabled:opacity-40"
            >
              Next <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function DeleteSaleButton({ id, title }: { id: string; title: string }) {
  const [pending, startTransition] = useTransition();
  return (
    <button
      type="button"
      disabled={pending}
      onClick={() => {
        if (!window.confirm(`Delete the sale for "${title}"? This releases the item back to inventory and cannot be undone.`)) return;
        startTransition(() => deleteSale(id));
      }}
      aria-label="Delete sale"
      className="text-muted-foreground hover:text-destructive disabled:opacity-40"
    >
      <Trash2 className="h-4 w-4" />
    </button>
  );
}
