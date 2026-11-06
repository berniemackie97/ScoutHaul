"use client";

import { useActionState, useEffect, useState, useTransition } from "react";
import { useFormStatus } from "react-dom";
import { AlertTriangle, Check, ExternalLink, PackageCheck, Pencil, Truck, X } from "lucide-react";
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from "@overhaul/ui";
import { When } from "@/components/when";
import { daysBetween, trackingHref } from "@/features/orders/order-presentation";
import {
  advanceOrder,
  updateOrderStatus,
  updateOrderTracking,
  type OrderActionResult,
  type OrderStep,
} from "./actions";

export type FulfillmentOrder = {
  id: string;
  channel: string;
  isLocalPickup: boolean;
  paymentStatus?: string | null;
  createdAt?: string | null;
  paidAt?: string | null;
  packedAt?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
  cancelledAt?: string | null;
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
  trackingUrl?: string | null;
  /** When the marketplace or carrier says the parcel arrived, if the order has not recorded it yet. */
  trackingDeliveredAt?: string | null;
  workflow?: { stage?: string | null; availableActions?: string[] | null; blockers?: string[] | null } | null;
};

const CARRIERS = ["USPS", "UPS", "FedEx", "DHL", "OnTrac", "eBay Standard Envelope", "Local delivery"];
const idle: OrderActionResult = { ok: false, message: "" };

