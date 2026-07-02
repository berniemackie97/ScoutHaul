"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { createInventoryItem, type NewInventoryResult } from "./actions";

const initial: NewInventoryResult = { ok: false, message: "" };
const selectClass =
  "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring";

const ownershipOptions = [
  ["Owned", "Owned inventory"],
  ["Consigned", "Consigned inventory"],
  ["CustomerRepair", "Customer-owned repair"],
  ["VendorManaged", "Vendor-managed inventory"],
] as const;
const functionalOptions = [
  ["Unknown", "Unknown"],
  ["Untested", "Untested"],
  ["FullyFunctional", "Fully functional"],
  ["FunctionalWithIssues", "Functional with issues"],
  ["Intermittent", "Intermittent"],
  ["NonFunctional", "Non-functional"],
  ["PartsOnly", "Parts only"],
] as const;

function SubmitButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Creating…" : "Create item"}</Button>;
}

function Field({ label, name, type = "text", placeholder, step, min }: { label: string; name: string; type?: string; placeholder?: string; step?: string; min?: string }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type={type} placeholder={placeholder} step={step} min={min} />
    </div>
  );
}

function SelectField({ label, name, options }: { label: string; name: string; options: readonly (readonly [string, string])[] }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <select id={name} name={name} className={selectClass} defaultValue={options[0]?.[0]}>
        {options.map(([value, text]) => (
          <option key={value} value={value}>
            {text}
          </option>
        ))}
      </select>
    </div>
  );
}

export function ItemCreator() {
  const [state, action] = useActionState(createInventoryItem, initial);

  return (
    <form action={action} className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
      <div className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Item identity</CardTitle>
            <CardDescription>What the item is. Only a title is required — everything else can be refined after it exists.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="title">Title</Label>
              <Input id="title" name="title" required placeholder="e.g. Vintage Pyrex mixing bowl, spring blossom" />
            </div>
            <Field label="Category" name="category" placeholder="Kitchenware" />
            <Field label="Brand" name="brand" placeholder="Pyrex" />
            <Field label="Model" name="model" />
            <Field label="Color" name="color" />
            <Field label="Condition" name="condition" placeholder="Good, light wear" />
            <Field label="Storage location" name="storageLocation" placeholder="Bin A3" />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Classification</CardTitle>
            <CardDescription>Ownership and functional state drive consignment, repair, and listing routing.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Ownership" name="ownershipType" options={ownershipOptions} />
            <SelectField label="Functional status" name="functionalStatus" options={functionalOptions} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Cost and valuation</CardTitle>
            <CardDescription>Single-item purchase cost becomes this item&rsquo;s cost basis for profit reporting.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-3">
            <Field label="Purchase price" name="purchasePrice" type="number" step="0.01" min="0" />
            <Field label="Seller asking price" name="sellerAskingPrice" type="number" step="0.01" min="0" />
            <Field label="Estimated sale price" name="estimatedSalePrice" type="number" step="0.01" min="0" />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Details and notes</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <Field label="Barcode / UPC" name="barcode" />
            <Field label="Serial number" name="serialNumber" />
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="description">Description</Label>
              <Textarea id="description" name="description" rows={3} placeholder="Condition detail, provenance, measurements…" />
            </div>
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="notes">Internal notes</Label>
              <Textarea id="notes" name="notes" rows={2} placeholder="Where/how acquired, follow-up work…" />
            </div>
          </CardContent>
        </Card>
      </div>

      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card className="p-6 text-sm">
          <p className="font-medium">Add a single item</p>
          <p className="text-muted-foreground mt-2">
            Use this for a one-off buy. For a receipt/pallet/haul with many pieces, record a{" "}
            <a href="/purchases" className="text-brand-700 hover:underline">purchase lot</a> and itemize it instead.
          </p>
          <p className="text-muted-foreground mt-3">After creating, you&rsquo;ll land on the item to add photos, generate a listing, and set its stage.</p>
        </Card>
        {state.message && (
          <div className="border-destructive/30 bg-destructive/5 text-destructive rounded-lg border p-3 text-sm">{state.message}</div>
        )}
        <SubmitButton />
      </aside>
    </form>
  );
}
