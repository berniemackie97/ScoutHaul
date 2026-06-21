"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import {
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Input,
  Label,
  Textarea,
} from "@overhaul/ui";
import { updateSale, type SaleActionResult } from "./actions";

type SaleDto = components["schemas"]["SaleDto"];

const initialState: SaleActionResult = { ok: false, message: "" };

function SaveButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending}>
      {pending ? "Saving correction…" : "Save correction"}
    </Button>
  );
}

function Money({ label, name, defaultValue }: { label: string; name: string; defaultValue: number }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="number" step="0.01" min="0" defaultValue={defaultValue} />
    </div>
  );
}

export function SaleEditor({ sale }: { sale: SaleDto }) {
  const action = updateSale.bind(null, sale.id!);
  const [state, formAction] = useActionState(action, initialState);
  const soldDate = sale.soldAt ? new Date(sale.soldAt).toISOString().slice(0, 10) : "";

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Correct this sale</CardTitle>
        <CardDescription>Every change is recorded with a reason and a before/after snapshot. Net profit is recomputed from these inputs; tax is never part of net.</CardDescription>
      </CardHeader>
      <CardContent>
        <form action={formAction} className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="platform">Channel / platform</Label>
              <Input id="platform" name="platform" defaultValue={sale.platform ?? ""} required />
            </div>
            <div className="space-y-2">
              <Label htmlFor="soldAt">Sold date</Label>
              <Input id="soldAt" name="soldAt" type="date" defaultValue={soldDate} />
            </div>
          </div>

          <div className="grid gap-4 sm:grid-cols-3">
            <Money label="Sold price" name="soldPrice" defaultValue={sale.soldPrice ?? 0} />
            <Money label="Platform fees" name="platformFees" defaultValue={sale.platformFees ?? 0} />
            <Money label="Payment fees" name="paymentFees" defaultValue={sale.paymentFees ?? 0} />
            <Money label="Shipping cost" name="shippingCost" defaultValue={sale.shippingCost ?? 0} />
            <Money label="Supplies cost" name="suppliesCost" defaultValue={sale.suppliesCost ?? 0} />
            <Money label="Other costs" name="otherCosts" defaultValue={sale.otherCosts ?? 0} />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Money label="Sales tax collected" name="salesTaxCollected" defaultValue={sale.salesTaxCollected ?? 0} />
            <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
              <input
                type="checkbox"
                name="taxRemittedByPlatform"
                defaultChecked={sale.taxRemittedByPlatform ?? true}
                className="mt-0.5 h-4 w-4"
              />
              <span>
                <span className="font-medium">Platform collected &amp; remits the tax</span>
                <span className="block text-xs text-muted-foreground">Marketplace facilitator — pass-through, not your liability. Uncheck if you owe it.</span>
              </span>
            </label>
          </div>

          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea id="notes" name="notes" defaultValue={sale.notes ?? ""} rows={2} />
          </div>

          <div className="space-y-2">
            <Label htmlFor="reason">Reason for this correction <span className="text-destructive">*</span></Label>
            <Input id="reason" name="reason" required placeholder="e.g. Actual eBay fee differed from the estimate" />
          </div>

          <div className="flex items-center justify-between gap-4 border-t pt-4">
            <p
              aria-live="polite"
              className={`text-sm ${state.message ? (state.ok ? "text-green-700" : "text-destructive") : "text-muted-foreground"}`}
            >
              {state.message || "Corrections preserve full history — the original is never erased."}
            </p>
            <SaveButton />
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
