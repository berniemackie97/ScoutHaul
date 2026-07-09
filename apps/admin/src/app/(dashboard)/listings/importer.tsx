"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Label, Textarea } from "@overhaul/ui";
import { importListings, type ListingActionResult } from "./actions";

const initial: ListingActionResult = { ok: false, message: "" };
const sample = `[
  { "externalListingId": "1234567890", "title": "Vintage Denim Jacket", "price": 45.00, "status": "Active", "quantity": 1, "url": "https://www.ebay.com/itm/1234567890" }
]`;

function SubmitButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Importing…" : "Import listings"}</Button>;
}

export function ListingImporter() {
  const [state, action] = useActionState(importListings, initial);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Import listings</CardTitle>
        <CardDescription>
          Until the extension is connected, paste scraped listing JSON here. Re-importing the same listing id updates
          it in place, never duplicates.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-3">
          <div className="grid gap-2">
            <Label htmlFor="listingsJson">Listing JSON</Label>
            <Textarea id="listingsJson" name="listingsJson" rows={6} placeholder={sample} className="font-mono text-xs" />
          </div>
          <div className="flex items-center justify-between gap-3">
            {state.message && <p className={`text-sm ${state.ok ? "text-emerald-700" : "text-destructive"}`}>{state.message}</p>}
            <SubmitButton />
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
