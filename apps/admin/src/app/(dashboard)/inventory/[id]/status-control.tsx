"use client";

import { useState, useTransition } from "react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import {
  INVENTORY_STATUS_GROUPS,
  TERMINAL_STATUSES,
  isSoldStatus,
  statusRank,
  type InventoryStatus,
} from "@/features/inventory/inventory-operations";
import { changeInventoryStatus } from "./actions";

export function StatusControl({ id, status }: { id: string; status: InventoryStatus }) {
  const [nextStatus, setNextStatus] = useState<InventoryStatus>(status);
  const [message, setMessage] = useState("");
  const [isPending, startTransition] = useTransition();

  function submit() {
    if (nextStatus === status) return;

    if (TERMINAL_STATUSES.includes(nextStatus)) {
      if (!window.confirm(`Move this item to ${nextStatus}? This removes it from active inventory.`)) return;
    } else if (isSoldStatus(status) && statusRank(nextStatus) < statusRank(status)) {
      if (!window.confirm(`Move this item back from ${status} to ${nextStatus}? Do this only to correct a mistake — it can desync the sale record.`)) return;
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
          Move the item through its lifecycle. Sale &amp; fulfillment stages normally advance from the sale,
          but you can set any stage here to correct a record.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <select
          className="h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
          value={nextStatus}
          disabled={isPending}
          onChange={(event) => setNextStatus(event.target.value as InventoryStatus)}
        >
          {INVENTORY_STATUS_GROUPS.map((group) => (
            <optgroup key={group.group} label={group.group}>
              {group.options.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </optgroup>
          ))}
        </select>
        <Button className="w-full" onClick={submit} disabled={isPending || nextStatus === status}>
          {isPending ? "Updating…" : "Update stage"}
        </Button>
        {message && <p className="text-sm text-muted-foreground" aria-live="polite">{message}</p>}
      </CardContent>
    </Card>
  );
}
