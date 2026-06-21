"use client";

import Link from "next/link";
import { useState, useTransition } from "react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import {
  COMMERCE_MANAGED_STATUSES,
  MANUAL_INVENTORY_STATUSES,
  TERMINAL_STATUSES,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { changeInventoryStatus } from "./actions";

export function StatusControl({ id, status }: { id: string; status: InventoryStatus }) {
  const [nextStatus, setNextStatus] = useState<InventoryStatus>(status);
  const [message, setMessage] = useState("");
  const [isPending, startTransition] = useTransition();
  const isCommerceManaged = COMMERCE_MANAGED_STATUSES.includes(status) && status !== "Returned";

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
            <p className="text-muted-foreground text-sm">
              This stage is controlled by its sale record so inventory, fulfillment, reports, and audit history stay synchronized.
            </p>
            <Button asChild className="w-full" variant="outline">
              <Link href="/finance">Open sales workflow</Link>
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
            <Button className="w-full" onClick={submit} disabled={isPending || nextStatus === status}>
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
