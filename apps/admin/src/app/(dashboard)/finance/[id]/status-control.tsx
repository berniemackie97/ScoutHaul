"use client";

import { useActionState, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { transitionSale, type SaleActionResult } from "./actions";

type SaleDto = components["schemas"]["SaleDto"];
type Shipment = components["schemas"]["SaleShipmentDto"];
const CARRIERS = ["USPS", "UPS", "FedEx", "DHL", "eBay Labels", "Other"];
const initial: SaleActionResult = { ok: false, message: "" };
const labels: Record<string, string> = {
  Recorded: "Recorded / sold",
  Packed: "Packed",
  Shipped: "Shipped",
  Delivered: "Delivered",
  ReturnRequested: "Return requested",
  Returned: "Return received",
  Refunded: "Refunded",
  Cancelled: "Cancelled",
};

function Submit() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending}>
      {pending ? "Updating…" : "Apply transition"}
    </Button>
  );
}

export function SaleStatusControl({ sale, shipment }: { sale: SaleDto; shipment?: Shipment | null }) {
  const [state, action] = useActionState(transitionSale.bind(null, sale.id!), initial);
  const hasCarrier = Boolean(shipment?.carrier && CARRIERS.includes(shipment.carrier));
  const transitions = sale.allowedTransitions ?? [];
  const [selected, setSelected] = useState(transitions[0] ?? "");
  const reasonRequired =
    ["Cancelled", "ReturnRequested", "Returned", "Refunded"].includes(selected) ||
    (sale.status === "Packed" && selected === "Recorded") ||
    (sale.status === "ReturnRequested" && selected === "Delivered");
  if (transitions.length === 0)
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Fulfillment lifecycle</CardTitle>
          <CardDescription>This sale is in terminal state {labels[sale.status ?? ""] ?? sale.status}.</CardDescription>
        </CardHeader>
      </Card>
    );
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Advance fulfillment</CardTitle>
        <CardDescription>Only legal next states are available. Exceptions and reversals require an audit reason.</CardDescription>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="status">Next status</Label>
            <select
              id="status"
              name="status"
              value={selected}
              onChange={(event) => setSelected(event.target.value)}
              className="border-input bg-background h-10 w-full rounded-lg border px-3 text-sm"
            >
              {transitions.map((status) => (
                <option key={status} value={status}>
                  {labels[status]}
                </option>
              ))}
            </select>
          </div>
          <div className="space-y-2">
            <Label htmlFor="occurredAt">Occurred at</Label>
            <Input
              id="occurredAt"
              name="occurredAt"
              type="datetime-local"
              defaultValue={new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 16)}
            />
          </div>
          {selected === "Shipped" && (
            <div className="space-y-2 rounded-lg border bg-muted/20 p-3">
              <div className="grid gap-3 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="carrier">Carrier</Label>
                  <select
                    id="carrier"
                    name="carrier"
                    defaultValue={hasCarrier ? shipment!.carrier! : "USPS"}
                    className="border-input bg-background h-10 w-full rounded-lg border px-3 text-sm"
                  >
                    {CARRIERS.map((carrier) => (
                      <option key={carrier} value={carrier}>{carrier}</option>
                    ))}
                  </select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="trackingNumber">Tracking number</Label>
                  <Input id="trackingNumber" name="trackingNumber" defaultValue={shipment?.trackingNumber ?? ""} placeholder="Paste tracking…" />
                </div>
              </div>
              {shipment?.trackingNumber && (
                <p className="text-muted-foreground text-xs">On file from the shipping step — pre-filled. Only change it if the shipment changed.</p>
              )}
            </div>
          )}
          <div className="space-y-2">
            <Label htmlFor="reason">Reason {reasonRequired ? "(required)" : "(optional)"}</Label>
            <Textarea id="reason" name="reason" rows={3} required={reasonRequired} placeholder="Operational context for the audit trail…" />
          </div>
          {state.message && <p className={`text-sm ${state.ok ? "text-green-700" : "text-destructive"}`}>{state.message}</p>}
          <Submit />
        </form>
      </CardContent>
    </Card>
  );
}
