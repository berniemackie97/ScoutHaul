"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { createPurchaseLot, type PurchaseActionResult } from "./actions";

type AllocationMethod = "Equal" | "WeightedByExpectedValue" | "Manual";

const initial: PurchaseActionResult = { ok: false, message: "" };
const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";
const today = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 10);

function SubmitButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Saving lot…" : "Record purchase lot"}</Button>;
}

function NumberField({ name, label, value, onChange }: { name: string; label: string; value: string; onChange: (value: string) => void }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="number" step="0.01" min="0" value={value} onChange={(event) => onChange(event.target.value)} />
    </div>
  );
}

export function PurchaseLotCreator({ candidates }: { candidates: InventoryItem[] }) {
  const [state, action] = useActionState(createPurchaseLot, initial);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [allocationMethod, setAllocationMethod] = useState<AllocationMethod>("Equal");
  const [purchaseSubtotal, setPurchaseSubtotal] = useState("");
  const [shippingCost, setShippingCost] = useState("");
  const [taxCost, setTaxCost] = useState("");
  const [feeCost, setFeeCost] = useState("");
  const [otherCost, setOtherCost] = useState("");

  const selectedItems = useMemo(
    () => candidates.filter((item) => item.id && selected.has(item.id)),
    [candidates, selected],
  );
  const landedCost = [purchaseSubtotal, shippingCost, taxCost, feeCost, otherCost].reduce((sum, value) => sum + (Number(value) || 0), 0);
  const equalAllocation = selectedItems.length > 0 ? landedCost / selectedItems.length : 0;

  function toggle(id: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <form action={action} className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
      <div className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Purchase source document</CardTitle>
            <CardDescription>Use this for a thrift receipt, auction invoice, pallet, pickup, or loose lot. The total landed cost is allocated to selected items.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="name">Lot name</Label>
              <Input id="name" name="name" required placeholder="e.g. Goodwill bins 2026-07-02, auction lot 114" />
            </div>
            <div className="space-y-2">
              <Label htmlFor="supplierName">Supplier / source</Label>
              <Input id="supplierName" name="supplierName" placeholder="Goodwill, Facebook seller, auction house…" />
            </div>
            <div className="space-y-2">
              <Label htmlFor="externalReference">Receipt / invoice / order #</Label>
              <Input id="externalReference" name="externalReference" />
            </div>
            <div className="space-y-2">
              <Label htmlFor="purchasedAt">Purchase date</Label>
              <Input id="purchasedAt" name="purchasedAt" type="date" defaultValue={today()} required />
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
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Landed cost</CardTitle>
            <CardDescription>Subtotal plus shipping/tax/fees/other becomes the total cost basis allocated to items.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-3 lg:grid-cols-5">
            <NumberField name="purchaseSubtotal" label="Purchase subtotal" value={purchaseSubtotal} onChange={setPurchaseSubtotal} />
            <NumberField name="shippingCost" label="Inbound shipping" value={shippingCost} onChange={setShippingCost} />
            <NumberField name="taxCost" label="Tax" value={taxCost} onChange={setTaxCost} />
            <NumberField name="feeCost" label="Buyer fees" value={feeCost} onChange={setFeeCost} />
            <NumberField name="otherCost" label="Other landed cost" value={otherCost} onChange={setOtherCost} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Allocate to inventory</CardTitle>
            <CardDescription>Select existing inventory records. Manual mode requires the selected manual costs to equal landed cost.</CardDescription>
          </CardHeader>
          <CardContent>
            {candidates.length === 0 ? (
              <p className="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground">No unassigned active inventory candidates found.</p>
            ) : (
              <div className="max-h-[520px] overflow-auto rounded-lg border">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 border-b bg-muted/80 text-left text-xs uppercase tracking-wide text-muted-foreground backdrop-blur">
                    <tr>
                      <th className="px-3 py-2 font-medium">Item</th>
                      <th className="px-3 py-2 text-right font-medium">Current cost</th>
                      <th className="px-3 py-2 text-right font-medium">Expected</th>
                      <th className="px-3 py-2 font-medium">Manual cost</th>
                      <th className="px-3 py-2 font-medium">Weight</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y">
                    {candidates.map((item) => {
                      if (!item.id) return null;
                      const checked = selected.has(item.id);
                      return (
                        <tr key={item.id} className={checked ? "bg-brand-50/40" : undefined}>
                          <td className="px-3 py-2">
                            <label className="flex items-start gap-2">
                              <input type="checkbox" name="inventoryItemId" value={item.id} checked={checked} onChange={() => toggle(item.id!)} className="mt-1" />
                              <span>
                                <span className="font-medium">{item.title}</span>
                                <span className="block text-xs text-muted-foreground">{[item.status, item.category, item.storageLocation].filter(Boolean).join(" · ")}</span>
                              </span>
                            </label>
                          </td>
                          <td className="px-3 py-2 text-right tabular-nums">{money(item.purchasePrice)}</td>
                          <td className="px-3 py-2 text-right tabular-nums">{money(item.estimatedSalePrice)}</td>
                          <td className="px-3 py-2"><Input name={`manualCost:${item.id}`} type="number" step="0.01" min="0" disabled={!checked || allocationMethod !== "Manual"} placeholder={allocationMethod === "Equal" ? money(equalAllocation) : ""} /></td>
                          <td className="px-3 py-2"><Input name={`weight:${item.id}`} type="number" step="0.01" min="0" disabled={!checked || allocationMethod !== "WeightedByExpectedValue"} placeholder={item.estimatedSalePrice != null ? String(item.estimatedSalePrice) : ""} /></td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-lg">Notes</CardTitle></CardHeader>
          <CardContent><Textarea name="notes" rows={3} placeholder="Receipt context, payment account, condition exceptions, follow-up work…" /></CardContent>
        </Card>
      </div>

      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card className="p-6">
          <p className="text-sm text-muted-foreground">Total landed cost</p>
          <p className="mt-2 text-4xl font-semibold tabular-nums">{money(landedCost)}</p>
          <div className="mt-5 space-y-2 border-t pt-4 text-sm">
            <div className="flex justify-between"><span className="text-muted-foreground">Selected items</span><span>{selectedItems.length}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Equal cost each</span><span>{selectedItems.length ? money(equalAllocation) : "—"}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Method</span><span>{allocationMethod}</span></div>
          </div>
        </Card>
        {state.message && <div className={`rounded-lg border p-3 text-sm ${state.ok ? "border-green-200 bg-green-50 text-green-800" : "border-destructive/30 bg-destructive/5 text-destructive"}`}>{state.message}</div>}
        <SubmitButton />
      </aside>
    </form>
  );
}
