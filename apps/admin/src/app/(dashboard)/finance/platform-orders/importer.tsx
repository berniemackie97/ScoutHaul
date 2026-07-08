"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Label, Textarea } from "@overhaul/ui";
import { importPlatformOrders, type ActionResult } from "./actions";

type Channel = components["schemas"]["SaleChannelOptionDto"];

const initial: ActionResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring";

const sample = `[
  {
    "externalOrderId": "12-34567-89012",
    "orderedAt": "2026-07-01T15:04:00Z",
    "itemsSubtotal": 120.00,
    "shippingCharged": 8.50,
    "taxCollected": 9.90,
    "feeTotal": 18.40,
    "payoutAmount": 110.10,
    "lines": [{ "title": "Vintage Denim Jacket", "unitPrice": 120.00, "quantity": 1, "externalItemId": "1234567890" }]
  }
]`;

function ImportButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Importing…" : "Import orders"}</Button>;
}

export function ManualImporter({ channels }: { channels: Channel[] }) {
  const [state, action] = useActionState(importPlatformOrders, initial);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Import orders manually</CardTitle>
        <CardDescription>
          A fallback for before the browser extension is connected: paste the scraped order JSON from a marketplace.
          Re-importing the same order id updates it in place — never a duplicate.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-4">
          <div className="grid gap-2 sm:max-w-xs">
            <Label htmlFor="platform">Platform</Label>
            <select id="platform" name="platform" required className={selectClass} defaultValue="">
              <option value="">Select platform…</option>
              {channels
                .filter((channel) => channel.value)
                .map((channel) => (
                  <option key={channel.value} value={channel.value ?? ""}>
                    {channel.label}
                  </option>
                ))}
            </select>
          </div>
          <div className="grid gap-2">
            <Label htmlFor="ordersJson">Order JSON</Label>
            <Textarea id="ordersJson" name="ordersJson" rows={8} placeholder={sample} className="font-mono text-xs" />
          </div>
          <div className="flex items-center justify-between gap-3">
            {state.message ? (
              <p className={`text-sm ${state.ok ? "text-emerald-700" : "text-destructive"}`}>{state.message}</p>
            ) : (
              <span />
            )}
            <ImportButton />
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
