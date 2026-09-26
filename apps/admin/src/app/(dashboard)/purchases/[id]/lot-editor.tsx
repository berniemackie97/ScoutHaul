"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea, formatCurrency } from "@overhaul/ui";
import { updateLot, type LotActionResult } from "./actions";

type AcquisitionLot = components["schemas"]["AcquisitionLotDto"];
type AllocationMethod = components["schemas"]["AcquisitionLotAllocationMethod"];

const initial: LotActionResult = { ok: false, message: "" };
const dateInput = (iso: string | null | undefined) => (iso ? new Date(iso).toISOString().slice(0, 10) : "");

function SubmitButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Saving…" : "Save lot & re-allocate"}</Button>;
}

function NumberField({ name, label, defaultValue, value, onChange }: { name: string; label: string; defaultValue?: string; value?: string; onChange?: (value: string) => void }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="number" step="0.01" min="0" defaultValue={defaultValue} value={value} onChange={onChange ? (event) => onChange(event.target.value) : undefined} />
    </div>
  );
}

export function LotEditor({ lot }: { lot: AcquisitionLot }) {
  const boundAction = updateLot.bind(null, lot.id!);
  const [state, action] = useActionState(boundAction, initial);
  const [allocationMethod, setAllocationMethod] = useState<AllocationMethod>(lot.allocationMethod ?? "Equal");
  const [kept, setKept] = useState<Set<string>>(new Set((lot.items ?? []).map((item) => item.inventoryItemId!)));
  const [subtotal, setSubtotal] = useState(String(lot.purchaseSubtotal ?? 0));
  const [shipping, setShipping] = useState(String(lot.shippingCost ?? 0));
  const [tax, setTax] = useState(String(lot.taxCost ?? 0));
  const [fee, setFee] = useState(String(lot.feeCost ?? 0));
  const [other, setOther] = useState(String(lot.otherCost ?? 0));

  const landed = [subtotal, shipping, tax, fee, other].reduce((sum, value) => sum + (Number(value) || 0), 0);
  const items = lot.items ?? [];
  const keptCount = items.filter((item) => kept.has(item.inventoryItemId!)).length;
  const equalEach = useMemo(() => (keptCount > 0 ? landed / keptCount : 0), [landed, keptCount]);

  function toggle(id: string) {
    setKept((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Revise lot</CardTitle>
        <CardDescription>Correct landed cost, change the allocation method, or detach items. Saving re-allocates cost basis and back-corrects any recorded sales.</CardDescription>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-5">
          <input type="hidden" name="version" value={lot.version ?? ""} />
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="name">Lot name</Label>
              <Input id="name" name="name" defaultValue={lot.name ?? ""} required />
            </div>
            <div className="space-y-2">
              <Label htmlFor="supplierName">Supplier / source</Label>
              <Input id="supplierName" name="supplierName" defaultValue={lot.supplierName ?? ""} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="externalReference">Receipt / invoice #</Label>
              <Input id="externalReference" name="externalReference" defaultValue={lot.externalReference ?? ""} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="purchasedAt">Purchase date</Label>
              <Input id="purchasedAt" name="purchasedAt" type="date" defaultValue={dateInput(lot.purchasedAt)} required />
            </div>
            <div className="space-y-2">
              <Label htmlFor="allocationMethod">Allocation method</Label>
              <select
                id="allocationMethod"
                name="allocationMethod"
                value={allocationMethod}
                onChange={(event) => setAllocationMethod(event.target.value as AllocationMethod)}
                className="h-10 w-full rounded-lg border border-input bg-background px-3 text-sm"
              >
                <option value="Equal">Equal split</option>
                <option value="WeightedByExpectedValue">Weighted by expected value</option>
                <option value="Manual">Manual exact allocation</option>
              </select>
            </div>
          </div>

          <div className="grid gap-4 sm:grid-cols-3 lg:grid-cols-5">
            <NumberField name="purchaseSubtotal" label="Subtotal" value={subtotal} onChange={setSubtotal} />
            <NumberField name="shippingCost" label="Shipping" value={shipping} onChange={setShipping} />
            <NumberField name="taxCost" label="Tax" value={tax} onChange={setTax} />
            <NumberField name="feeCost" label="Buyer fees" value={fee} onChange={setFee} />
            <NumberField name="otherCost" label="Other" value={other} onChange={setOther} />
          </div>

          <div className="overflow-x-auto rounded-lg border">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 font-medium">Keep</th>
                  <th className="px-3 py-2 font-medium">Item</th>
                  <th className="px-3 py-2 text-right font-medium">Current allocated</th>
                  <th className="px-3 py-2 font-medium">Manual cost</th>
                  <th className="px-3 py-2 font-medium">Weight</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {items.map((item) => {
                  const itemId = item.inventoryItemId!;
                  const checked = kept.has(itemId);
                  return (
                    <tr key={itemId} className={checked ? undefined : "opacity-50"}>
                      <td className="px-3 py-2">
                        <input type="checkbox" name="inventoryItemId" value={itemId} checked={checked} onChange={() => toggle(itemId)} />
                      </td>
                      <td className="px-3 py-2">
                        <span className="font-medium">{item.inventoryItemTitle}</span>
                        <span className="block text-xs text-muted-foreground">{item.inventoryItemStatus}</span>
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums">{formatCurrency(item.allocatedCost)}</td>
                      <td className="px-3 py-2">
                        <Input name={`manualCost:${itemId}`} type="number" step="0.01" min="0" defaultValue={allocationMethod === "Manual" ? String(item.allocatedCost) : ""} disabled={!checked || allocationMethod !== "Manual"} placeholder={allocationMethod === "Equal" ? formatCurrency(equalEach) : ""} />
                      </td>
                      <td className="px-3 py-2">
                        <Input name={`weight:${itemId}`} type="number" step="0.01" min="0" defaultValue={item.allocationWeight != null ? String(item.allocationWeight) : ""} disabled={!checked || allocationMethod !== "WeightedByExpectedValue"} />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea id="notes" name="notes" rows={3} defaultValue={lot.notes ?? ""} />
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3 border-t pt-4 text-sm">
            <span className="text-muted-foreground">
              Landed cost <span className="text-foreground font-semibold tabular-nums">{formatCurrency(landed)}</span> · keeping {keptCount} item{keptCount === 1 ? "" : "s"}
              {allocationMethod === "Equal" && keptCount > 0 ? ` · ${formatCurrency(equalEach)} each` : ""}
            </span>
            <SubmitButton />
          </div>
          {state.message && (
            <div className={`rounded-lg border p-3 text-sm ${state.ok ? "border-green-200 bg-green-50 text-green-800" : "border-destructive/30 bg-destructive/5 text-destructive"}`}>
              {state.message}
            </div>
          )}
        </form>
      </CardContent>
    </Card>
  );
}
