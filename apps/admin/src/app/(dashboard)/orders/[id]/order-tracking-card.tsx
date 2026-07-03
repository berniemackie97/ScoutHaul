"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import { Truck } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label } from "@overhaul/ui";
import { updateOrderTracking, type OrderActionResult } from "./actions";

type TrackingOrder = {
  id: string;
  isLocalPickup?: boolean | null;
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
  trackingUrl?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
};

const initial: OrderActionResult = { ok: false, message: "" };
const carrierOptions = ["USPS", "UPS", "FedEx", "DHL", "OnTrac", "eBay Standard Envelope", "Local delivery"];

function localDateTime(value: string | null | undefined) {
  if (!value) return "";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "";
  const local = new Date(parsed.getTime() - parsed.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

function SubmitButtons() {
  const { pending } = useFormStatus();
  return (
    <div className="grid gap-2 sm:grid-cols-3">
      <Button type="submit" name="intent" value="save" variant="outline" disabled={pending}>
        Save tracking
      </Button>
      <Button type="submit" name="intent" value="markShipped" disabled={pending}>
        Mark shipped
      </Button>
      <Button type="submit" name="intent" value="markDelivered" variant="secondary" disabled={pending}>
        Mark delivered
      </Button>
    </div>
  );
}

export function OrderTrackingCard({ order }: { order: TrackingOrder }) {
  const [state, action] = useActionState(updateOrderTracking.bind(null, order.id), initial);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Truck className="h-4 w-4" aria-hidden />
          Package tracking
        </CardTitle>
        <CardDescription>
          Record carrier evidence and advance fulfillment. Customer-facing tracking can reuse this same order record.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="shippingCarrier">Carrier</Label>
              <Input
                id="shippingCarrier"
                name="shippingCarrier"
                list="shipping-carriers"
                defaultValue={order.shippingCarrier ?? ""}
                placeholder="USPS, UPS, FedEx…"
                autoComplete="off"
              />
              <datalist id="shipping-carriers">
                {carrierOptions.map((carrier) => <option key={carrier} value={carrier} />)}
              </datalist>
            </div>

            <div className="space-y-2">
              <Label htmlFor="trackingNumber">Tracking number</Label>
              <Input id="trackingNumber" name="trackingNumber" defaultValue={order.trackingNumber ?? ""} placeholder="9400…" autoComplete="off" />
            </div>

            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="trackingUrl">Tracking URL</Label>
              <Input id="trackingUrl" name="trackingUrl" defaultValue={order.trackingUrl ?? ""} placeholder="https://tools.usps.com/..." autoComplete="off" />
            </div>

            <div className="space-y-2">
              <Label htmlFor="shippedAt">Shipped at</Label>
              <Input id="shippedAt" name="shippedAt" type="datetime-local" defaultValue={localDateTime(order.shippedAt)} />
            </div>

            <div className="space-y-2">
              <Label htmlFor="deliveredAt">Delivered at</Label>
              <Input id="deliveredAt" name="deliveredAt" type="datetime-local" defaultValue={localDateTime(order.deliveredAt)} />
            </div>
          </div>

          {state.message && (
            <div className={`rounded-lg border p-3 text-sm ${state.ok ? "border-green-200 bg-green-50 text-green-800" : "border-destructive/30 bg-destructive/5 text-destructive"}`}>
              {state.message}
            </div>
          )}

          <SubmitButtons />
        </form>
      </CardContent>
    </Card>
  );
}
