"use client";

import { useActionState, useEffect, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { createPurchaseLot, type PurchaseActionResult } from "./actions";

type AllocationMethod = "Equal" | "WeightedByExpectedValue" | "Manual";
type PurchaseKind = "Single" | "Lot";

const initial: PurchaseActionResult = { ok: false, message: "" };
const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";
const today = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 10);

function SubmitButton({ kind }: { kind: PurchaseKind }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Saving…" : kind === "Single" ? "Record single-item purchase" : "Record acquisition lot"}</Button>;
}

function NumberField({
  name,
  label,
  value,
  onChange,
  error,
}: {
  name: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input
        id={name}
        name={name}
        type="number"
        step="0.01"
        min="0"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={error ? "true" : undefined}
      />
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

export function PurchaseLotCreator({ candidates }: { candidates: InventoryItem[] }) {
  const [state, action] = useActionState(createPurchaseLot, initial);
  const values = state.values ?? {};
  const fieldErrors = state.fieldErrors ?? {};
  const formKey = state.formKey ?? "initial";
  const valueFor = (name: string, fallback = "") => values[name] ?? fallback;
  const [purchaseKind, setPurchaseKind] = useState<PurchaseKind>("Single");
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

  useEffect(() => {
    if (!state.values) return;
    setPurchaseKind(state.values.purchaseKind === "Lot" ? "Lot" : "Single");
    setAllocationMethod((state.values.allocationMethod as AllocationMethod | undefined) ?? "Equal");
    setPurchaseSubtotal(state.values.purchaseSubtotal ?? "");
    setShippingCost(state.values.shippingCost ?? "");
    setTaxCost(state.values.taxCost ?? "");
    setFeeCost(state.values.feeCost ?? "");
    setOtherCost(state.values.otherCost ?? "");
    setSelected(new Set((state.values.selectedInventoryItemIds ?? "").split("|").filter(Boolean)));
  }, [state.formKey, state.values]);

  useEffect(() => {
    if (purchaseKind !== "Single" || selected.size <= 1) return;
    const first = [...selected][0];
    setSelected(first ? new Set([first]) : new Set());
  }, [purchaseKind, selected]);

  function toggle(id: string) {
    setSelected((current) => {
      if (purchaseKind === "Single") {
        return current.has(id) ? new Set() : new Set([id]);
      }
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <form action={action} className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
      <input type="hidden" name="purchaseKind" value={purchaseKind} />
      <div className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Purchase source document</CardTitle>
            <CardDescription>Record a single purchased item or a multi-item receipt, auction lot, pallet, pickup, or haul. Landed cost flows into item cost basis.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="purchaseKind">Purchase type</Label>
              <select
                id="purchaseKind"
                value={purchaseKind}
                onChange={(event) => setPurchaseKind(event.target.value as PurchaseKind)}
                className="h-10 w-full rounded-lg border border-input bg-background px-3 text-sm"
              >
                <option value="Single">Single item purchase</option>
                <option value="Lot">Multi-item acquisition lot</option>
              </select>
            </div>
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="name">{purchaseKind === "Single" ? "Purchase name" : "Lot name"}</Label>
              <Input
                key={`name:${formKey}`}
                id="name"
                name="name"
                required
                placeholder={purchaseKind === "Single" ? "e.g. Gardevoir & Sylveon GX purchase" : "e.g. Goodwill bins 2026-07-02, auction lot 114"}
                defaultValue={valueFor("name")}
                aria-invalid={fieldErrors.name ? "true" : undefined}
              />
              {fieldErrors.name && <p className="text-destructive text-xs">{fieldErrors.name}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="supplierName">Supplier / source</Label>
              <Input key={`supplierName:${formKey}`} id="supplierName" name="supplierName" placeholder="Goodwill, Facebook seller, auction house…" defaultValue={valueFor("supplierName")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="externalReference">Receipt / invoice / order #</Label>
              <Input key={`externalReference:${formKey}`} id="externalReference" name="externalReference" defaultValue={valueFor("externalReference")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="purchasedAt">Purchase date</Label>
              <Input
                key={`purchasedAt:${formKey}`}
                id="purchasedAt"
                name="purchasedAt"
                type="date"
                defaultValue={valueFor("purchasedAt", today())}
                required
                aria-invalid={fieldErrors.purchasedAt ? "true" : undefined}
              />
              {fieldErrors.purchasedAt && <p className="text-destructive text-xs">{fieldErrors.purchasedAt}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="allocationMethod">Allocation method</Label>
              <select
                id="allocationMethod"
                name="allocationMethod"
                value={allocationMethod}
                onChange={(event) => setAllocationMethod(event.target.value as AllocationMethod)}
                disabled={purchaseKind === "Single"}
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
            <NumberField name="purchaseSubtotal" label="Purchase subtotal" value={purchaseSubtotal} onChange={setPurchaseSubtotal} error={fieldErrors.purchaseSubtotal} />
            <NumberField name="shippingCost" label="Inbound shipping" value={shippingCost} onChange={setShippingCost} error={fieldErrors.shippingCost} />
            <NumberField name="taxCost" label="Tax" value={taxCost} onChange={setTaxCost} error={fieldErrors.taxCost} />
            <NumberField name="feeCost" label="Buyer fees" value={feeCost} onChange={setFeeCost} error={fieldErrors.feeCost} />
            <NumberField name="otherCost" label="Other landed cost" value={otherCost} onChange={setOtherCost} error={fieldErrors.otherCost} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">
              {purchaseKind === "Single" ? "Attach purchased item" : "Allocate to inventory"}{" "}
              <span className="text-muted-foreground text-sm font-normal">{purchaseKind === "Lot" ? "(optional)" : "(required)"}</span>
            </CardTitle>
            <CardDescription>
              {purchaseKind === "Single"
                ? "Choose the one inventory item this purchase created. The full landed cost becomes that item’s cost basis."
                : "Attach existing inventory now, or save the lot and itemize new pieces on the lot page. Manual mode requires the selected manual costs to equal landed cost."}
            </CardDescription>
          </CardHeader>
          <CardContent>
            {fieldErrors.inventoryItemId && <p className="text-destructive mb-3 text-sm">{fieldErrors.inventoryItemId}</p>}
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
                              <input type={purchaseKind === "Single" ? "radio" : "checkbox"} name="inventoryItemId" value={item.id} checked={checked} onChange={() => toggle(item.id!)} className="mt-1" />
                              <span>
                                <span className="font-medium">{item.title}</span>
                                <span className="block text-xs text-muted-foreground">{[item.status, item.category, item.storageLocation].filter(Boolean).join(" · ")}</span>
                              </span>
                            </label>
                          </td>
                          <td className="px-3 py-2 text-right tabular-nums">{money(item.purchasePrice)}</td>
                          <td className="px-3 py-2 text-right tabular-nums">{money(item.estimatedSalePrice)}</td>
                          <td className="px-3 py-2">
                            <Input
                              key={`manualCost:${item.id}:${formKey}`}
                              name={`manualCost:${item.id}`}
                              type="number"
                              step="0.01"
                              min="0"
                              disabled={!checked || purchaseKind === "Single" || allocationMethod !== "Manual"}
                              placeholder={allocationMethod === "Equal" ? money(equalAllocation) : ""}
                              defaultValue={valueFor(`manualCost:${item.id}`)}
                              aria-invalid={fieldErrors[`manualCost:${item.id}`] ? "true" : undefined}
                            />
                            {fieldErrors[`manualCost:${item.id}`] && <p className="text-destructive mt-1 text-xs">{fieldErrors[`manualCost:${item.id}`]}</p>}
                          </td>
                          <td className="px-3 py-2">
                            <Input
                              key={`weight:${item.id}:${formKey}`}
                              name={`weight:${item.id}`}
                              type="number"
                              step="0.01"
                              min="0"
                              disabled={!checked || purchaseKind === "Single" || allocationMethod !== "WeightedByExpectedValue"}
                              placeholder={item.estimatedSalePrice != null ? String(item.estimatedSalePrice) : ""}
                              defaultValue={valueFor(`weight:${item.id}`)}
                              aria-invalid={fieldErrors[`weight:${item.id}`] ? "true" : undefined}
                            />
                            {fieldErrors[`weight:${item.id}`] && <p className="text-destructive mt-1 text-xs">{fieldErrors[`weight:${item.id}`]}</p>}
                          </td>
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
          <CardContent><Textarea key={`notes:${formKey}`} name="notes" rows={3} placeholder="Receipt context, payment account, condition exceptions, follow-up work…" defaultValue={valueFor("notes")} /></CardContent>
        </Card>
      </div>

      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card className="p-6">
          <p className="text-sm text-muted-foreground">Total landed cost</p>
          <p className="mt-2 text-4xl font-semibold tabular-nums">{money(landedCost)}</p>
          <div className="mt-5 space-y-2 border-t pt-4 text-sm">
            <div className="flex justify-between"><span className="text-muted-foreground">Selected items</span><span>{selectedItems.length}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Equal cost each</span><span>{selectedItems.length ? money(equalAllocation) : "—"}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Method</span><span>{purchaseKind === "Single" ? "Single item" : allocationMethod}</span></div>
          </div>
        </Card>
        {state.message && <div className={`rounded-lg border p-3 text-sm ${state.ok ? "border-green-200 bg-green-50 text-green-800" : "border-destructive/30 bg-destructive/5 text-destructive"}`}>{state.message}</div>}
        <SubmitButton kind={purchaseKind} />
      </aside>
    </form>
  );
}
