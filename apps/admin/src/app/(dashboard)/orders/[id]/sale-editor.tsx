"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { updateSale, type SaleActionResult } from "./sale-actions";

type SaleDto = components["schemas"]["SaleDto"];
type Channel = components["schemas"]["SaleChannelOptionDto"];

const initialState: SaleActionResult = { ok: false, message: "" };

function SaveButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending}>
      {pending ? "Saving correction…" : "Save correction"}
    </Button>
  );
}

function Money({
  label,
  name,
  defaultValue,
  error,
  formKey,
}: {
  label: string;
  name: string;
  defaultValue: string | number;
  error?: string;
  formKey?: string;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input
        key={`${name}:${formKey ?? "initial"}`}
        id={name}
        name={name}
        type="number"
        step="0.01"
        min="0"
        defaultValue={defaultValue}
        aria-invalid={error ? "true" : undefined}
      />
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

export function SaleEditor({ sale, channels }: { sale: SaleDto; channels: Channel[] }) {
  const action = updateSale.bind(null, sale.id!);
  const [state, formAction] = useActionState(action, initialState);
  const soldDate = sale.soldAt ? new Date(sale.soldAt).toISOString().slice(0, 10) : "";
  const values = state.values ?? {};
  const fieldErrors = state.fieldErrors ?? {};
  const formKey = state.formKey ?? "initial";
  const valueFor = (name: string, fallback?: string | number | null) => values[name] ?? fallback ?? "";
  const taxRemitted = state.values ? values.taxRemittedByPlatform !== "off" : sale.taxRemittedByPlatform ?? true;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Correct this sale</CardTitle>
        <CardDescription>
          Every change is recorded with a reason and a before/after snapshot. Buyer shipping is revenue; shipping labels are costs; tax is
          never part of net.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form action={formAction} className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="platform">Channel / platform</Label>
              <select
                key={`platform:${formKey}`}
                id="platform"
                name="platform"
                defaultValue={valueFor("platform", sale.platform)}
                required
                className="border-input bg-background h-10 w-full rounded-lg border px-3 text-sm"
                aria-invalid={fieldErrors.platform ? "true" : undefined}
              >
                {channels.flatMap((channel) =>
                  channel.value
                    ? [
                        <option key={channel.value} value={channel.value}>
                          {channel.label}
                        </option>,
                      ]
                    : []
                )}
              </select>
              {fieldErrors.platform && <p className="text-destructive text-xs">{fieldErrors.platform}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="soldAt">Sold date</Label>
              <Input
                key={`soldAt:${formKey}`}
                id="soldAt"
                name="soldAt"
                type="date"
                defaultValue={valueFor("soldAt", soldDate)}
                aria-invalid={fieldErrors.soldAt ? "true" : undefined}
              />
              {fieldErrors.soldAt && <p className="text-destructive text-xs">{fieldErrors.soldAt}</p>}
            </div>
          </div>

          <div className="grid gap-4 sm:grid-cols-3">
            <Money label="Sold price" name="soldPrice" defaultValue={valueFor("soldPrice", sale.soldPrice ?? 0)} error={fieldErrors.soldPrice} formKey={formKey} />
            <Money label="Buyer shipping charged" name="shippingCharged" defaultValue={valueFor("shippingCharged", sale.shippingCharged ?? 0)} error={fieldErrors.shippingCharged} formKey={formKey} />
            <Money label="Platform fees" name="platformFees" defaultValue={valueFor("platformFees", sale.platformFees ?? 0)} error={fieldErrors.platformFees} formKey={formKey} />
            <Money label="Payment fees" name="paymentFees" defaultValue={valueFor("paymentFees", sale.paymentFees ?? 0)} error={fieldErrors.paymentFees} formKey={formKey} />
            <Money label="Shipping label cost" name="shippingCost" defaultValue={valueFor("shippingCost", sale.shippingCost ?? 0)} error={fieldErrors.shippingCost} formKey={formKey} />
            <Money label="Supplies cost" name="suppliesCost" defaultValue={valueFor("suppliesCost", sale.suppliesCost ?? 0)} error={fieldErrors.suppliesCost} formKey={formKey} />
            <Money label="Other costs" name="otherCosts" defaultValue={valueFor("otherCosts", sale.otherCosts ?? 0)} error={fieldErrors.otherCosts} formKey={formKey} />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Money label="Sales tax collected" name="salesTaxCollected" defaultValue={valueFor("salesTaxCollected", sale.salesTaxCollected ?? 0)} error={fieldErrors.salesTaxCollected} formKey={formKey} />
            <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
              <input
                key={`taxRemittedByPlatform:${formKey}`}
                type="checkbox"
                name="taxRemittedByPlatform"
                defaultChecked={taxRemitted}
                className="mt-0.5 h-4 w-4"
              />
              <span>
                <span className="font-medium">Platform collected &amp; remits the tax</span>
                <span className="text-muted-foreground block text-xs">
                  Marketplace facilitator — pass-through, not your liability. Uncheck if you owe it.
                </span>
              </span>
            </label>
          </div>

          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea key={`notes:${formKey}`} id="notes" name="notes" defaultValue={valueFor("notes", sale.notes)} rows={2} />
          </div>

          <div className="space-y-2">
            <Label htmlFor="reason">
              Reason for this correction <span className="text-destructive">*</span>
            </Label>
            <Input
              key={`reason:${formKey}`}
              id="reason"
              name="reason"
              required
              placeholder="e.g. Actual eBay fee differed from the estimate"
              defaultValue={values.reason ?? ""}
              aria-invalid={fieldErrors.reason ? "true" : undefined}
            />
            {fieldErrors.reason && <p className="text-destructive text-xs">{fieldErrors.reason}</p>}
          </div>

          <div className="flex items-center justify-between gap-4 border-t pt-4">
            <p
              aria-live="polite"
              className={`text-sm ${state.message ? (state.ok ? "text-success" : "text-destructive") : "text-muted-foreground"}`}
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
