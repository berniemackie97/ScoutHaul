"use client";

import Link from "next/link";
import { useState, useTransition } from "react";
import { ArrowRight } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, formatCurrency } from "@overhaul/ui";
import { When } from "@/components/when";
import {
  COMMERCE_MANAGED_STATUSES,
  MANUAL_INVENTORY_STATUSES,
  TERMINAL_STATUSES,
  type InventoryItem,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { factsFromInventoryItem, readinessBlockers } from "@/features/inventory/lifecycle-readiness";
import { ReadinessChecklist } from "@/features/inventory/readiness-checklist";
import { changeInventoryStatus } from "./actions";

type Sale = components["schemas"]["SaleDto"];

export function StatusControl({ item, sale }: { item: InventoryItem; sale?: Sale }) {
  const id = item.id!;
  const status = item.status!;
  const [nextStatus, setNextStatus] = useState<InventoryStatus>(status);
  const [message, setMessage] = useState("");
  const [isPending, startTransition] = useTransition();
  const isCommerceManaged = COMMERCE_MANAGED_STATUSES.includes(status) && status !== "Returned";
  const readinessFacts = factsFromInventoryItem(item);
  const blockers = readinessBlockers(nextStatus, readinessFacts);
  const blockedReadyToList = nextStatus === "ReadyToList" && blockers.length > 0;

  function submit() {
    if (nextStatus === status) return;

    if (TERMINAL_STATUSES.includes(nextStatus)) {
      if (!window.confirm(`Move this item to ${nextStatus}? This removes it from active inventory.`)) return;
    }

    startTransition(async () => {
      const result = await changeInventoryStatus(id, nextStatus);
      setMessage(result.message);
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Lifecycle stage</CardTitle>
        <CardDescription>
          Manage inventory preparation and disposition. Sale, fulfillment, and return stages are controlled by the sale record.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {isCommerceManaged ? (
          <>
            {sale ? (
              <div className="bg-muted/30 space-y-1 rounded-lg border p-3 text-sm">
                <p className="font-medium">
                  Sold on {sale.platform} · <When value={sale.soldAt} />
                </p>
                <p className="text-muted-foreground tabular-nums">
                  {formatCurrency(sale.soldPrice)} · net {formatCurrency(sale.netProfit)}
                </p>
              </div>
            ) : (
              <p className="text-muted-foreground text-sm">
                Its sale controls this stage, so inventory, fulfillment and reports stay in step.
              </p>
            )}
            <Button asChild className="w-full" variant="outline">
              <Link href={orderHref(item, sale)}>
                {sale?.orderId ? `Open order ${sale.groupOrderReference ?? ""}`.trim() : "Find its order"}
                <ArrowRight className="ml-2 h-4 w-4" aria-hidden />
              </Link>
            </Button>
          </>
        ) : (
          <>
            <select
              className="border-input bg-background focus:ring-ring h-10 w-full rounded-lg border px-3 text-sm focus:outline-none focus:ring-2"
              value={nextStatus}
              disabled={isPending}
              onChange={(event) => setNextStatus(event.target.value as InventoryStatus)}
            >
              {status === "Returned" && <option value="Returned">Returned</option>}
              {MANUAL_INVENTORY_STATUSES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <ReadinessChecklist targetStatus={nextStatus} facts={readinessFacts} compact />
            {blockedReadyToList && (
              <p className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-800">
                Resolve the blocker{blockers.length === 1 ? "" : "s"} before moving this item to Ready to list.
              </p>
            )}
            <Button className="w-full" onClick={submit} disabled={isPending || nextStatus === status || blockedReadyToList}>
              {isPending ? "Updating…" : "Update stage"}
            </Button>
            {message && (
              <p className="text-muted-foreground text-sm" aria-live="polite">
                {message}
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

/** The item's own order when the sale knows it; otherwise the order list searched for the item. */
function orderHref(item: InventoryItem, sale: Sale | undefined) {
  if (sale?.orderId) return `/orders/${sale.orderId}`;
  return `/orders?q=${encodeURIComponent(item.title ?? "")}`;
}
