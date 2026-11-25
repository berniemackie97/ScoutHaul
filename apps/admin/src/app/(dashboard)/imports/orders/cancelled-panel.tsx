"use client";

import { useActionState, useState } from "react";
import { AlertTriangle } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, formatCurrency, formatDate } from "@overhaul/ui";
import { resolveCancellation, type ActionResult } from "./actions";

type PlatformOrder = components["schemas"]["PlatformOrderDto"];

const idle: ActionResult = { ok: true, message: "" };

/**
 * Orders a marketplace cancelled after they had already been recorded as sales. Undoing a sale moves stock and
 * money, so sync never does it on its own — this is where that gets answered.
 */
export function CancelledAfterSalePanel({ orders }: { orders: PlatformOrder[] }) {
  if (orders.length === 0) {
    return null;
  }

  return (
    <section className="space-y-3" aria-labelledby="cancelled-heading">
      <h2 id="cancelled-heading" className="text-sm font-semibold">
        Cancelled after they were recorded as sales
      </h2>
      <p className="text-muted-foreground text-sm">
        The marketplace reported a cancellation or full refund on these sales. Reverse a sale that never settled when
        its items are back in stock. If you received a payout, record the refund against the original sale before
        closing the review.
      </p>
      <div className="grid gap-3">
        {orders.map((order) => (
          <CancelledOrder key={order.id} order={order} />
        ))}
      </div>
    </section>
  );
}

function CancelledOrder({ order }: { order: PlatformOrder }) {
  const [state, submit, pending] = useActionState(resolveCancellation, idle);
  const [note, setNote] = useState("");

  return (
    <Card className="space-y-3 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-2">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning" aria-hidden />
          <div className="text-sm">
            <p className="font-medium">
              {order.platform} order {order.externalOrderId}
            </p>
            <p className="text-muted-foreground text-xs">
              {order.buyerName ?? order.buyerReference ?? "Buyer unknown"} · ordered {formatDate(order.orderedAt)} ·{" "}
              {formatCurrency(order.itemsSubtotal)}
            </p>
            <p className="text-muted-foreground mt-1 text-xs">
              Reported by {order.platform} {order.cancelledOnPlatformAt ? formatDate(order.cancelledOnPlatformAt) : ""}
            </p>
          </div>
        </div>
        <Badge variant="warning">Needs a decision</Badge>
      </div>

      <form action={submit} className="flex flex-wrap items-end gap-2">
        <input type="hidden" name="orderId" value={order.id} />
        <label className="grow text-xs">
          <span className="text-muted-foreground">Reason (required to keep the sale)</span>
          <input
            name="note"
            value={note}
            onChange={(event) => setNote(event.target.value)}
            onKeyDown={(event) => {
              // Enter in a text field submits through the first button, which here undoes the sale. Undoing a sale
              // is not something anyone should do by pressing Enter while typing their reason for keeping it.
              if (event.key === "Enter") event.preventDefault();
            }}
            className="mt-1 h-9 w-full rounded-md border px-2 text-sm"
            placeholder="e.g. buyer paid by bank transfer instead"
          />
        </label>
        <Button
          type="submit"
          name="outcome"
          value="Reversed"
          variant="destructive"
          size="sm"
          disabled={pending}
          onClick={(event) => {
            if (
              !window.confirm(
                `Reverse the sale for ${order.platform} order ${order.externalOrderId}?\n\n`
                  + "Its item goes back into stock and the money comes off the books.",
              )
            ) {
              event.preventDefault();
            }
          }}
        >
          Reverse the sale
        </Button>
        <Button type="submit" name="outcome" value="Kept" variant="outline" size="sm" disabled={pending || note.trim().length === 0}>
          Keep the sale
        </Button>
      </form>

      {state.message ? (
        <p className={`text-xs ${state.ok ? "text-success" : "text-warning"}`} aria-live="polite">
          {state.message}
        </p>
      ) : null}
    </Card>
  );
}
