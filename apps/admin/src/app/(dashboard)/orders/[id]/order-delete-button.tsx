"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Trash2 } from "lucide-react";
import { Button } from "@overhaul/ui";
import { deleteOrder, type OrderActionResult } from "./actions";

// Broken/phantom orders (a stray import with no lines, wrong totals, or an item id mistaken for an order) can be
// cleared out here. Sales are never destroyed — a linked sale keeps its financials and is detached to be re-grouped.
export function OrderDeleteButton({ id, orderNumber }: { id: string; orderNumber: string }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [result, setResult] = useState<OrderActionResult | null>(null);

  function onDelete() {
    const confirmed = window.confirm(
      `Delete order ${orderNumber}?\n\n` +
        "Its line items are removed. Any linked sale keeps its financial record and is detached so you can " +
        "re-group it later. This cannot be undone.",
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
    <div className="flex flex-col items-start gap-1 sm:items-end">
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={pending}
        onClick={onDelete}
        className="border-destructive/30 text-destructive hover:bg-destructive/10"
      >
        <Trash2 className="mr-2 h-4 w-4" aria-hidden />
        {pending ? "Deleting…" : "Delete order"}
      </Button>
      {result && !result.ok && <p className="text-destructive text-xs">{result.message}</p>}
    </div>
  );
}
