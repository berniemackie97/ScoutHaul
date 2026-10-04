"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Ban, MoreHorizontal, Trash2 } from "lucide-react";
import {
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@overhaul/ui";
import { deleteOrder, updateOrderStatus, type OrderActionResult } from "./actions";

/** The rare, destructive things — kept out of the way of the day's work. */
export function OrderActionsMenu({ id, orderNumber, canCancel }: { id: string; orderNumber: string; canCancel: boolean }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [result, setResult] = useState<OrderActionResult | null>(null);

  function cancel() {
    if (!window.confirm(`Cancel order ${orderNumber}?\n\nIts sales are reversed and the items go back into stock.`)) return;
    startTransition(async () => setResult(await updateOrderStatus(id, "Cancelled")));
  }

  function remove() {
    const confirmed = window.confirm(
      `Delete order ${orderNumber}?\n\nUse this only for a broken or duplicate import. Linked sales keep their ` +
        "financial record and are detached. This cannot be undone.",
    );
    if (!confirmed) return;
    startTransition(async () => {
      const next = await deleteOrder(id);
      setResult(next);
      if (next.ok) {
        router.push("/orders");
        router.refresh();
      }
    });
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" disabled={pending} aria-label="More order actions">
            <MoreHorizontal className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          {canCancel && (
            <DropdownMenuItem onSelect={cancel}>
              <Ban className="mr-2 h-4 w-4" aria-hidden /> Cancel order
            </DropdownMenuItem>
          )}
          {canCancel && <DropdownMenuSeparator />}
          <DropdownMenuItem onSelect={remove} className="text-destructive focus:text-destructive">
            <Trash2 className="mr-2 h-4 w-4" aria-hidden /> Delete order
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      {result && !result.ok && <p className="text-destructive max-w-xs text-right text-xs">{result.message}</p>}
      {result?.ok && <p className="text-xs text-emerald-700">{result.message}</p>}
    </div>
  );
}
