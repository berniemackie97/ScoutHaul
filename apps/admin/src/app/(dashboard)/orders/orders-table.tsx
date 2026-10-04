"use client";

import Link from "next/link";
import { useMemo, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { ExternalLink, GitMerge, PackageCheck, Truck, CheckCheck } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, formatCurrency } from "@overhaul/ui";
import { When } from "@/components/when";
import { paymentNote, stageLabel, stageVariant, trackingHref } from "@/features/orders/order-presentation";
import { advanceOrders, mergeOrders, type BulkStep, type MergeOrdersResult } from "./actions";

export type OrderRow = components["schemas"]["OrderSummaryDto"];

const BULK: Array<{ step: BulkStep; label: string; icon: typeof Truck }> = [
  { step: "markPacked", label: "Mark packed", icon: PackageCheck },
  { step: "markShipped", label: "Mark shipped", icon: Truck },
  { step: "markDelivered", label: "Mark delivered", icon: CheckCheck },
];

export function OrdersTable({ orders, emptyLabel }: { orders: OrderRow[]; emptyLabel: string }) {
  const router = useRouter();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [result, setResult] = useState<MergeOrdersResult | null>(null);
  const [pending, startTransition] = useTransition();

  const selectedOrders = useMemo(
    () => orders.filter((order) => order.id && selected.has(order.id)),
    [orders, selected],
  );
  // Merging across channels would fabricate an order that never existed on any marketplace.
  const sameChannel = new Set(selectedOrders.map((order) => order.channel ?? "Storefront")).size <= 1;
  const canMerge = selectedOrders.length >= 2 && sameChannel;
  const allSelected = orders.length > 0 && orders.every((order) => order.id && selected.has(order.id));

  function toggle(id: string) {
    setResult(null);
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAll() {
    setResult(null);
    setSelected(allSelected ? new Set() : new Set(orders.map((order) => order.id!).filter(Boolean)));
  }

  function finish(next: MergeOrdersResult) {
    setResult(next);
    if (next.ok) {
      setSelected(new Set());
      router.refresh();
    }
  }

  function merge() {
    if (!canMerge) return;
    const count = selectedOrders.length;
    if (!window.confirm(`Merge ${count} orders into one? Their lines and sales move onto the oldest order; the others are removed. This cannot be undone.`)) {
      return;
    }
    startTransition(async () => finish(await mergeOrders(selectedOrders.map((order) => order.id!))));
  }

  function advance(step: BulkStep) {
    const targets = selectedOrders.map((order) => ({ id: order.id!, orderNumber: order.orderNumber ?? order.id! }));
    startTransition(async () => finish(await advanceOrders(targets, step)));
  }

  if (orders.length === 0) {
    return (
      <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
        {emptyLabel}
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {(selected.size > 0 || result) && (
        <div className="bg-card sticky top-2 z-10 flex flex-col gap-3 rounded-xl border p-3 shadow-sm lg:flex-row lg:items-center lg:justify-between">
          <div className="min-w-0 text-sm">
            {selected.size > 0 && <span className="font-medium">{selectedOrders.length} selected</span>}
            {selectedOrders.length >= 2 && !sameChannel && (
              <p className="text-muted-foreground mt-1">Merging needs orders from one channel.</p>
            )}
            {result?.message && (
              <p className={`mt-1 ${result.ok ? "text-emerald-700" : "text-destructive"}`}>{result.message}</p>
            )}
          </div>
          {selected.size > 0 && (
            <div className="flex flex-wrap gap-2">
              {BULK.map(({ step, label, icon: Icon }) => (
                <Button key={step} type="button" size="sm" variant="outline" disabled={pending} onClick={() => advance(step)}>
                  <Icon className="mr-1.5 h-4 w-4" aria-hidden /> {label}
                </Button>
              ))}
              <Button type="button" size="sm" variant="outline" disabled={pending || !canMerge} onClick={merge}>
                <GitMerge className="mr-1.5 h-4 w-4" aria-hidden /> Merge
              </Button>
            </div>
          )}
        </div>
      )}

      <div className="bg-card overflow-x-auto rounded-xl border">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
            <tr>
              <th className="w-10 px-4 py-3">
                <input
                  type="checkbox"
                  aria-label="Select every order on this page"
                  checked={allSelected}
                  onChange={toggleAll}
                  className="border-input h-4 w-4 rounded"
                />
              </th>
              <th className="px-4 py-3 font-medium">Order</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium">Customer</th>
              <th className="px-4 py-3 font-medium">Placed</th>
              <th className="px-4 py-3 text-right font-medium">Total</th>
              <th className="px-4 py-3 font-medium">Tracking</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {orders.map((order) => {
              const payment = paymentNote(order.paymentStatus);
              const href = trackingHref(order.shippingCarrier, order.trackingNumber, order.trackingUrl);
              const extra = (order.itemCount ?? 0) - 1;
              return (
                <tr key={order.id} className={`hover:bg-muted/30 transition-colors ${order.id && selected.has(order.id) ? "bg-brand-500/5" : ""}`}>
                  <td className="px-4 py-3 align-top">
                    <input
                      type="checkbox"
                      aria-label={`Select order ${order.orderNumber}`}
                      checked={Boolean(order.id && selected.has(order.id))}
                      onChange={() => order.id && toggle(order.id)}
                      className="border-input mt-0.5 h-4 w-4 rounded"
                    />
                  </td>
                  <td className="max-w-[360px] p-0">
                    <Link href={`/orders/${order.id}`} className="block px-4 py-3">
                      <span className="block truncate font-medium">{order.leadItemTitle ?? order.orderNumber}</span>
                      <span className="text-muted-foreground mt-0.5 flex items-center gap-1.5 text-xs">
                        <span className="bg-muted rounded px-1.5 py-0.5 font-medium">{order.channel ?? "Storefront"}</span>
                        <span className="truncate font-mono">{order.orderNumber}</span>
                        {extra > 0 && <span className="shrink-0">· +{extra} more</span>}
                        {order.isLocalPickup && <span className="shrink-0">· pickup</span>}
                      </span>
                    </Link>
                  </td>
                  <td className="whitespace-nowrap px-4 py-3">
                    <div className="flex flex-wrap gap-1.5">
                      <Badge variant={stageVariant(order.stage)}>{stageLabel(order.stage ?? order.status)}</Badge>
                      {payment && <Badge variant={payment.variant}>{payment.label}</Badge>}
                    </div>
                  </td>
                  <td className="text-muted-foreground max-w-[180px] truncate px-4 py-3">
                    {order.customerName || order.buyerReference || order.email || "—"}
                  </td>
                  <td className="text-muted-foreground whitespace-nowrap px-4 py-3">
                    <When value={order.createdAt} />
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-right font-medium tabular-nums">{formatCurrency(order.grandTotal)}</td>
                  <td className="text-muted-foreground px-4 py-3">
                    {order.isLocalPickup ? (
                      "Pickup"
                    ) : order.trackingNumber ? (
                      <span className="block max-w-[170px]">
                        <span className="text-foreground text-xs font-medium">{order.shippingCarrier ?? "Carrier"}</span>
                        {href ? (
                          <a href={href} target="_blank" rel="noreferrer" className="hover:text-foreground flex items-center gap-1 truncate font-mono text-xs underline-offset-2 hover:underline">
                            <span className="truncate">{order.trackingNumber}</span>
                            <ExternalLink className="h-3 w-3 shrink-0" aria-hidden />
                          </a>
                        ) : (
                          <span className="block truncate font-mono text-xs">{order.trackingNumber}</span>
                        )}
                      </span>
                    ) : order.shippedAt ? (
                      <span className="text-xs">
                        Shipped <When value={order.shippedAt} />
                      </span>
                    ) : (
                      "—"
                    )}
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