export function OrderFulfillment({ order }: { order: FulfillmentOrder }) {
  const stage = order.workflow?.stage ?? "";
  const available = order.workflow?.availableActions ?? [];
  const blockers = order.workflow?.blockers ?? [];
  const marketplace = order.channel !== "Storefront";
  const closed = stage === "Closed";
  const can = (action: string) => available.includes(action);

  const [pending, startTransition] = useTransition();
  const [result, setResult] = useState<OrderActionResult | null>(null);
  const [editing, setEditing] = useState(false);
  const [confirmingDelivery, setConfirmingDelivery] = useState(false);

  // A storefront order cannot ship without tracking, so the form is the next step rather than a side panel.
  const needsTrackingToShip = stage === "ToShip" && !can("Ship") && !marketplace;
  const formOpen = editing || needsTrackingToShip;

  function run(work: () => Promise<OrderActionResult>) {
    setResult(null);
    startTransition(async () => setResult(await work()));
  }
  const step = (next: OrderStep) => run(() => advanceOrder(order.id, next));

  const paid = ["Paid", "PartiallyRefunded", "Refunded"].includes(order.paymentStatus ?? "") || Boolean(order.paidAt);
  const recorded = [
    { label: "Placed", at: order.createdAt, done: true },
    { label: "Paid", at: order.paidAt, done: paid },
    { label: "Packed", at: order.packedAt, done: Boolean(order.packedAt) },
    ...(order.isLocalPickup ? [] : [{ label: "Shipped", at: order.shippedAt, done: Boolean(order.shippedAt) }]),
    { label: order.isLocalPickup ? "Picked up" : "Delivered", at: order.deliveredAt, done: Boolean(order.deliveredAt) },
  ];
  // A later stage implies the earlier ones happened, but their dates stay unknown rather than borrowed.
  const steps = recorded.map((entry, index) => ({
    ...entry,
    done: entry.done || recorded.slice(index + 1).some((later) => later.done),
  }));
  const current = closed ? -1 : steps.findIndex((entry) => !entry.done);

  const href = trackingHref(order.shippingCarrier, order.trackingNumber, order.trackingUrl);
  const transitDays = daysBetween(order.shippedAt, order.deliveredAt);
  const hasShipment = Boolean(order.trackingNumber || order.shippedAt);

  const primary: { label: string; onClick: () => void } | null = can("RecordPayment")
    ? { label: "Mark paid", onClick: () => run(() => updateOrderStatus(order.id, "Paid")) }
    : can("MarkPacked")
      ? { label: "Mark packed", onClick: () => step("markPacked") }
      : can("Ship")
        ? { label: "Mark shipped", onClick: () => step("markShipped") }
        : can("MarkPickedUp")
          ? { label: "Mark picked up", onClick: () => setConfirmingDelivery(true) }
          : can("MarkDelivered")
            ? { label: "Mark delivered", onClick: () => setConfirmingDelivery(true) }
            : null;
  // Marketplace orders are often finished on the marketplace before anyone opens them here.
  const offerDelivered = marketplace && !closed && !order.deliveredAt && primary?.label !== "Mark delivered" && paid;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Truck className="h-4 w-4" aria-hidden /> Fulfillment
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-5">
        <ol className="grid gap-2" style={{ gridTemplateColumns: `repeat(${steps.length}, minmax(0, 1fr))` }}>
          {steps.map((entry, index) => {
            const isCurrent = index === current;
            return (
              <li key={entry.label} className="min-w-0">
                <div
                  className={`h-1.5 rounded-full ${entry.done ? "bg-emerald-500" : isCurrent ? "bg-brand-500" : "bg-muted"}`}
                  aria-hidden
                />
                <p className={`mt-2 flex items-center gap-1 text-xs font-medium ${entry.done || isCurrent ? "text-foreground" : "text-muted-foreground"}`}>
                  {entry.done && <Check className="h-3 w-3 shrink-0 text-emerald-600" aria-hidden />}
                  <span className="truncate">{entry.label}</span>
                </p>
                <p className="text-muted-foreground truncate text-xs">
                  {entry.done ? entry.at ? <When value={entry.at} /> : "Date not recorded" : isCurrent ? "Next" : " "}
                </p>
              </li>
            );
          })}
        </ol>

        {closed && (
          <Notice tone="muted">
            {order.cancelledAt ? (
              <>Cancelled <When value={order.cancelledAt} />. Nothing left to fulfil.</>
            ) : (
              "Refunded in full. Nothing left to fulfil."
            )}
          </Notice>
        )}
        {stage === "NeedsAttention" && (
          <Notice tone="warning">A return is open on this order. Work it from its line under Line items.</Notice>
        )}

        {hasShipment && !formOpen && (
          <div className="flex flex-col gap-3 rounded-lg border p-3 text-sm sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0">
              <p className="font-medium">
                {order.shippingCarrier ?? "Shipped"}
                {order.trackingNumber && (
                  <span className="text-muted-foreground ml-2 font-mono text-xs break-all">{order.trackingNumber}</span>
                )}
              </p>
              <p className="text-muted-foreground text-xs">
                {order.deliveredAt
                  ? transitDays != null && order.shippedAt
                    ? `Delivered ${transitDays === 0 ? "the day it shipped" : `${transitDays} day${transitDays === 1 ? "" : "s"} after shipping`}`
                    : "Delivered"
                  : order.shippedAt
                    ? "In transit"
                    : "Tracking recorded, not shipped yet"}
              </p>
            </div>
            <div className="flex shrink-0 gap-2">
              {href && (
                <Button asChild size="sm" variant="outline">
                  <a href={href} target="_blank" rel="noreferrer">
                    Track <ExternalLink className="ml-1.5 h-3.5 w-3.5" aria-hidden />
                  </a>
                </Button>
              )}
              {!closed && (
                <Button size="sm" variant="ghost" onClick={() => setEditing(true)}>
                  <Pencil className="mr-1.5 h-3.5 w-3.5" aria-hidden /> Edit
                </Button>
              )}
            </div>
          </div>
        )}

        {formOpen && !closed && (
          <ShipmentForm
            order={order}
            shipIntent={needsTrackingToShip}
            onDone={() => setEditing(false)}
            onCancel={editing ? () => setEditing(false) : undefined}
          />
        )}

        {blockers.length > 0 && !needsTrackingToShip && !closed && (
          <Notice tone="warning">
            <p className="font-medium">Before this order can move on</p>
            <ul className="mt-1 list-disc space-y-0.5 pl-5">
              {blockers.map((blocker) => (
                <li key={blocker}>{blocker}</li>
              ))}
            </ul>
          </Notice>
        )}

        {confirmingDelivery && !closed && !formOpen && (
          <DeliveryForm
            order={order}
            pickup={order.isLocalPickup}
            onDone={(done) => {
              setConfirmingDelivery(false);
              setResult(done);
            }}
            onCancel={() => setConfirmingDelivery(false)}
          />
        )}

        {!closed && !formOpen && !confirmingDelivery && (primary || offerDelivered || !hasShipment) && (
          <div className="flex flex-wrap items-center gap-2">
            {primary && (
              <Button disabled={pending} onClick={primary.onClick}>
                <PackageCheck className="mr-1.5 h-4 w-4" aria-hidden />
                {pending ? "Saving…" : primary.label}
              </Button>
            )}
            {offerDelivered && (
              <Button variant="outline" disabled={pending} onClick={() => setConfirmingDelivery(true)}>
                Already delivered
              </Button>
            )}
            {!hasShipment && !order.isLocalPickup && (
              <Button variant="ghost" onClick={() => setEditing(true)}>
                Add tracking
              </Button>
            )}
          </div>
        )}

        {result?.message && (
          <p className={`text-sm ${result.ok ? "text-emerald-700" : "text-destructive"}`} role="status">
            {result.message}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function Notice({ tone, children }: { tone: "warning" | "muted"; children: React.ReactNode }) {
  const styles =
    tone === "warning"
      ? "border-amber-500/30 bg-amber-500/5 text-amber-900 dark:text-amber-200"
      : "bg-muted/40 text-muted-foreground";
  return (
    <div className={`flex gap-2 rounded-lg border p-3 text-sm ${styles}`}>
      {tone === "warning" && <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />}
      <div>{children}</div>
    </div>
  );
}

/** Records delivery when it happened. Earlier stages keep whatever dates they have. */
function DeliveryForm({
  order,
  pickup,
  onDone,
  onCancel,
}: {
  order: FulfillmentOrder;
  pickup: boolean;
  onDone: (result: OrderActionResult) => void;
  onCancel: () => void;
}) {
  const [value, setValue] = useState(() => localInput(order.trackingDeliveredAt ?? new Date().toISOString()));
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  const min = localInput(order.shippedAt ?? order.createdAt);
  const max = localInput(new Date().toISOString());

  function submit(event: React.FormEvent) {
    event.preventDefault();
    const parsed = new Date(value);
    if (!value || Number.isNaN(parsed.getTime())) {
      setError("Enter when it was delivered.");
      return;
    }
    setError(null);
    startTransition(async () => {
      const outcome = await advanceOrder(order.id, "markDelivered", parsed.toISOString());
      if (outcome.ok) onDone(outcome);
      else setError(outcome.message);
    });
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-lg border p-4">
      <div className="flex items-center justify-between">
        <p className="text-sm font-medium">{pickup ? "When was it collected?" : "When was it delivered?"}</p>
        <Button type="button" size="sm" variant="ghost" onClick={onCancel} aria-label="Close">
          <X className="h-4 w-4" />
        </Button>
      </div>
      <Field label={pickup ? "Picked up" : "Delivered"} id="deliveryAt">
        <Input
          id="deliveryAt"
          type="datetime-local"
          value={value}
          min={min || undefined}
          max={max}
          onChange={(event) => setValue(event.target.value)}
          required
        />
      </Field>
      <p className="text-muted-foreground text-xs">
        {order.trackingDeliveredAt
          ? "Filled in from the tracking. Earlier stages keep the dates they already have."
          : "Use the date on the carrier\u2019s tracking if you have it. Earlier stages keep the dates they already have."}
      </p>
      {error && <p className="text-destructive text-sm">{error}</p>}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={pending}>
          {pending ? "Saving…" : pickup ? "Mark picked up" : "Mark delivered"}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={pending}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

function localInput(value: string | null | undefined) {
  if (!value) return "";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "";
  return new Date(parsed.getTime() - parsed.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function ShipmentForm({
  order,
  shipIntent,
  onDone,
  onCancel,
}: {
  order: FulfillmentOrder;
  shipIntent: boolean;
  onDone: () => void;
  onCancel?: () => void;
}) {
  const [state, action] = useActionState(updateOrderTracking.bind(null, order.id), idle);
  useEffect(() => {
    if (state.ok) onDone();
  }, [state, onDone]);

  return (
    <form action={action} className="space-y-4 rounded-lg border p-4">
      <div className="flex items-center justify-between">
        <p className="text-sm font-medium">{shipIntent ? "Add tracking to ship" : "Shipment details"}</p>
        {onCancel && (
          <Button type="button" size="sm" variant="ghost" onClick={onCancel} aria-label="Close">
            <X className="h-4 w-4" />
          </Button>
        )}
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Carrier" id="shippingCarrier">
          <Input id="shippingCarrier" name="shippingCarrier" list="shipping-carriers" defaultValue={order.shippingCarrier ?? ""} placeholder="USPS" autoComplete="off" />
          <datalist id="shipping-carriers">
            {CARRIERS.map((carrier) => (
              <option key={carrier} value={carrier} />
            ))}
          </datalist>
        </Field>
        <Field label="Tracking number" id="trackingNumber">
          <Input id="trackingNumber" name="trackingNumber" defaultValue={order.trackingNumber ?? ""} placeholder="9400…" autoComplete="off" />
        </Field>
        <Field label="Tracking link (optional)" id="trackingUrl" wide>
          <Input id="trackingUrl" name="trackingUrl" defaultValue={order.trackingUrl ?? ""} placeholder="Built from the carrier when left blank" autoComplete="off" />
        </Field>
        {!shipIntent && (
          <>
            <Field label="Shipped" id="shippedAt">
              <Input id="shippedAt" name="shippedAt" type="datetime-local" defaultValue={localInput(order.shippedAt)} />
            </Field>
            <Field label="Delivered" id="deliveredAt">
              <Input id="deliveredAt" name="deliveredAt" type="datetime-local" defaultValue={localInput(order.deliveredAt)} />
            </Field>
          </>
        )}
      </div>
      {state.message && !state.ok && <p className="text-destructive text-sm">{state.message}</p>}
      <FormButtons shipIntent={shipIntent} />
    </form>
  );
}

function Field({ label, id, wide, children }: { label: string; id: string; wide?: boolean; children: React.ReactNode }) {
  return (
    <div className={`space-y-1.5 ${wide ? "sm:col-span-2" : ""}`}>
      <Label htmlFor={id}>{label}</Label>
      {children}
    </div>
  );
}

function FormButtons({ shipIntent }: { shipIntent: boolean }) {
  const { pending } = useFormStatus();
  return (
    <div className="flex flex-wrap gap-2">
      {shipIntent ? (
        <Button type="submit" name="intent" value="markShipped" disabled={pending}>
          {pending ? "Saving…" : "Save and mark shipped"}
        </Button>
      ) : (
        <Button type="submit" name="intent" value="save" disabled={pending}>
          {pending ? "Saving…" : "Save shipment"}
        </Button>
      )}
    </div>
  );
}
