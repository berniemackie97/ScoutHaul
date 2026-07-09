"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Input } from "@overhaul/ui";
import { combineSales, ungroupSale, type CombineResult } from "./combine-actions";

type Sale = components["schemas"]["SaleDto"];

const initial: CombineResult = { ok: false, message: "" };
const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";
const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

const statusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Returned", "Refunded", "Cancelled"].includes(status ?? "")) return "destructive" as const;
  if (["ReturnRequested", "PartiallyRefunded"].includes(status ?? "")) return "warning" as const;
  if (["Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

// Stable accent per order group so a multi-item order is scannable at a glance.
const groupAccents = ["#6366f1", "#0ea5e9", "#10b981", "#f59e0b", "#ec4899", "#8b5cf6"];

function CombineButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" size="sm" disabled={pending}>
      {pending ? "Combining…" : "Combine into one order"}
    </Button>
  );
}

function UngroupButton() {
  const { pending } = useFormStatus();
  return (
    <button type="submit" disabled={pending} className="text-muted-foreground hover:text-destructive text-xs underline">
      {pending ? "…" : "ungroup"}
    </button>
  );
}

export function SalesTable({ sales }: { sales: Sale[] }) {
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [state, action] = useActionState(combineSales, initial);

  const groupColor = useMemo(() => {
    const ids = [...new Set(sales.map((sale) => sale.saleGroupId).filter((id): id is string => !!id))];
    return new Map(ids.map((id, index) => [id, groupAccents[index % groupAccents.length]!]));
  }, [sales]);

  const toggle = (id: string) =>
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const selectedIds = [...selected];

  return (
    <div className="space-y-3">
      {selected.size > 0 && (
        <form
          action={action}
          className="bg-muted/40 flex flex-wrap items-center gap-3 rounded-xl border p-3 text-sm"
        >
          <input type="hidden" name="saleIds" value={JSON.stringify(selectedIds)} />
          <span className="font-medium">{selected.size} selected</span>
          <Input
            name="orderReference"
            placeholder="eBay order number (optional)"
            className="h-9 max-w-xs"
          />
          <CombineButton />
          <button
            type="button"
            onClick={() => setSelected(new Set())}
            className="text-muted-foreground hover:text-foreground text-xs underline"
          >
            clear
          </button>
          {state.message && (
            <span className={state.ok ? "text-emerald-700" : "text-destructive"}>{state.message}</span>
          )}
          <span className="text-muted-foreground ml-auto text-xs">
            Combines several recorded sales into one order (shared reference), the way eBay groups a multi-item order.
          </span>
        </form>
      )}

      <div className="bg-card overflow-x-auto rounded-xl border">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
            <tr>
              <th className="w-10 px-3 py-3" />
              <th className="px-4 py-3 font-medium">Item</th>
              <th className="px-4 py-3 font-medium">Order</th>
              <th className="px-4 py-3 font-medium">Channel</th>
              <th className="px-4 py-3 font-medium">Sold</th>
              <th className="px-4 py-3 text-right font-medium">Item</th>
              <th className="px-4 py-3 text-right font-medium">Ship</th>
              <th className="px-4 py-3 text-right font-medium">Selling costs</th>
              <th className="px-4 py-3 text-right font-medium">Profit</th>
              <th className="px-4 py-3 font-medium">Status</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {sales.map((sale) => {
              const id = sale.id ?? "";
              const accent = sale.saleGroupId ? groupColor.get(sale.saleGroupId) : undefined;
              return (
                <tr key={id} className="hover:bg-muted/30 transition-colors" style={accent ? { boxShadow: `inset 3px 0 0 ${accent}` } : undefined}>
                  <td className="px-3 py-3">
                    <input
                      type="checkbox"
                      aria-label="Select sale"
                      checked={selected.has(id)}
                      onChange={() => toggle(id)}
                      className="border-input h-4 w-4 rounded"
                    />
                  </td>
                  <td className="p-0">
                    <Link href={`/finance/${id}`} className="text-foreground block px-4 py-3 font-medium">
                      {sale.inventoryItemTitle || "Item"}
                    </Link>
                  </td>
                  <td className="px-4 py-3">
                    {sale.saleGroupId ? (
                      <div className="flex items-center gap-2">
                        <Badge variant="outline" className="font-mono text-xs">
                          {sale.groupOrderReference || "order"}
                        </Badge>
                        <form action={ungroupSale.bind(null, id)}>
                          <UngroupButton />
                        </form>
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
                    {money((sale.platformFees ?? 0) + (sale.paymentFees ?? 0) + (sale.shippingCost ?? 0) + (sale.suppliesCost ?? 0) + (sale.otherCosts ?? 0))}
                  </td>
                  <td className={`px-4 py-3 text-right font-medium tabular-nums ${(sale.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}>
                    {money(sale.netProfit)}
                  </td>
                  <td className="px-4 py-3">
                    <Badge variant={statusVariant(sale.status)}>{sale.status}</Badge>
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
