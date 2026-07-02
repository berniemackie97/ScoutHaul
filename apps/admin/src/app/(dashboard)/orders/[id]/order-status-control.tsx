"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@overhaul/ui";
import { updateOrderStatus, type OrderActionResult } from "./actions";

type OrderStatus = "Pending" | "Paid" | "Fulfilling" | "Shipped" | "Delivered" | "Cancelled" | "Refunded";
type Action = { label: string; status: OrderStatus; variant?: "default" | "outline" | "destructive" };

// Which transitions make sense from the current status. Corrections are still possible via the API;
// this surfaces the common forward path plus the relevant exception actions.
const NEXT_ACTIONS: Record<OrderStatus, Action[]> = {
  Pending: [
    { label: "Mark paid", status: "Paid" },
    { label: "Cancel", status: "Cancelled", variant: "destructive" },
  ],
  Paid: [
    { label: "Start fulfilling", status: "Fulfilling" },
    { label: "Mark shipped", status: "Shipped" },
    { label: "Refund", status: "Refunded", variant: "outline" },
  ],
  Fulfilling: [
    { label: "Mark shipped", status: "Shipped" },
    { label: "Refund", status: "Refunded", variant: "outline" },
  ],
  Shipped: [
    { label: "Mark delivered", status: "Delivered" },
    { label: "Refund", status: "Refunded", variant: "outline" },
  ],
  Delivered: [{ label: "Refund", status: "Refunded", variant: "outline" }],
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
    return <p className="text-muted-foreground text-sm">This order is in a terminal state ({status}).</p>;
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        {actions.map((action) => (
          <Button key={action.status} variant={action.variant ?? "default"} disabled={pending} onClick={() => run(action.status)}>
            {action.label}
          </Button>
        ))}
      </div>
      {result && (
        <p className={`text-sm ${result.ok ? "text-green-700" : "text-destructive"}`}>{result.message}</p>
      )}
    </div>
  );
}
