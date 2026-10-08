"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import { ExternalLink, Store } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, CardContent, CardHeader, CardTitle, Input, Label, formatCurrency } from "@overhaul/ui";
import { NativeSelect } from "@/components/native-select";
import { updateStorefrontPolicy, type StorefrontPolicyResult } from "./storefront-policy-actions";

type StoreListing = components["schemas"]["StoreListingDto"];
type StoreListingStatus = components["schemas"]["StoreListingStatus"];
type ShippingPolicy = components["schemas"]["StoreShippingPolicy"];

const initial: StorefrontPolicyResult = { ok: false, message: "" };

const STATUS: Record<StoreListingStatus, { label: string; variant: "success" | "warning" | "info" | "secondary"; says: string }> = {
  Active: { label: "Live", variant: "success", says: "Shoppers can see and buy it." },
  Draft: { label: "Not live yet", variant: "warning", says: "Hidden from shoppers until it is ready — the checklist under Lifecycle stage shows what it still needs." },
  Reserved: { label: "Reserved", variant: "info", says: "Held while a buyer checks out." },
  Sold: { label: "Sold", variant: "secondary", says: "It has sold; its product page says so." },
  Archived: { label: "Off the shelf", variant: "secondary", says: "It left the storefront when it stopped being Listed." },
};

function SaveButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending} className="w-full">{pending ? "Saving…" : "Save offers and shipping"}</Button>;
}

/** Where this item stands on the storefront, a way to see it there, and the offer and shipping rules it sells under. */
export function StorefrontCard({ itemId, listing, storeUrl }: { itemId: string; listing?: StoreListing | null; storeUrl: string | null }) {
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

  const status = listing?.status ? STATUS[listing.status] : null;
  const productUrl = storeUrl && listing?.publicPath && listing.status !== "Archived" ? `${storeUrl}${listing.publicPath}` : null;
  const editable = listing?.id && (listing.status === "Draft" || listing.status === "Active" || listing.status === "Reserved");

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between gap-3 space-y-0">
        <CardTitle className="flex items-center gap-2 text-lg">
          <Store className="h-4 w-4" aria-hidden /> Storefront
        </CardTitle>
        {status && <Badge variant={status.variant}>{status.label}</Badge>}
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-1 text-sm">
          {listing && (
            <p className="font-medium tabular-nums">
              {formatCurrency(listing.listPrice)}
              {listing.status === "Active" && (
                <span className="text-muted-foreground font-normal"> · {listing.viewCount ?? 0} view{listing.viewCount === 1 ? "" : "s"}</span>
              )}
            </p>
          )}
          <p className="text-muted-foreground">
            {status?.says ?? "Not on the storefront. Marking the item Listed puts it on the shelf; it goes live once it has a price and a photo."}
          </p>
          {productUrl && (
            <a href={productUrl} target="_blank" rel="noreferrer" className="text-brand-700 dark:text-brand-300 inline-flex items-center gap-1 font-medium hover:underline">
              View on the store <ExternalLink className="h-3.5 w-3.5" aria-hidden />
            </a>
          )}
        </div>

        {editable && (
          <form action={formAction} className="space-y-4 border-t pt-4">
            <label className="flex items-start gap-3 text-sm">
              <input
                type="checkbox"
                name="acceptsOffers"
                checked={acceptsOffers}
                onChange={(event) => setAcceptsOffers(event.target.checked)}
                className="mt-1"
              />
              <span>
                <span className="block font-medium">Accept buyer offers</span>
                <span className="text-muted-foreground">The product page shows the offer form only when this is on.</span>
              </span>
            </label>

            {acceptsOffers && (
              <div className="grid grid-cols-2 gap-3">
                <MoneyField
                  label="Minimum offer"
                  name="minimumOfferAmount"
                  defaultValue={values?.minimumOfferAmount ?? moneyValue(listing.minimumOfferAmount)}
                  error={state.fieldErrors?.minimumOfferAmount}
                />
                <MoneyField
                  label="Maximum offer"
                  name="maximumOfferAmount"
                  defaultValue={values?.maximumOfferAmount ?? moneyValue(listing.maximumOfferAmount)}
                  error={state.fieldErrors?.maximumOfferAmount}
                />
              </div>
            )}

            <div className="space-y-1">
              <Label htmlFor="shippingPolicy" className="text-xs">Shipping</Label>
              <NativeSelect
                id="shippingPolicy"
                name="shippingPolicy"
                value={shippingPolicy}
                onChange={(event) => setShippingPolicy(event.target.value as ShippingPolicy)}
              >
                <option value="StoreDefault">Store default rate</option>
                <option value="Free">Free shipping</option>
                <option value="FixedAmount">A fixed amount for this item</option>
              </NativeSelect>
              {state.fieldErrors?.shippingPolicy ? <p className="text-destructive text-xs">{state.fieldErrors.shippingPolicy}</p> : null}
            </div>

            {shippingPolicy === "FixedAmount" && (
              <MoneyField
                label="Shipping amount"
                name="shippingAmountOverride"
                defaultValue={values?.shippingAmountOverride ?? moneyValue(listing.shippingAmountOverride)}
                error={state.fieldErrors?.shippingAmountOverride}
              />
            )}

            {state.message ? <p className={`text-xs ${state.ok ? "text-green-700" : "text-destructive"}`}>{state.message}</p> : null}
            <SaveButton />
          </form>
        )}
      </CardContent>
    </Card>
  );
}

function MoneyField({
  label,
  name,
  defaultValue,
  error,
}: {
  label: string;
  name: string;
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
