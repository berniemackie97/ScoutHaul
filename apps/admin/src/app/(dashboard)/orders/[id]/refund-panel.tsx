"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, CardContent, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { recordRefund, voidRefund, type SaleActionResult } from "./sale-actions";

type Refund = components["schemas"]["RefundDto"];

const initial: SaleActionResult = { ok: false, message: "" };
const money = (value: number | null | undefined) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value ?? 0);
const day = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";
const today = new Date().toISOString().slice(0, 10);

function RecordButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending}>
      {pending ? "Recording…" : "Record refund"}
    </Button>
  );
}

function VoidButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" variant="ghost" size="sm" disabled={pending} className="text-destructive h-7 px-2 text-xs">
      {pending ? "…" : "Void"}
    </Button>
  );
}

export function RefundPanel({ saleId, refunds }: { saleId: string; refunds: Refund[] }) {
  const [state, action] = useActionState(recordRefund.bind(null, saleId), initial);
  const fieldErrors = state.fieldErrors ?? {};
  const totalRefunded = refunds.reduce((sum, refund) => sum + (refund.itemAmount ?? 0) + (refund.shippingRefunded ?? 0), 0);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Refunds</CardTitle>
        <p className="text-muted-foreground text-sm">
          Record a full or partial refund as its own event. Net profit recomputes automatically; a returned item
          releases back to inventory. Item &amp; shipping reduce revenue, a fee credit lifts net, tax is pass-through.
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        {refunds.length > 0 && (
          <div className="space-y-2">
            {refunds.map((refund) => (
              <div key={refund.id} className="bg-muted/30 flex items-start justify-between gap-3 rounded-lg p-3 text-sm">
                <div className="space-y-0.5">
                  <div className="flex items-center gap-2">
                    <Badge variant={refund.type === "Full" ? "destructive" : "secondary"}>{refund.type}</Badge>
                    <span className="font-medium tabular-nums">{money((refund.itemAmount ?? 0) + (refund.shippingRefunded ?? 0))}</span>
                    {refund.itemReturned && <span className="text-muted-foreground text-xs">· item returned</span>}
                  </div>
                  <p className="text-muted-foreground text-xs">
                    {day(refund.refundedAt)} · {refund.reason}
                    {(refund.feeCredit ?? 0) > 0 ? ` · fee credit ${money(refund.feeCredit)}` : ""}
                  </p>
                </div>
                <form action={voidRefund.bind(null, saleId, refund.id ?? "")}>
                  <VoidButton />
                </form>
              </div>
            ))}
            <div className="text-muted-foreground flex justify-between px-1 text-xs">
              <span>Total refunded to buyer</span>
              <span className="tabular-nums">{money(totalRefunded)}</span>
            </div>
          </div>
        )}

        <form action={action} className="space-y-4 border-t pt-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="grid gap-1.5">
              <Label htmlFor="itemAmount">Item amount refunded</Label>
              <Input id="itemAmount" name="itemAmount" type="number" step="0.01" min="0" placeholder="0.00" />
              {fieldErrors.itemAmount && <p className="text-destructive text-xs">{fieldErrors.itemAmount}</p>}
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="shippingRefunded">Shipping refunded</Label>
              <Input id="shippingRefunded" name="shippingRefunded" type="number" step="0.01" min="0" placeholder="0.00" />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="feeCredit">Platform fee credited back</Label>
              <Input id="feeCredit" name="feeCredit" type="number" step="0.01" min="0" placeholder="0.00" />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="taxRefunded">Tax refunded</Label>
              <Input id="taxRefunded" name="taxRefunded" type="number" step="0.01" min="0" placeholder="0.00" />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="refundedAt">Refund date</Label>
              <Input id="refundedAt" name="refundedAt" type="date" max={today} defaultValue={today} />
              {fieldErrors.refundedAt && <p className="text-destructive text-xs">{fieldErrors.refundedAt}</p>}
            </div>
            <label className="mt-6 flex items-center gap-2 text-sm">
              <input type="checkbox" name="itemReturned" className="border-input h-4 w-4 rounded" />
              Item was returned (restock)
            </label>
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="reason">Reason</Label>
            <Input id="reason" name="reason" placeholder="e.g. Partial refund — minor damage, buyer kept item" />
            {fieldErrors.reason && <p className="text-destructive text-xs">{fieldErrors.reason}</p>}
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="notes">Notes (optional)</Label>
            <Textarea id="notes" name="notes" rows={2} />
          </div>
          <div className="flex items-center justify-between gap-3">
            {state.message && <p className={`text-sm ${state.ok ? "text-emerald-700" : "text-destructive"}`}>{state.message}</p>}
            <div className="ml-auto">
              <RecordButton />
            </div>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
