"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Label, Textarea } from "@overhaul/ui";
import { importEbayCsv, importPlatformOrders, type ActionResult } from "./actions";

type Channel = components["schemas"]["SaleChannelOptionDto"];

const initial: ActionResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring";

const jsonSample = `[
  {
    "externalOrderId": "12-34567-89012",
    "orderedAt": "2026-07-01T15:04:00Z",
    "itemsSubtotal": 120.00,
    "shippingCharged": 8.50,
    "taxCollected": 9.90,
    "feeTotal": 18.40,
    "shippingCost": 5.97,
    "lines": [{ "title": "Vintage Denim Jacket", "unitPrice": 120.00, "quantity": 1, "externalItemId": "1234567890" }]
  }
]`;

function SubmitButton({ idle, busy }: { idle: string; busy: string }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? busy : idle}</Button>;
}

function Result({ state }: { state: ActionResult }) {
  if (!state.message) return null;
  return <p className={`text-sm ${state.ok ? "text-emerald-700" : "text-destructive"}`}>{state.message}</p>;
}

export function ManualImporter({ channels }: { channels: Channel[] }) {
  const [csvState, csvAction] = useActionState(importEbayCsv, initial);
  const [jsonState, jsonAction] = useActionState(importPlatformOrders, initial);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Import from an eBay report</CardTitle>
        <CardDescription>
          In eBay: Seller Hub → Performance → Sales → <strong>Download</strong>. Upload that CSV here — it carries
          eBay&apos;s exact fees per item. Re-uploading updates in place, never duplicates.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <form action={csvAction} className="space-y-3">
          <div className="grid gap-2">
            <Label htmlFor="csvFile">eBay sales report (.csv)</Label>
            <input
              id="csvFile"
              name="csvFile"
              type="file"
              accept=".csv,text/csv"
              className="file:bg-muted file:text-foreground text-sm file:mr-3 file:rounded-md file:border-0 file:px-3 file:py-1.5 file:text-sm"
            />
          </div>
          <div className="flex items-center justify-between gap-3">
            <Result state={csvState} />
            <SubmitButton idle="Import eBay report" busy="Importing…" />
          </div>
        </form>

        <details className="border-t pt-3">
          <summary className="text-muted-foreground cursor-pointer text-xs">Advanced: paste order JSON (for the extension format)</summary>
          <form action={jsonAction} className="mt-3 space-y-3">
            <div className="grid gap-2 sm:max-w-xs">
              <Label htmlFor="platform">Platform</Label>
              <select id="platform" name="platform" required className={selectClass} defaultValue="eBay">
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
              <Textarea id="ordersJson" name="ordersJson" rows={7} placeholder={jsonSample} className="font-mono text-xs" />
            </div>
            <div className="flex items-center justify-between gap-3">
              <Result state={jsonState} />
              <SubmitButton idle="Import JSON" busy="Importing…" />
            </div>
          </form>
        </details>
      </CardContent>
    </Card>
  );
}
