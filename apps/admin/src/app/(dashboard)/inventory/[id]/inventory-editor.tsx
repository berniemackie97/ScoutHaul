"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import {
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Input,
  Label,
  Textarea,
} from "@overhaul/ui";
import {
  updateInventoryItem,
  type InventoryActionResult,
} from "./actions";

type InventoryItem = components["schemas"]["InventoryItemDto"];

const initialState: InventoryActionResult = { ok: false, message: "" };

function SubmitButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Saving…" : "Save inventory record"}</Button>;
}

function Field({
  label,
  name,
  defaultValue,
  type = "text",
  placeholder,
}: {
  label: string;
  name: string;
  defaultValue?: string | number | null;
  type?: string;
  placeholder?: string;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input
        id={name}
        name={name}
        type={type}
        step={type === "number" ? "0.01" : undefined}
        min={type === "number" ? "0" : undefined}
        defaultValue={defaultValue ?? ""}
        placeholder={placeholder}
      />
    </div>
  );
}

function NotesField({
  label,
  name,
  defaultValue,
  placeholder,
  rows = 3,
}: {
  label: string;
  name: string;
  defaultValue?: string | null;
  placeholder?: string;
  rows?: number;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Textarea id={name} name={name} defaultValue={defaultValue ?? ""} placeholder={placeholder} rows={rows} />
    </div>
  );
}

export function InventoryEditor({ item }: { item: InventoryItem }) {
  const action = updateInventoryItem.bind(null, item.id!);
  const [state, formAction] = useActionState(action, initialState);

  return (
    <form action={formAction} className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Core record</CardTitle>
          <CardDescription>The canonical identity and merchandising information for this item.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <Field label="Title" name="title" defaultValue={item.title} />
          <NotesField label="Description" name="description" defaultValue={item.description} rows={5} />
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Field label="Category" name="category" defaultValue={item.category} />
            <Field label="Brand" name="brand" defaultValue={item.brand} />
            <Field label="Model" name="model" defaultValue={item.model} />
            <Field label="Condition" name="condition" defaultValue={item.condition} />
            <Field label="Color" name="color" defaultValue={item.color} />
            <Field label="Barcode / SKU" name="barcode" defaultValue={item.barcode} />
            <Field label="Size system" name="sizeSystem" defaultValue={item.sizeSystem} />
            <Field label="Size" name="sizeValue" defaultValue={item.sizeValue} />
            <Field label="Storage location" name="storageLocation" defaultValue={item.storageLocation} placeholder="Aisle, shelf, bin…" />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Cost and valuation</CardTitle>
          <CardDescription>Specific-item cost basis and expected sale value used by budgets and profit reporting.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-3">
          <Field label="Seller asking price" name="sellerAskingPrice" type="number" defaultValue={item.sellerAskingPrice} />
          <Field label="Acquisition cost" name="purchasePrice" type="number" defaultValue={item.purchasePrice} />
          <Field label="Estimated sale price" name="estimatedSalePrice" type="number" defaultValue={item.estimatedSalePrice} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Preparation and listing</CardTitle>
          <CardDescription>Operational notes that travel with the item through intake, preparation, and sale.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-5 lg:grid-cols-2">
          <NotesField label="Accessories included" name="accessories" defaultValue={item.accessories} />
          <NotesField label="Missing parts / disclosed defects" name="missingParts" defaultValue={item.missingParts} />
          <NotesField label="Testing notes" name="testingNotes" defaultValue={item.testingNotes} />
          <NotesField label="Cleaning notes" name="cleaningNotes" defaultValue={item.cleaningNotes} />
          <NotesField label="Listing notes" name="listingNotes" defaultValue={item.listingNotes} />
          <NotesField label="Internal notes" name="notes" defaultValue={item.notes} />
        </CardContent>
      </Card>

      <details className="rounded-xl border bg-card p-6">
        <summary className="cursor-pointer text-sm font-semibold">Advanced structured attributes</summary>
        <p className="mt-2 text-sm text-muted-foreground">Raw structured fields are retained for category-specific specifications and measurements.</p>
        <div className="mt-5 grid gap-5 lg:grid-cols-2">
          <NotesField label="Attributes JSON" name="attributesJson" defaultValue={item.attributesJson} rows={6} />
          <NotesField label="Measurements JSON" name="measurementsJson" defaultValue={item.measurementsJson} rows={6} />
        </div>
      </details>

      <div className="sticky bottom-4 flex items-center justify-between gap-4 rounded-xl border bg-background/95 p-4 shadow-lg backdrop-blur">
        <div aria-live="polite" className={`text-sm ${state.message ? (state.ok ? "text-green-700" : "text-destructive") : "text-muted-foreground"}`}>
          {state.message || "Changes update the shared record used by mobile and storefront workflows."}
        </div>
        <SubmitButton />
      </div>
    </form>
  );
}
