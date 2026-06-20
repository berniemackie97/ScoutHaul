"use client";

import { useState, useTransition } from "react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import {
  COMMERCE_MANAGED_STATUSES,
  MANUAL_INVENTORY_STATUSES,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { changeInventoryStatus } from "./actions";

export function StatusControl({ id, status }: { id: string; status: InventoryStatus }) {
  const [nextStatus, setNextStatus] = useState<InventoryStatus>(status);
  const [message, setMessage] = useState("");
  const [isPending, startTransition] = useTransition();
  const commerceManaged = COMMERCE_MANAGED_STATUSES.includes(status);

  function submit() {
    if (nextStatus === status) return;
    if (["Donated", "Disposed"].includes(nextStatus)) {
      const confirmed = window.confirm(`Move this item to ${nextStatus}? This removes it from active inventory.`);
      if (!confirmed) return;
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
          {commerceManaged
            ? "This stage is controlled by the sale and fulfillment workflow so inventory and financial records stay synchronized."
            : "Move the item through intake, preparation, and listing work."}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <select
          className="h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
          value={nextStatus}
          disabled={commerceManaged || isPending}
          onChange={(event) => setNextStatus(event.target.value as InventoryStatus)}
        >
          {commerceManaged && <option value={status}>{status}</option>}
          {MANUAL_INVENTORY_STATUSES.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select>
        <Button className="w-full" onClick={submit} disabled={commerceManaged || isPending || nextStatus === status}>
          {isPending ? "Updating…" : "Update stage"}
        </Button>
        {message && <p className="text-sm text-muted-foreground" aria-live="polite">{message}</p>}
      </CardContent>
    </Card>
  );
}
