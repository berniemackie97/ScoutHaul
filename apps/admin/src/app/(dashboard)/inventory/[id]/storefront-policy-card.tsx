"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import { Store } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from "@overhaul/ui";
import { updateStorefrontPolicy, type StorefrontPolicyResult } from "./storefront-policy-actions";

type StoreListing = components["schemas"]["StoreListingDto"];
type ShippingPolicy = components["schemas"]["StoreShippingPolicy"];

const initial: StorefrontPolicyResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring";

function SaveButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending} className="w-full">{pending ? "Saving…" : "Save storefront policy"}</Button>;
}

export function StorefrontPolicyCard({ itemId, listing }: { itemId: string; listing?: StoreListing | null }) {
  const action = useMemo(
    () => listing?.id ? updateStorefrontPolicy.bind(null, itemId, listing.id) : null,
    [itemId, listing?.id],
  );
  const [state, formAction] = useActionState(action ?? inertAction, initial);
  const values = state.values;
  const [acceptsOffers, setAcceptsOffers] = useState(
    values?.acceptsOffers ? values.acceptsOffers === "on" : listing?.acceptsOffers !== false,
  );
  const [shippingPolicy, setShippingPolicy] = useState<ShippingPolicy>(
    (values?.shippingPolicy as ShippingPolicy | undefined) ?? listing?.shippingPolicy ?? "StoreDefault",
  );

  if (!listing?.id) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg">
            <Store className="h-4 w-4" /> Storefront policy
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-2 text-sm text-muted-foreground">
          <p>This item does not have a storefront listing yet.</p>
          <p>Publish it to the storefront, then set offer and shipping rules here.</p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Store className="h-4 w-4" /> Storefront policy
        </CardTitle>
      </CardHeader>
      <CardContent>
        <form action={formAction} className="space-y-4">
          <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <input
              type="checkbox"
              name="acceptsOffers"
              checked={acceptsOffers}
              onChange={(event) => setAcceptsOffers(event.target.checked)}
              className="mt-1"
            />
            <span>
              <span className="block font-medium">Accept buyer offers</span>
              <span className="text-muted-foreground">The public product page shows the offer form only when this is enabled.</span>
            </span>
          </label>

          <div className="grid grid-cols-2 gap-3">
            <MoneyField
              label="Minimum offer"
              name="minimumOfferAmount"
              disabled={!acceptsOffers}
              defaultValue={values?.minimumOfferAmount ?? moneyValue(listing.minimumOfferAmount)}
              error={state.fieldErrors?.minimumOfferAmount}
            />
            <MoneyField
              label="Maximum offer"
              name="maximumOfferAmount"
              disabled={!acceptsOffers}
              defaultValue={values?.maximumOfferAmount ?? moneyValue(listing.maximumOfferAmount)}
              error={state.fieldErrors?.maximumOfferAmount}
            />
          </div>

          <div className="space-y-1">
            <Label htmlFor="shippingPolicy" className="text-xs">Shipping policy</Label>
            <select
              id="shippingPolicy"
              name="shippingPolicy"
              value={shippingPolicy}
              onChange={(event) => setShippingPolicy(event.target.value as ShippingPolicy)}
              className={selectClass}
            >
              <option value="StoreDefault">Store default rate</option>
              <option value="Free">Free shipping for this item</option>
              <option value="FixedAmount">Fixed/reduced shipping for this item</option>
            </select>
            {state.fieldErrors?.shippingPolicy ? <p className="text-xs text-destructive">{state.fieldErrors.shippingPolicy}</p> : null}
          </div>

          <MoneyField
            label="Fixed shipping amount"
            name="shippingAmountOverride"
            disabled={shippingPolicy !== "FixedAmount"}
            defaultValue={values?.shippingAmountOverride ?? moneyValue(listing.shippingAmountOverride)}
            error={state.fieldErrors?.shippingAmountOverride}
          />

          {state.message ? <p className={`text-xs ${state.ok ? "text-green-700" : "text-destructive"}`}>{state.message}</p> : null}
          <SaveButton />
        </form>
      </CardContent>
    </Card>
  );
}

function MoneyField({
  label,
  name,
  disabled,
  defaultValue,
  error,
}: {
  label: string;
  name: string;
  disabled?: boolean;
  defaultValue?: string;
  error?: string;
}) {
  return (
    <div className="space-y-1">
      <Label htmlFor={name} className="text-xs">{label}</Label>
      <Input
        id={name}
        name={name}
        type="number"
        step="0.01"
        min="0"
        disabled={disabled}
        defaultValue={defaultValue}
        className="h-9"
      />
      {error ? <p className="text-xs text-destructive">{error}</p> : null}
    </div>
  );
}

async function inertAction(
  _previous: StorefrontPolicyResult,
  _formData: FormData,
): Promise<StorefrontPolicyResult> {
  return { ok: false, message: "This item is not published to the storefront." };
}

function moneyValue(value: number | null | undefined): string | undefined {
  return typeof value === "number" ? value.toFixed(2) : undefined;
}
