"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, CardContent, CardHeader, CardTitle, Textarea } from "@overhaul/ui";
import { reconcilePlatformOrder, ignorePlatformOrder, type ActionResult } from "./actions";

type PlatformOrder = components["schemas"]["PlatformOrderDto"];
type PlatformOrderLine = components["schemas"]["PlatformOrderLineDto"];

export type MatchTarget = { id: string; title: string; isSold: boolean };

const initial: ActionResult = { ok: false, message: "" };
const selectClass =
  "h-9 w-full rounded-lg border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring";
const currency = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";
const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

function ReconcileButton({
  disabled,
  willCreate,
  willCorrect,
}: {
  disabled: boolean;
  willCreate: number;
  willCorrect: number;
}) {
  const { pending } = useFormStatus();
  const label =
    willCreate + willCorrect === 0
      ? "Match every line first"
      : [
          willCreate > 0 ? `record ${willCreate}` : null,
          willCorrect > 0 ? `correct ${willCorrect}` : null,
        ]
          .filter(Boolean)
          .join(" · ");
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

function OrderCard({ order, targets }: { order: PlatformOrder; targets: MatchTarget[] }) {
  const [reconcileState, reconcileAction] = useActionState(reconcilePlatformOrder, initial);
  const [ignoreState, ignoreAction] = useActionState(ignorePlatformOrder, initial);
  const lines = order.lines ?? [];

  const soldById = useMemo(
    () => new Map(targets.map((target) => [target.id, target.isSold])),
    [targets]
  );
  // Unsold first (a new sale), then already-recorded (a correction); each alphabetical.
  const sortedTargets = useMemo(
    () =>
      [...targets].sort(
        (a, b) => Number(a.isSold) - Number(b.isSold) || a.title.localeCompare(b.title)
      ),
    [targets]
  );

  const [map, setMap] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      lines.map((line) => [
        line.id ?? "",
        line.inventoryItemId ?? line.suggestedInventoryItemId ?? "",
      ])
    )
  );

  const chosen = Object.values(map).filter(Boolean);
  const allMatched = lines.length > 0 && lines.every((line) => map[line.id ?? ""]);
  const allDistinct = new Set(chosen).size === chosen.length;
  const canReconcile = allMatched && allDistinct;
  const willCorrect = chosen.filter((id) => soldById.get(id)).length;
  const willCreate = chosen.length - willCorrect;

  const mappingJson = useMemo(
    () =>
      JSON.stringify(
        lines
          .filter((line) => line.id && map[line.id])
          .map((line) => ({ platformOrderLineId: line.id, inventoryItemId: map[line.id!] }))
      ),
    [lines, map]
  );

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
              {date(order.orderedAt)}
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
          <span className="text-muted-foreground">
            Fees <span className="text-foreground">-{currency(order.feeTotal)}</span>
          </span>
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
                  const selected = map[id];
                  const willCorrectLine = selected ? soldById.get(selected) : false;
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
                        <select
                          className={selectClass}
                          value={map[id] ?? ""}
                          onChange={(event) =>
                            setMap((current) => ({ ...current, [id]: event.target.value }))
                          }
                        >
                          <option value="">Choose an item…</option>
                          {sortedTargets.map((target) => (
                            <option key={target.id} value={target.id}>
                              {target.title}
                              {target.isSold ? " · recorded" : ""}
                              {target.id === line.suggestedInventoryItemId ? " (suggested)" : ""}
                            </option>
                          ))}
                        </select>
                        {selected && (
                          <span
                            className={`mt-1 block text-xs ${willCorrectLine ? "text-amber-700" : "text-emerald-700"}`}
                          >
                            {willCorrectLine
                              ? "→ corrects the recorded sale (exact fees)"
                              : "→ records a new sale"}
                          </span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {!allDistinct && (
            <p className="text-destructive text-xs">
              Two lines point to the same item. Each item can match only one line.
            </p>
          )}

          <Textarea name="notes" rows={2} placeholder="Note (optional)" />

          <div className="flex items-center justify-between gap-3">
            <p className="text-muted-foreground text-xs">
              Writes {order.platform}&apos;s exact price, shipping, tax, and fees. Items you already
              recorded are corrected in place (audited); the rest become new sales.
            </p>
            <ReconcileButton
              disabled={!canReconcile}
              willCreate={willCreate}
              willCorrect={willCorrect}
            />
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

export function PlatformOrderReconciler({
  orders,
  targets,
}: {
  orders: PlatformOrder[];
  targets: MatchTarget[];
}) {
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
        <OrderCard key={order.id} order={order} targets={targets} />
      ))}
    </div>
  );
}
