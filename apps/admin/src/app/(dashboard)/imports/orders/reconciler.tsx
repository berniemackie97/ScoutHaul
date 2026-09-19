"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, CardContent, CardHeader, CardTitle, Textarea, formatCurrency, formatDate } from "@overhaul/ui";
import { InventoryPicker } from "@/components/inventory/inventory-picker";
import { confirmLabel, initialChoice, mappingRows, summarize, type LineChoice } from "@/features/imports/reconcile-model";
import { reconcilePlatformOrder, ignorePlatformOrder, type ActionResult } from "./actions";

type PlatformOrder = components["schemas"]["PlatformOrderDto"];
type PlatformOrderLine = components["schemas"]["PlatformOrderLineDto"];

const initial: ActionResult = { ok: false, message: "" };
const currency = formatCurrency;

function ReconcileButton({ disabled, label }: { disabled: boolean; label: string }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending || disabled}>
      {pending ? "Saving…" : `Confirm (${label})`}
    </Button>
  );
}

function IgnoreButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" variant="ghost" disabled={pending}>
      {pending ? "…" : "Ignore"}
    </Button>
  );
}

function OrderCard({ order }: { order: PlatformOrder }) {
  const [reconcileState, reconcileAction] = useActionState(reconcilePlatformOrder, initial);
  const [ignoreState, ignoreAction] = useActionState(ignorePlatformOrder, initial);
  // `?? []` produced a fresh array on every render, so the useMemo keyed on `lines` below
  // recomputed every time and memoized nothing.
  const lines = useMemo(() => order.lines ?? [], [order.lines]);

  const [choices, setChoices] = useState<Record<string, LineChoice | null>>(() =>
    Object.fromEntries(lines.map((line) => [line.id ?? "", initialChoice(line)]))
  );

  const summary = summarize(lines, choices);
  // Fees not posted yet: settling now would write a sale with a profit that is not true.
  const feesPending = order.feesPending === true;
  const canReconcile = summary.complete && summary.distinct && !feesPending;
  const mappingJson = useMemo(() => JSON.stringify(mappingRows(lines, choices)), [lines, choices]);

  const hasPayout = typeof order.payoutAmount === "number";
  const variance = order.payoutVariance ?? 0;
  const varianceOff = hasPayout && Math.abs(variance) >= 0.01;

  return (
    <Card>
      <CardHeader className="gap-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="text-base">
              {order.platform} · order {order.externalOrderId}
            </CardTitle>
            <p className="text-muted-foreground mt-1 text-xs">
              {formatDate(order.orderedAt)}
              {order.buyerName ? ` · ${order.buyerName}` : ""}
              {order.trackingNumber
                ? ` · ${order.carrier ?? "tracking"} ${order.trackingNumber}`
                : ""}
            </p>
          </div>
          <form action={ignoreAction}>
            <input type="hidden" name="orderId" value={order.id ?? ""} />
            <IgnoreButton />
          </form>
        </div>
        <div className="flex flex-wrap gap-x-5 gap-y-1 text-xs tabular-nums">
          <span className="text-muted-foreground">
            Items <span className="text-foreground">{currency(order.itemsSubtotal)}</span>
          </span>
          <span className="text-muted-foreground">
            Shipping <span className="text-foreground">{currency(order.shippingCharged)}</span>
          </span>
          <span className="text-muted-foreground">
            Tax <span className="text-foreground">{currency(order.taxCollected)}</span>
          </span>
          {feesPending ? (
            <Badge variant="warning">Fees pending</Badge>
          ) : (
            <span className="text-muted-foreground">
              Fees <span className="text-foreground">-{currency(order.feeTotal)}</span>
            </span>
          )}
          <span className="text-muted-foreground">
            Expected payout{" "}
            <span className="text-foreground">{currency(order.expectedPayout)}</span>
          </span>
          {hasPayout && (
            <span className="text-muted-foreground">
              {order.platform} payout{" "}
              <span className="text-foreground">{currency(order.payoutAmount)}</span>
            </span>
          )}
          {varianceOff && (
            <Badge variant="warning">
              {variance > 0 ? "+" : ""}
              {currency(variance)} vs expected
            </Badge>
          )}
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <form action={reconcileAction} className="space-y-4">
          <input type="hidden" name="orderId" value={order.id ?? ""} />
          <input type="hidden" name="mappingJson" value={mappingJson} />
          <div className="overflow-x-auto rounded-lg border">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
                <tr>
                  <th className="px-3 py-2 font-medium">Platform line</th>
                  <th className="px-3 py-2 text-right font-medium">Price</th>
                  <th className="px-3 py-2 font-medium">Inventory item</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {lines.map((line: PlatformOrderLine) => {
                  const id = line.id ?? "";
                  const choice = choices[id] ?? null;
                  const importedMoneyDetails = [
                    typeof line.platformFees === "number"
                      ? `fees -${currency(line.platformFees)}`
                      : null,
                    typeof line.sellerShippingCost === "number"
                      ? `label -${currency(line.sellerShippingCost)}`
                      : null,
                    typeof line.netSales === "number" ? `net ${currency(line.netSales)}` : null,
                  ].filter(Boolean);
                  return (
                    <tr key={id}>
                      <td className="px-3 py-2">
                        <span className="font-medium">{line.title}</span>
                        <span className="text-muted-foreground block text-xs">
                          {line.quantity && line.quantity > 1 ? `×${line.quantity} · ` : ""}
                          {line.externalItemId ? `item ${line.externalItemId}` : "no platform id"}
                        </span>
                        {importedMoneyDetails.length > 0 && (
                          <span className="text-muted-foreground mt-1 block text-xs">
                            {importedMoneyDetails.join(" · ")}
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums">
                        {currency((line.unitPrice ?? 0) * (line.quantity ?? 1))}
                      </td>
                      <td className="px-3 py-2">
                        <InventoryPicker
                          value={choice}
                          lineTitle={line.title ?? "this line"}
                          suggestion={
                            line.suggestedInventoryItemId
                              ? {
                                  id: line.suggestedInventoryItemId,
                                  title: line.suggestedInventoryItemTitle ?? "Suggested item",
                                  alreadySold: line.suggestedInventoryItemAlreadySold ?? false,
                                  exact: line.suggestionExact ?? false,
                                }
                              : null
                          }
                          onChange={(next) => setChoices((current) => ({ ...current, [id]: next }))}
                        />
                        {choice ? (
                          <span
                            className={`mt-1 block text-xs ${choice.kind === "item" && choice.isSold ? "text-amber-700" : "text-emerald-700"}`}
                          >
                            {choice.kind === "create"
                              ? "→ creates the item and records the sale"
                              : choice.isSold
                                ? "→ corrects the recorded sale (exact fees)"
                                : "→ records a new sale"}
                          </span>
                        ) : null}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {!summary.distinct && (
            <p className="text-destructive text-xs">
              Two lines point to the same item. Each item can match only one line.
            </p>
          )}

          <Textarea name="notes" rows={2} placeholder="Note (optional)" />

          <div className="flex items-center justify-between gap-3">
            <p className="text-muted-foreground text-xs">
              {feesPending
                ? `${order.platform} has not posted this order's fees yet. The next sales scan after they appear brings them in; it can be settled then.`
                : `Writes ${order.platform}'s exact price, shipping, tax, and fees. Items you already recorded are corrected in place (audited); the rest become new sales.`}
            </p>
            <ReconcileButton disabled={!canReconcile} label={confirmLabel(summary)} />
          </div>

          {reconcileState.message && (
            <div
              className={`rounded-lg border p-3 text-sm ${
                reconcileState.ok
                  ? "border-emerald-500/30 bg-emerald-500/5 text-emerald-700"
                  : "border-destructive/30 bg-destructive/5 text-destructive"
              }`}
            >
              {reconcileState.message}
            </div>
          )}
        </form>
        {ignoreState.message && !ignoreState.ok && (
          <p className="text-destructive text-xs">{ignoreState.message}</p>
        )}
      </CardContent>
    </Card>
  );
}

export function PlatformOrderReconciler({ orders }: { orders: PlatformOrder[] }) {
  if (orders.length === 0) {
    return (
      <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
        Nothing to decide — anything that matched exactly is already recorded. Import an eBay All orders report below, then optionally
        import Listing sales to add exact fees and net-sales figures.
      </div>
    );
  }
  return (
    <div className="space-y-4">
      {orders.map((order) => (
        <OrderCard key={order.id} order={order} />
      ))}
    </div>
  );
}
