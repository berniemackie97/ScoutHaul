"use client";

import { useActionState, useEffect, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { createSale, type CreateSaleResult } from "./actions";

type Candidate = components["schemas"]["SaleCandidateDto"];
type FeeRule = {
  name?: string | null;
  percent?: number | null;
  fixedAmount?: number | null;
  appliesAboveAmount?: number | null;
  appliesUpToAmount?: number | null;
  minimumFee?: number | null;
  maximumFee?: number | null;
  notes?: string | null;
};
type FeeProfile = {
  id?: string | null;
  label?: string | null;
  description?: string | null;
  isDefault?: boolean | null;
  platformRules?: FeeRule[] | null;
  paymentRules?: FeeRule[] | null;
};
type Channel = components["schemas"]["SaleChannelOptionDto"] & {
  feeProfiles?: FeeProfile[] | null;
};
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

function MoneyField({
  label,
  name,
  value,
  onChange,
  error,
}: {
  label: string;
  name: string;
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

export function SaleCreator({ candidates, channels }: { candidates: Candidate[]; channels: Channel[] }) {
  const [state, action] = useActionState(createSale, initial);
  const values = state.values ?? {};
  const fieldErrors = state.fieldErrors ?? {};
  const formKey = state.formKey ?? "initial";
  const [candidateId, setCandidateId] = useState("");
  const [channel, setChannel] = useState("");
  const [feeProfileId, setFeeProfileId] = useState("");
  const [soldPrice, setSoldPrice] = useState("");
  const [shippingCharged, setShippingCharged] = useState("");
  const [salesTax, setSalesTax] = useState("");
  const [taxRemitted, setTaxRemitted] = useState(true);
  const [platformFees, setPlatformFees] = useState("");
  const [paymentFees, setPaymentFees] = useState("");
  const [shippingCost, setShippingCost] = useState("");
  const [suppliesCost, setSuppliesCost] = useState("");
  const [otherCosts, setOtherCosts] = useState("");

  useEffect(() => {
    if (!state.values) return;

    setCandidateId(state.values.inventoryItemId ?? "");
    setChannel(state.values.platform ?? "");
    setSoldPrice(state.values.soldPrice ?? "");
    setShippingCharged(state.values.shippingCharged ?? "");
    setSalesTax(state.values.salesTaxCollected ?? "");
    setTaxRemitted(state.values.taxRemittedByPlatform !== "off");
    setPlatformFees(state.values.platformFees ?? "");
    setPaymentFees(state.values.paymentFees ?? "");
    setShippingCost(state.values.shippingCost ?? "");
    setSuppliesCost(state.values.suppliesCost ?? "");
    setOtherCosts(state.values.otherCosts ?? "");

    const nextChannel = channels.find((option) => option.value === state.values?.platform);
    const defaultProfile = nextChannel?.feeProfiles?.find((profile: FeeProfile) => profile.isDefault) ?? nextChannel?.feeProfiles?.[0];
    setFeeProfileId(defaultProfile?.id ?? "");
  }, [channels, state.formKey, state.values]);

  const selected = candidates.find((candidate) => candidate.inventoryItemId === candidateId);
  const totalCosts = [platformFees, paymentFees, shippingCost, suppliesCost, otherCosts].reduce(
    (sum, value) => sum + (Number(value) || 0),
    0
  );
  const itemRevenue = Number(soldPrice) || 0;
  const shippingRevenue = Number(shippingCharged) || 0;
  const salesRevenue = itemRevenue + shippingRevenue;
  const marketplaceNet = salesRevenue - totalCosts;
  const trueNet = marketplaceNet - (selected?.costOfGoods ?? 0);
  // Tax we collected but the platform does NOT remit is a liability we owe the state — never part of net.
  const taxOwed = taxRemitted ? 0 : Number(salesTax) || 0;

  const selectedChannel = channels.find((option) => option.value === channel);
  const feeProfiles = selectedChannel?.feeProfiles ?? [];
  const selectedFeeProfile = feeProfiles.find((profile) => profile.id === feeProfileId) ?? feeProfiles.find((profile) => profile.isDefault) ?? feeProfiles[0];
  const salePrice = salesRevenue;
  const round2 = (value: number) => Math.round(value * 100) / 100;
  function estimateRule(rule: FeeRule): number {
    const above = Math.max(0, rule.appliesAboveAmount ?? 0);
    const upTo = rule.appliesUpToAmount ?? salePrice;
    const basis = Math.max(0, Math.min(salePrice, upTo) - above);
    if (basis <= 0 && (rule.percent ?? 0) > 0) return 0;
    let fee = (basis * Math.max(0, rule.percent ?? 0)) / 100 + Math.max(0, rule.fixedAmount ?? 0);
    if (rule.minimumFee != null) fee = Math.max(fee, rule.minimumFee);
    if (rule.maximumFee != null) fee = Math.min(fee, rule.maximumFee);
    return round2(fee);
  }
  const estimateRules = (rules: FeeRule[] | null | undefined) => round2((rules ?? []).reduce((sum, rule) => sum + estimateRule(rule), 0));
  const estPlatformFee = selectedFeeProfile ? estimateRules(selectedFeeProfile.platformRules) : 0;
  const estPaymentFee = selectedFeeProfile ? estimateRules(selectedFeeProfile.paymentRules) : 0;
  const showFeeEstimate = salePrice > 0 && (estPlatformFee > 0 || estPaymentFee > 0);
  function applyFeeEstimate() {
    setPlatformFees(String(estPlatformFee));
    setPaymentFees(String(estPaymentFee));
  }
  function chooseChannel(value: string) {
    setChannel(value);
    const nextChannel = channels.find((option) => option.value === value);
    const defaultProfile = nextChannel?.feeProfiles?.find((profile: FeeProfile) => profile.isDefault) ?? nextChannel?.feeProfiles?.[0];
    setFeeProfileId(defaultProfile?.id ?? "");
  }
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
    if (item.listingPlatform) chooseChannel(item.listingPlatform);
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
                aria-invalid={fieldErrors.inventoryItemId ? "true" : undefined}
              >
                <option value="">Select an eligible item…</option>
                {candidates.map((candidate) => (
                  <option key={candidate.inventoryItemId} value={candidate.inventoryItemId}>
                    {candidate.title} · {candidate.status} · cost {currency(candidate.costOfGoods ?? 0)}
                  </option>
                ))}
              </select>
              {fieldErrors.inventoryItemId && <p className="text-destructive text-xs">{fieldErrors.inventoryItemId}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="platform">Sales channel</Label>
              <select
                id="platform"
                name="platform"
                value={channel}
                onChange={(event) => chooseChannel(event.target.value)}
                className={selectClass}
                required
                aria-invalid={fieldErrors.platform ? "true" : undefined}
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
              {fieldErrors.platform && <p className="text-destructive text-xs">{fieldErrors.platform}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="feeProfile">Fee profile</Label>
              <select
                id="feeProfile"
                value={selectedFeeProfile?.id ?? ""}
                onChange={(event) => setFeeProfileId(event.target.value)}
                className={selectClass}
                disabled={!feeProfiles.length}
              >
                {feeProfiles.length === 0 ? <option value="">Manual fees</option> : feeProfiles.map((profile) => (
                  <option key={profile.id ?? profile.label ?? "profile"} value={profile.id ?? ""}>{profile.label ?? profile.id ?? "Fee profile"}</option>
                ))}
              </select>
              {selectedFeeProfile?.description && <p className="text-muted-foreground text-xs">{selectedFeeProfile.description}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="soldAt">Sold date</Label>
              <Input
                key={`soldAt:${formKey}`}
                id="soldAt"
                name="soldAt"
                type="date"
                defaultValue={values.soldAt ?? localDateValue()}
                required
                aria-invalid={fieldErrors.soldAt ? "true" : undefined}
              />
              {fieldErrors.soldAt && <p className="text-destructive text-xs">{fieldErrors.soldAt}</p>}
            </div>
            <MoneyField label="Sold price" name="soldPrice" value={soldPrice} onChange={setSoldPrice} error={fieldErrors.soldPrice} />
            <MoneyField label="Buyer shipping charged" name="shippingCharged" value={shippingCharged} onChange={setShippingCharged} error={fieldErrors.shippingCharged} />
            <MoneyField label="Sales tax collected" name="salesTaxCollected" value={salesTax} onChange={setSalesTax} error={fieldErrors.salesTaxCollected} />
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
            {showFeeEstimate && (
              <button
                type="button"
                onClick={applyFeeEstimate}
                className="flex items-center justify-between gap-3 rounded-lg border border-dashed border-brand-300 bg-brand-50/40 p-3 text-left text-sm transition hover:bg-brand-50 sm:col-span-2 lg:col-span-3"
              >
                <span className="text-muted-foreground">
                  Estimated {selectedChannel?.label} / {selectedFeeProfile?.label} fees: platform{" "}
                  <span className="text-foreground font-medium">{currency(estPlatformFee)}</span> · payment{" "}
                  <span className="text-foreground font-medium">{currency(estPaymentFee)}</span>
                </span>
                <span className="text-brand-600 shrink-0 font-medium">Apply</span>
              </button>
            )}
            <MoneyField label="Platform fees" name="platformFees" value={platformFees} onChange={setPlatformFees} error={fieldErrors.platformFees} />
            <MoneyField label="Payment fees" name="paymentFees" value={paymentFees} onChange={setPaymentFees} error={fieldErrors.paymentFees} />
            <MoneyField label="Shipping label cost" name="shippingCost" value={shippingCost} onChange={setShippingCost} error={fieldErrors.shippingCost} />
            <MoneyField label="Packing supplies" name="suppliesCost" value={suppliesCost} onChange={setSuppliesCost} error={fieldErrors.suppliesCost} />
            <MoneyField label="Other costs" name="otherCosts" value={otherCosts} onChange={setOtherCosts} error={fieldErrors.otherCosts} />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Internal notes</CardTitle>
          </CardHeader>
          <CardContent>
            <Textarea key={`notes:${formKey}`} name="notes" rows={4} placeholder="Order number, buyer context, payout notes, exceptions…" defaultValue={values.notes ?? ""} />
          </CardContent>
        </Card>
      </div>
      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card className="p-6">
          <p className="text-muted-foreground text-sm">True net profit</p>
          <p className={`mt-2 text-4xl font-semibold tabular-nums ${trueNet < 0 ? "text-destructive" : ""}`}>{currency(trueNet)}</p>
          <div className="mt-5 space-y-2 border-t pt-4 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Item sales</span>
              <span>{currency(itemRevenue)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Shipping charged</span>
              <span>{currency(shippingRevenue)}</span>
            </div>
            <div className="flex justify-between border-t border-dashed pt-2">
              <span className="text-muted-foreground">Marketplace net before COGS</span>
              <span>{currency(marketplaceNet)}</span>
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
