"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea, formatCurrency } from "@overhaul/ui";
import { recordBundledSale, type BundledResult } from "./actions";

type Candidate = components["schemas"]["SaleCandidateDto"];
type Channel = components["schemas"]["SaleChannelOptionDto"];

const initial: BundledResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring";
const currency = (value: number) => formatCurrency(value);
const localDate = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 10);

function SaveButton({ count }: { count: number }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending || count < 2}>{pending ? "Recording…" : `Record bundled sale (${count})`}</Button>;
}

function Money({ label, name, value, onChange }: { label: string; name: string; value: string; onChange: (v: string) => void }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="number" step="0.01" min="0" value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  );
}

export function BundledSaleCreator({ candidates, channels }: { candidates: Candidate[]; channels: Channel[] }) {
  const [state, action] = useActionState(recordBundledSale, initial);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [manual, setManual] = useState<Record<string, string>>({});
  const [channel, setChannel] = useState("");
  const [total, setTotal] = useState("");
  const [shippingCharged, setShippingCharged] = useState("");
  const [platformFees, setPlatformFees] = useState("");
  const [paymentFees, setPaymentFees] = useState("");
  const [shippingCost, setShippingCost] = useState("");
  const [salesTax, setSalesTax] = useState("");
  const [taxRemitted, setTaxRemitted] = useState(true);

  const groups = useMemo(() => {
    const map = new Map<string, Channel[]>();
    for (const option of channels) if (option.value) map.set(option.group ?? "Channels", [...(map.get(option.group ?? "Channels") ?? []), option]);
    return map;
  }, [channels]);

  const chosen = candidates.filter((c) => c.inventoryItemId && selected.has(c.inventoryItemId));
  const cogs = chosen.reduce((sum, c) => sum + (c.costOfGoods ?? 0), 0);
  const feesTotal = [platformFees, paymentFees, shippingCost].reduce((sum, v) => sum + (Number(v) || 0), 0);
  const revenue = (Number(total) || 0) + (Number(shippingCharged) || 0);
  const marketplaceNet = revenue - feesTotal;
  const groupNet = marketplaceNet - cogs;

  const itemsJson = JSON.stringify(
    chosen.map((c) => ({ inventoryItemId: c.inventoryItemId, manualPrice: manual[c.inventoryItemId!] ? Number(manual[c.inventoryItemId!]) : null }))
  );

  function toggle(id: string) {
    setSelected((cur) => {
      const next = new Set(cur);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <form action={action} className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
      <input type="hidden" name="itemsJson" value={itemsJson} />
      <div className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Items in this order</CardTitle>
            <CardDescription>Pick every item that shipped together as one package. Leave per-item price blank to split the total by expected value, or set exact amounts.</CardDescription>
          </CardHeader>
          <CardContent className="p-0">
            <div className="max-h-[420px] overflow-auto">
              <table className="w-full text-sm">
                <thead className="bg-muted/40 text-muted-foreground border-y sticky top-0 text-left text-xs uppercase tracking-wide">
                  <tr><th className="px-3 py-2 font-medium">Item</th><th className="px-3 py-2 text-right font-medium">COGS</th><th className="px-3 py-2 font-medium">Price (optional)</th></tr>
                </thead>
                <tbody className="divide-y">
                  {candidates.map((c) => {
                    if (!c.inventoryItemId) return null;
                    const id = c.inventoryItemId;
                    const checked = selected.has(id);
                    return (
                      <tr key={id} className={checked ? "bg-brand-50/40" : undefined}>
                        <td className="px-3 py-2">
                          <label className="flex items-start gap-2">
                            <input type="checkbox" checked={checked} onChange={() => toggle(id)} className="mt-1" />
                            <span><span className="font-medium">{c.title}</span><span className="text-muted-foreground block text-xs">{c.status}</span></span>
                          </label>
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">{currency(c.costOfGoods ?? 0)}</td>
                        <td className="px-3 py-2">
                          <Input type="number" step="0.01" min="0" disabled={!checked} value={manual[id] ?? ""} onChange={(e) => setManual((m) => ({ ...m, [id]: e.target.value }))} placeholder="auto" className="h-8" />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-lg">Order totals</CardTitle><CardDescription>One total, one set of fees, one tracking — split across the items automatically.</CardDescription></CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="platform">Sales channel</Label>
              <select id="platform" name="platform" value={channel} onChange={(e) => setChannel(e.target.value)} required className={selectClass}>
                <option value="">Select channel…</option>
                {[...groups.entries()].map(([group, options]) => (
                  <optgroup key={group} label={group}>{options.map((o) => <option key={o.value} value={o.value ?? ""}>{o.label}</option>)}</optgroup>
                ))}
              </select>
            </div>
            <div className="space-y-2"><Label htmlFor="orderReference">Order / tracking reference</Label><Input id="orderReference" name="orderReference" placeholder="eBay order #" /></div>
            <Money label="Order total (all items)" name="totalSoldPrice" value={total} onChange={setTotal} />
            <Money label="Buyer shipping charged" name="shippingCharged" value={shippingCharged} onChange={setShippingCharged} />
            <div className="space-y-2"><Label htmlFor="soldAt">Sold date</Label><Input id="soldAt" name="soldAt" type="date" defaultValue={localDate()} /></div>
            <Money label="Platform fees" name="platformFees" value={platformFees} onChange={setPlatformFees} />
            <Money label="Payment fees" name="paymentFees" value={paymentFees} onChange={setPaymentFees} />
            <Money label="Shipping label cost" name="shippingCost" value={shippingCost} onChange={setShippingCost} />
            <Money label="Sales tax" name="salesTaxCollected" value={salesTax} onChange={setSalesTax} />
            <input type="hidden" name="suppliesCost" value="0" />
            <input type="hidden" name="otherCosts" value="0" />
            <label className="flex items-center gap-2 text-sm sm:col-span-2"><input type="checkbox" name="taxRemittedByPlatform" checked={taxRemitted} onChange={(e) => setTaxRemitted(e.target.checked)} /> Marketplace remits the sales tax (pass-through)</label>
            <div className="space-y-2 sm:col-span-2"><Label htmlFor="notes">Notes</Label><Textarea id="notes" name="notes" rows={2} /></div>
          </CardContent>
        </Card>
      </div>

      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card className="p-6">
          <p className="text-muted-foreground text-sm">Group true net</p>
          <p className={`mt-2 text-4xl font-semibold tabular-nums ${groupNet < 0 ? "text-destructive" : ""}`}>{currency(groupNet)}</p>
          <div className="mt-5 space-y-2 border-t pt-4 text-sm">
            <div className="flex justify-between"><span className="text-muted-foreground">Items</span><span>{chosen.length}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Order total</span><span>{currency(Number(total) || 0)}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Shipping charged</span><span>{currency(Number(shippingCharged) || 0)}</span></div>
            <div className="flex justify-between border-t border-dashed pt-2"><span className="text-muted-foreground">Marketplace net before COGS</span><span>{currency(marketplaceNet)}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">COGS</span><span>-{currency(cogs)}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Fees</span><span>-{currency(feesTotal)}</span></div>
          </div>
          <p className="text-muted-foreground mt-3 text-xs">The total and fees are split across items by expected value (or your per-item prices). Each item keeps its own net.</p>
        </Card>
        {state.message && <div className="border-destructive/30 bg-destructive/5 text-destructive rounded-lg border p-3 text-sm">{state.message}</div>}
        <SaveButton count={chosen.length} />
      </aside>
    </form>
  );
}
