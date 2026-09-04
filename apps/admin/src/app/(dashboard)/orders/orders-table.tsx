"use client";

import Link from "next/link";
import { useMemo, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { GitMerge } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, formatCurrency, formatDate } from "@overhaul/ui";
import { mergeOrders, type MergeOrdersResult } from "./actions";

export type OrderRow = components["schemas"]["OrderSummaryDto"] & {
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
};


// The stage the API derived, said the way an operator would say it.
const stageLabels: Record<string, string> = {
  AwaitingPayment: "Awaiting payment",
  ToPack: "To pack",
  ToShip: "To ship",
  ReadyForPickup: "Ready for pickup",
  InTransit: "In transit",
  Delivered: "Delivered",
  NeedsAttention: "Needs attention",
  Closed: "Closed",
};
const stageVariant = (stage: string | undefined) => {
  if (stage === "NeedsAttention") return "destructive" as const;
  if (stage === "Delivered" || stage === "Closed") return "success" as const;
  if (stage === "AwaitingPayment") return "warning" as const;
  if (stage) return "info" as const;
  return "secondary" as const;
};

const orderStatusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Cancelled", "Refunded"].includes(status ?? "")) return "destructive" as const;
  if (status === "Pending") return "warning" as const;
  if (["Paid", "Fulfilling", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

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
  const channels = new Set(selectedOrders.map((order) => order.channel ?? "Storefront"));
  const sameChannel = channels.size <= 1;
  const canMerge = selectedOrders.length >= 2 && sameChannel;

  function toggle(id: string) {
    setResult(null);
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function merge() {
    if (!canMerge) return;
    const count = selectedOrders.length;
    if (!window.confirm(`Merge ${count} orders into one? Their lines and sales move onto the oldest order; the others are removed. This cannot be undone.`)) {
      return;
    }
    startTransition(async () => {
      const next = await mergeOrders(selectedOrders.map((order) => order.id!).filter(Boolean));
      setResult(next);
      if (next.ok) {
        setSelected(new Set());
        router.refresh();
      }
    });
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
      {selected.size > 0 && (
        <div className="bg-card flex flex-col gap-3 rounded-xl border p-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="text-sm">
            <span className="font-medium">{selectedOrders.length} selected</span>
            {selectedOrders.length >= 2 && !sameChannel && (
              <p className="text-destructive mt-1">Orders on different channels can’t be merged — select one channel.</p>
            )}
            {result?.message && (
              <p className={`mt-1 ${result.ok ? "text-green-700" : "text-destructive"}`}>{result.message}</p>
            )}
          </div>
          <Button type="button" disabled={pending || !canMerge} onClick={merge} className="justify-center">
            <GitMerge className="mr-2 h-4 w-4" aria-hidden />
            {pending ? "Merging…" : "Merge into one order"}
          </Button>
        </div>
      )}

      <div className="bg-card overflow-x-auto rounded-xl border">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
            <tr>
              <th className="w-10 px-4 py-3 font-medium" />
              <th className="px-4 py-3 font-medium">Order</th>
              <th className="px-4 py-3 font-medium">Channel</th>
              <th className="px-4 py-3 font-medium">Customer</th>
              <th className="px-4 py-3 font-medium">Placed</th>
              <th className="px-4 py-3 text-right font-medium">Items</th>
              <th className="px-4 py-3 text-right font-medium">Total</th>
              <th className="px-4 py-3 font-medium">Tracking</th>
              <th className="px-4 py-3 font-medium">Payment</th>
              <th className="px-4 py-3 font-medium">Stage</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {orders.map((order) => (
              <tr key={order.id} className="hover:bg-muted/30 transition-colors">
                <td className="px-4 py-3">
                  <input
                    type="checkbox"
                    aria-label={`Select order ${order.orderNumber}`}
                    checked={Boolean(order.id && selected.has(order.id))}
                    onChange={() => order.id && toggle(order.id)}
                    className="border-input h-4 w-4 rounded"
                  />
                </td>
                <td className="p-0">
                  <Link href={`/orders/${order.id}`} className="text-foreground block px-4 py-3 font-medium">
                    {order.orderNumber}
                    {order.isLocalPickup && <span className="text-muted-foreground ml-2 text-xs">· pickup</span>}
                  </Link>
                </td>
                <td className="px-4 py-3">
                  <span className="bg-muted rounded-md px-2 py-0.5 text-xs font-medium">
                    {order.channel ?? "Storefront"}
                  </span>
                </td>
                {/* Marketplace buyers have a username, not an email. */}
                <td className="text-muted-foreground px-4 py-3">
                  {order.customerName || order.buyerReference || order.email || "—"}
                </td>
                <td className="text-muted-foreground px-4 py-3">{formatDate(order.createdAt)}</td>
                <td className="px-4 py-3 text-right tabular-nums">{order.itemCount}</td>
                <td className="px-4 py-3 text-right font-medium tabular-nums">{formatCurrency(order.grandTotal)}</td>
                <td className="text-muted-foreground px-4 py-3">
                  {order.isLocalPickup ? (
                    "Pickup"
                  ) : order.trackingNumber ? (
                    <span>
                      <span className="text-foreground font-medium">{order.shippingCarrier ?? "Carrier"}</span>
                      <span className="block max-w-[180px] truncate text-xs">{order.trackingNumber}</span>
                    </span>
                  ) : order.shippedAt ? (
                    <span>Shipped {formatDate(order.shippedAt)}</span>
                  ) : (
                    "—"
                  )}
                </td>
                <td className="px-4 py-3">
                  <Badge
                    variant={
                      order.paymentStatus === "Paid"
                        ? "success"
                        : order.paymentStatus === "Refunded"
                          ? "destructive"
                          : "secondary"
                    }
                  >
                    {order.paymentStatus}
                  </Badge>
                </td>
                <td className="px-4 py-3">
                  {order.stage ? (
                    <Badge variant={stageVariant(order.stage)}>
                      {stageLabels[order.stage] ?? order.stage}
                    </Badge>
                  ) : (
                    <Badge variant={orderStatusVariant(order.status)}>{order.status}</Badge>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
