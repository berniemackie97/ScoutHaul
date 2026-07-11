"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@overhaul/ui";
import { updateOrderStatus, type OrderActionResult } from "./actions";

type OrderStatus =
  | "Pending"
  | "Paid"
  | "Fulfilling"
  | "Shipped"
  | "Delivered"
  | "Cancelled"
  | "Refunded";
type Action = {
  label: string;
  status: OrderStatus;
  variant?: "default" | "outline" | "destructive";
};

// Shipment movement belongs to the Package tracking form; refunds/returns are money events recorded per line.
// This control only covers payment and cancellation, so nothing is edited in two places. A "Refunded" order
// status is derived from real refund events on the lines — never flipped by hand here.
const NEXT_ACTIONS: Record<OrderStatus, Action[]> = {
  Pending: [
    { label: "Mark paid", status: "Paid" },
    { label: "Cancel", status: "Cancelled", variant: "destructive" },
  ],
  Paid: [{ label: "Cancel", status: "Cancelled", variant: "destructive" }],
  Fulfilling: [{ label: "Cancel", status: "Cancelled", variant: "destructive" }],
  Shipped: [],
  Delivered: [],
  Cancelled: [],
  Refunded: [],
};

export function OrderStatusControl({ id, status }: { id: string; status: OrderStatus }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [result, setResult] = useState<OrderActionResult | null>(null);
  const actions = NEXT_ACTIONS[status] ?? [];

  function run(next: OrderStatus) {
    startTransition(async () => {
      const outcome = await updateOrderStatus(id, next);
      setResult(outcome);
      if (outcome.ok) router.refresh();
    });
  }

  if (actions.length === 0) {
    return (
      <p className="text-muted-foreground text-sm">This order is in a terminal state ({status}).</p>
    );
  }

  return (
    <div className="space-y-3">
      {status !== "Pending" && (
        <p className="text-muted-foreground text-sm">
          Shipping and delivery are controlled by Package tracking below.
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {actions.map((action) => (
          <Button
            key={action.status}
            variant={action.variant ?? "default"}
            disabled={pending}
            onClick={() => run(action.status)}
          >
            {action.label}
          </Button>
        ))}
      </div>
      {result && (
        <p className={`text-sm ${result.ok ? "text-green-700" : "text-destructive"}`}>
          {result.message}
        </p>
      )}
    </div>
  );
}
