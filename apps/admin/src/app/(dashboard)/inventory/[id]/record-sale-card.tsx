"use client";

import { useMemo, useState } from "react";
import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import { CircleDollarSign } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label, formatCurrency } from "@overhaul/ui";
import { recordSaleForItem, type SellResult } from "./sell-actions";
import { NativeSelect } from "@/components/native-select";

type Channel = components["schemas"]["SaleChannelOptionDto"];

const initial: SellResult = { ok: false, message: "" };
const currency = (value: number) => formatCurrency(value);
const today = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 10);

function SaveButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending} className="w-full">{pending ? "Recording…" : "Record sale"}</Button>;
}

function Money({ label, name, value, onChange }: { label: string; name: string; value: string; onChange: (value: string) => void }) {
  return (
    <div className="space-y-1">
      <Label htmlFor={name} className="text-xs">{label}</Label>
      <Input id={name} name={name} type="number" step="0.01" min="0" value={value} onChange={(event) => onChange(event.target.value)} className="h-9" />
    </div>
  );
}

// `prefill` arrives from a marketplace listing that sold: the card opens knowing everything but the fees.
export function RecordSaleCard({
  itemId,
  cost,
  channels,
  prefill,
}: {
  itemId: string;
  cost: number;
  channels: Channel[];
  prefill?: { platform?: string; price?: string; soldOn?: string };
}) {
  const [state, action] = useActionState(recordSaleForItem.bind(null, itemId), initial);
  const [open, setOpen] = useState(prefill !== undefined);
  const [soldPrice, setSoldPrice] = useState(prefill?.price ?? "");
  const [shippingCharged, setShippingCharged] = useState("");
  const [platformFees, setPlatformFees] = useState("");
  const [paymentFees, setPaymentFees] = useState("");
  const [shipping, setShipping] = useState("");
  const [tax, setTax] = useState("");
  const [taxRemitted, setTaxRemitted] = useState(true);

  // The listing's platform is already a channel value, but match it loosely so odd casing still lands on the channel.
  const prefilledChannel = useMemo(
    () => channels.find((option) => option.value?.toLowerCase() === prefill?.platform?.toLowerCase())?.value ?? "",
    [channels, prefill?.platform],
  );

  const groups = useMemo(() => {
    const map = new Map<string, Channel[]>();
    for (const option of channels) {
      if (!option.value) continue;
      map.set(option.group ?? "Channels", [...(map.get(option.group ?? "Channels") ?? []), option]);
    }
    return map;
  }, [channels]);

  const net = (Number(soldPrice) || 0) + (Number(shippingCharged) || 0) - cost - [platformFees, paymentFees, shipping].reduce((sum, value) => sum + (Number(value) || 0), 0);

  if (!open) {
    return (
      <Card>
        <CardContent className="p-4">
          <button type="button" onClick={() => setOpen(true)} className="text-brand-700 flex w-full items-center justify-center gap-2 text-sm font-medium">
            <CircleDollarSign className="h-4 w-4" /> Record a sale for this item
          </button>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-lg">
          <CircleDollarSign className="h-4 w-4" /> Record sale
        </CardTitle>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-3">
          <div className="space-y-1">
            <Label htmlFor="platform" className="text-xs">Channel</Label>
            <NativeSelect id="platform" name="platform" required defaultValue={prefilledChannel}>
              <option value="">Select channel…</option>
              {[...groups.entries()].map(([group, options]) => (
                <optgroup key={group} label={group}>
                  {options.map((option) => <option key={option.value} value={option.value ?? ""}>{option.label}</option>)}
                </optgroup>
              ))}
            </NativeSelect>
          </div>
          <div className="grid grid-cols-2 gap-2">
            <Money label="Sold price" name="soldPrice" value={soldPrice} onChange={setSoldPrice} />
            <Money label="Buyer shipping charged" name="shippingCharged" value={shippingCharged} onChange={setShippingCharged} />
            <div className="space-y-1">
              <Label htmlFor="soldAt" className="text-xs">Sold date</Label>
              <Input id="soldAt" name="soldAt" type="date" defaultValue={prefill?.soldOn ?? today()} className="h-9" />
            </div>
            <Money label="Platform fees" name="platformFees" value={platformFees} onChange={setPlatformFees} />
            <Money label="Payment fees" name="paymentFees" value={paymentFees} onChange={setPaymentFees} />
            <Money label="Shipping label cost" name="shippingCost" value={shipping} onChange={setShipping} />
            <Money label="Sales tax" name="salesTaxCollected" value={tax} onChange={setTax} />
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div className="space-y-1">
              <Label htmlFor="externalOrderId" className="text-xs">Order number</Label>
              <Input id="externalOrderId" name="externalOrderId" className="h-9" placeholder="Marketplace order #" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="buyerReference" className="text-xs">Buyer username</Label>
              <Input id="buyerReference" name="buyerReference" className="h-9" placeholder="Buyer handle" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="shippingCarrier" className="text-xs">Carrier</Label>
              <Input id="shippingCarrier" name="shippingCarrier" className="h-9" placeholder="USPS" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="trackingNumber" className="text-xs">Tracking number</Label>
              <Input id="trackingNumber" name="trackingNumber" className="h-9" placeholder="Groups same-package sales" />
            </div>
          </div>
          <input type="hidden" name="suppliesCost" value="0" />
          <input type="hidden" name="otherCosts" value="0" />
          <label className="flex items-center gap-2 text-xs">
            <input type="checkbox" name="taxRemittedByPlatform" checked={taxRemitted} onChange={(event) => setTaxRemitted(event.target.checked)} />
            Marketplace remits the sales tax (pass-through)
          </label>

          <div className="flex justify-between border-t pt-2 text-sm">
            <span className="text-muted-foreground">True net (after COGS {currency(cost)})</span>
            <span className={`font-semibold tabular-nums ${net < 0 ? "text-destructive" : ""}`}>{currency(net)}</span>
          </div>

          {state.message && <p className={`text-xs ${state.ok ? "text-green-700" : "text-destructive"}`}>{state.message}</p>}
          <div className="flex gap-2">
            <SaveButton />
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>Cancel</Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
