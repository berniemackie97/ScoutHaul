"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { createSale, type CreateSaleResult } from "./actions";

type Candidate = components["schemas"]["SaleCandidateDto"];
type Channel = components["schemas"]["SaleChannelOptionDto"];
const initial: CreateSaleResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring";
const currency = (value: number) => new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value);
const localDateValue = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 10);

function SaveButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending}>
      {pending ? "Recording sale…" : "Record sale"}
    </Button>
  );
}

function MoneyField({ label, name, value, onChange }: { label: string; name: string; value: string; onChange: (value: string) => void }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="number" step="0.01" min="0" value={value} onChange={(event) => onChange(event.target.value)} />
    </div>
  );
}

export function SaleCreator({ candidates, channels }: { candidates: Candidate[]; channels: Channel[] }) {
  const [state, action] = useActionState(createSale, initial);
  const [candidateId, setCandidateId] = useState("");
  const [channel, setChannel] = useState("");
  const [soldPrice, setSoldPrice] = useState("");
  const [salesTax, setSalesTax] = useState("");
  const [taxRemitted, setTaxRemitted] = useState(true);
  const [platformFees, setPlatformFees] = useState("");
  const [paymentFees, setPaymentFees] = useState("");
  const [shippingCost, setShippingCost] = useState("");
  const [suppliesCost, setSuppliesCost] = useState("");
  const [otherCosts, setOtherCosts] = useState("");
  const selected = candidates.find((candidate) => candidate.inventoryItemId === candidateId);
  const totalCosts = [platformFees, paymentFees, shippingCost, suppliesCost, otherCosts].reduce(
    (sum, value) => sum + (Number(value) || 0),
    0
  );
  const trueNet = (Number(soldPrice) || 0) - (selected?.costOfGoods ?? 0) - totalCosts;
  // Tax we collected but the platform does NOT remit is a liability we owe the state — never part of net.
  const taxOwed = taxRemitted ? 0 : Number(salesTax) || 0;
  const groups = useMemo(() => {
    const result = new Map<string, Channel[]>();
    for (const item of channels) {
      const group = item.group ?? "Channels";
      result.set(group, [...(result.get(group) ?? []), item]);
    }
    return result;
  }, [channels]);

  function chooseCandidate(id: string) {
    setCandidateId(id);
    const item = candidates.find((candidate) => candidate.inventoryItemId === id);
    if (!item) return;
    setSoldPrice(String(item.listingPrice ?? item.expectedSalePrice ?? ""));
    if (item.listingPlatform) setChannel(item.listingPlatform);
  }

  return (
    <form action={action} className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
      <input type="hidden" name="listingId" value={selected?.listingId ?? ""} />
      <div className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Sale identity</CardTitle>
            <CardDescription>
              Select the specific inventory record and where it sold. The item’s acquisition cost is snapshotted as COGS.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="inventoryItemId">Inventory item</Label>
              <select
                id="inventoryItemId"
                name="inventoryItemId"
                value={candidateId}
                onChange={(event) => chooseCandidate(event.target.value)}
                className={selectClass}
                required
              >
                <option value="">Select an eligible item…</option>
                {candidates.map((candidate) => (
                  <option key={candidate.inventoryItemId} value={candidate.inventoryItemId}>
                    {candidate.title} · {candidate.status} · cost {currency(candidate.costOfGoods ?? 0)}
                  </option>
                ))}
              </select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="platform">Sales channel</Label>
              <select
                id="platform"
                name="platform"
                value={channel}
                onChange={(event) => setChannel(event.target.value)}
                className={selectClass}
                required
              >
                <option value="">Select channel…</option>
                {[...groups.entries()].map(([group, options]) => (
                  <optgroup key={group} label={group}>
                    {options.flatMap((option) =>
                      option.value
                        ? [
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>,
                          ]
                        : []
                    )}
                  </optgroup>
                ))}
              </select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="soldAt">Sold date</Label>
              <Input id="soldAt" name="soldAt" type="date" defaultValue={localDateValue()} required />
            </div>
            <MoneyField label="Sold price" name="soldPrice" value={soldPrice} onChange={setSoldPrice} />
            <MoneyField label="Sales tax collected" name="salesTaxCollected" value={salesTax} onChange={setSalesTax} />
            <label className="flex items-center gap-2 rounded-lg border p-3 text-sm sm:col-span-2">
              <input
                type="checkbox"
                name="taxRemittedByPlatform"
                checked={taxRemitted}
                onChange={(event) => setTaxRemitted(event.target.checked)}
              />
              <span>
                <strong>Marketplace facilitator remits tax</strong>
                <span className="text-muted-foreground block text-xs">
                  Checked means tax is pass-through, not revenue or your tax liability.
                </span>
              </span>
            </label>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Actual fees and costs</CardTitle>
            <CardDescription>
              Use assessed amounts when known. These values determine true realized profit and remain correctable with an audit reason.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <MoneyField label="Platform fees" name="platformFees" value={platformFees} onChange={setPlatformFees} />
            <MoneyField label="Payment fees" name="paymentFees" value={paymentFees} onChange={setPaymentFees} />
            <MoneyField label="Shipping cost" name="shippingCost" value={shippingCost} onChange={setShippingCost} />
            <MoneyField label="Packing supplies" name="suppliesCost" value={suppliesCost} onChange={setSuppliesCost} />
            <MoneyField label="Other costs" name="otherCosts" value={otherCosts} onChange={setOtherCosts} />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Internal notes</CardTitle>
          </CardHeader>
          <CardContent>
            <Textarea name="notes" rows={4} placeholder="Order number, buyer context, payout notes, exceptions…" />
          </CardContent>
        </Card>
      </div>
      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card className="p-6">
          <p className="text-muted-foreground text-sm">True net profit</p>
          <p className={`mt-2 text-4xl font-semibold tabular-nums ${trueNet < 0 ? "text-destructive" : ""}`}>{currency(trueNet)}</p>
          <div className="mt-5 space-y-2 border-t pt-4 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Revenue</span>
              <span>{currency(Number(soldPrice) || 0)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">COGS</span>
              <span>-{currency(selected?.costOfGoods ?? 0)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Fees/costs</span>
              <span>-{currency(totalCosts)}</span>
            </div>
            {(Number(salesTax) || 0) > 0 && (
              <div className="flex justify-between border-t border-dashed pt-2">
                <span className="text-muted-foreground">Sales tax {taxRemitted ? "(pass-through)" : "(you owe)"}</span>
                <span className={taxOwed > 0 ? "text-amber-700" : "text-muted-foreground"}>{currency(taxRemitted ? Number(salesTax) || 0 : taxOwed)}</span>
              </div>
            )}
          </div>
          {taxOwed > 0 && (
            <p className="text-muted-foreground mt-3 text-xs">
              Tax is excluded from net profit. {currency(taxOwed)} is a liability you owe the state.
            </p>
          )}
        </Card>
        {state.message && (
          <div className="border-destructive/30 bg-destructive/5 text-destructive rounded-lg border p-3 text-sm">{state.message}</div>
        )}
        <SaveButton />
      </aside>
    </form>
  );
}
