"use client";

import { useActionState, useEffect, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { hasField, profileFor } from "@/features/inventory/item-profiles";
import { matchOption, NotesField, OptionField, SelectField, TextField } from "@/features/inventory/item-form/controls";
import { ConditionField } from "@/features/inventory/item-form/condition-field";
import { ItemDetails } from "@/features/inventory/item-form/item-details";
import { MeasurementsField } from "@/features/inventory/item-form/measurements-field";
import { DIMENSION_UNITS, OWNERSHIP, WEIGHT_UNITS } from "@/features/inventory/item-form/options";
import { updateInventoryItem, type InventoryActionResult } from "./actions";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];

const initialState: InventoryActionResult = { ok: false, message: "" };

function SubmitButton({ unchanged }: { unchanged: boolean }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending || unchanged}>{pending ? "Saving…" : "Save changes"}</Button>;
}

export function InventoryEditor({ item, referenceData }: { item: InventoryItem; referenceData: ReferenceData }) {
  const [state, formAction] = useActionState(updateInventoryItem.bind(null, item.id!), initialState);
  const values = state.values;
  const errors = state.fieldErrors ?? {};
  const formKey = state.formKey ?? "initial";
  // A failed save's typed values win over the stored ones.
  const text = (name: string, stored: string | null | undefined) => values?.[name] ?? stored ?? null;
  const amount = (name: string, stored: number | null | undefined) => values?.[name] ?? stored ?? null;

  const [category, setCategory] = useState(item.category ?? "");
  const [condition, setCondition] = useState(item.condition ?? "");
  const [storageLocation, setStorageLocation] = useState(item.storageLocation ?? "");
  useEffect(() => {
    if (!values) return;
    setCategory(values.category ?? "");
    setCondition(values.condition ?? "");
    setStorageLocation(values.storageLocation ?? "");
  }, [formKey, values]);

  // Leaving with unsaved edits loses them silently; the browser's own prompt is the guard.
  const [dirty, setDirty] = useState(false);
  const markDirty = () => setDirty(true);
  useEffect(() => {
    if (state.ok) setDirty(false);
  }, [state]);
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const profile = profileFor(referenceData.profiles, category);
  const showTestingNotes = hasField(profile, "FunctionalStatus") || Boolean(item.testingNotes);
  const measured = (profile.measurements?.length ?? 0) > 0;
  const hasPackageSize = Boolean(item.length || item.width || item.height);
  const editing = <T,>(set: (next: T) => void) => (next: T) => {
    set(next);
    markDirty();
  };

  return (
    <form action={formAction} onChange={markDirty} className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">What it is</CardTitle>
          <CardDescription>The category decides which details below apply.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <TextField label="Title" name="title" defaultValue={text("title", item.title)} error={errors.title} formKey={formKey} />
          <NotesField label="Description" name="description" defaultValue={text("description", item.description)} rows={5} error={errors.description} formKey={formKey} />
          <div className="grid gap-4 sm:grid-cols-2">
            <OptionField
              label="Category"
              name="category"
              // A category off the list (one the organization has since disabled) still shows and posts, rather than
              // reading as blank and saving the item uncategorised.
              value={matchOption(category, referenceData.categories ?? [], true)}
              options={referenceData.categories ?? []}
              onChange={editing(setCategory)}
              error={errors.category}
            />
            <TextField label="Barcode or SKU" name="barcode" defaultValue={text("barcode", item.barcode)} placeholder="UPC, EAN, ISBN or your own" error={errors.barcode} formKey={formKey} />
          </div>
          <ConditionField profile={profile} options={referenceData.conditions ?? []} value={condition} onChange={editing(setCondition)} error={errors.condition} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{profile.key === "general" ? "Details" : `${profile.label} details`}</CardTitle>
        </CardHeader>
        <CardContent>
          <ItemDetails
            profile={profile}
            category={matchOption(category, referenceData.categories ?? [])}
            reference={referenceData}
            initial={item}
            values={values}
            errors={errors}
            formKey={formKey}
            onEdit={markDirty}
          />
        </CardContent>
      </Card>

      {measured && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Measurements</CardTitle>
            <CardDescription>Measured flat. Buyers filter on these.</CardDescription>
          </CardHeader>
          <CardContent>
            <MeasurementsField measurements={profile.measurements ?? []} initial={values?.measurementsJson ?? item.measurementsJson} formKey={formKey} onEdit={markDirty} />
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Storage and shipping</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <OptionField
              label="Storage location"
              name="storageLocation"
              value={matchOption(storageLocation, referenceData.storageLocations ?? [], true)}
              options={referenceData.storageLocations ?? []}
              onChange={editing(setStorageLocation)}
              error={errors.storageLocation}
              custom
              placeholder="Search or type a bin, shelf or tote…"
            />
          </div>
          <div className="grid grid-cols-[1fr_96px] gap-4 sm:max-w-xs">
            <TextField label="Package weight" name="weight" type="number" defaultValue={amount("weight", item.weight)} error={errors.weight} formKey={formKey} />
            <SelectField label="Unit" name="weightUnit" choices={WEIGHT_UNITS} defaultValue={text("weightUnit", item.weightUnit)} formKey={formKey} />
          </div>
          {/* A garment ships in a poly mailer; its box size is rarely worth typing, so it folds away. Closed, it still posts. */}
          <details open={!measured || hasPackageSize} className="group rounded-lg border px-4 py-3">
            <summary className="cursor-pointer text-sm font-medium">Package size {measured && <span className="text-muted-foreground font-normal">· optional for this kind of item</span>}</summary>
            <div className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
              <TextField label="Length" name="length" type="number" defaultValue={amount("length", item.length)} error={errors.length} formKey={formKey} />
              <TextField label="Width" name="width" type="number" defaultValue={amount("width", item.width)} error={errors.width} formKey={formKey} />
              <TextField label="Height" name="height" type="number" defaultValue={amount("height", item.height)} error={errors.height} formKey={formKey} />
              <SelectField label="Unit" name="dimensionUnit" choices={DIMENSION_UNITS} defaultValue={text("dimensionUnit", item.dimensionUnit)} formKey={formKey} />
            </div>
          </details>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Cost and price</CardTitle>
          {item.acquisitionLotId && (
            <CardDescription>The cost is allocated from a purchase lot. Change it on the lot to keep the lot in balance; editing here overrides this item only.</CardDescription>
          )}
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-3">
          <TextField label={item.acquisitionLotId ? "Cost (allocated)" : "Cost"} name="purchasePrice" type="number" defaultValue={amount("purchasePrice", item.purchasePrice)} error={errors.purchasePrice} formKey={formKey} />
          <TextField label="Expected price" name="estimatedSalePrice" type="number" defaultValue={amount("estimatedSalePrice", item.estimatedSalePrice)} error={errors.estimatedSalePrice} formKey={formKey} />
          <TextField label="Seller's asking price" name="sellerAskingPrice" type="number" defaultValue={amount("sellerAskingPrice", item.sellerAskingPrice)} error={errors.sellerAskingPrice} formKey={formKey} />
          {item.isSold && (
            <p className="text-muted-foreground text-xs sm:col-span-3">
              This item has sold. Correcting its cost re-corrects the sale&rsquo;s cost of goods and net profit, with an entry in the sale&rsquo;s history.
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Notes</CardTitle>
          <CardDescription>What travels with the item: what is included, what is wrong with it, and what a listing should say.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-5 lg:grid-cols-2">
          <NotesField label="Included" name="accessories" defaultValue={text("accessories", item.accessories)} error={errors.accessories} formKey={formKey} />
          <NotesField label="Flaws and missing parts" name="missingParts" defaultValue={text("missingParts", item.missingParts)} error={errors.missingParts} formKey={formKey} />
          {showTestingNotes && (
            <NotesField label="Testing notes" name="testingNotes" defaultValue={text("testingNotes", item.testingNotes)} error={errors.testingNotes} formKey={formKey} />
          )}
          <NotesField label="Cleaning notes" name="cleaningNotes" defaultValue={text("cleaningNotes", item.cleaningNotes)} error={errors.cleaningNotes} formKey={formKey} />
          <NotesField label="Listing notes" name="listingNotes" defaultValue={text("listingNotes", item.listingNotes)} error={errors.listingNotes} formKey={formKey} />
          <NotesField label="Internal notes" name="notes" defaultValue={text("notes", item.notes)} error={errors.notes} formKey={formKey} />
        </CardContent>
      </Card>

      <details open={Boolean(errors.ownershipType)} className="bg-card rounded-xl border px-6 py-4">
        <summary className="cursor-pointer font-semibold">
          Ownership <span className="text-muted-foreground text-sm font-normal">· {OWNERSHIP.find(([value]) => value === (text("ownershipType", item.ownershipType) ?? "Owned"))?.[1] ?? "Owned"}</span>
        </summary>
        <div className="mt-4 sm:max-w-sm">
          <SelectField label="Ownership" name="ownershipType" choices={OWNERSHIP} defaultValue={text("ownershipType", item.ownershipType)} error={errors.ownershipType} formKey={formKey} />
        </div>
      </details>

      <div className="bg-background/95 sticky bottom-4 flex items-center justify-between gap-4 rounded-xl border p-4 shadow-lg backdrop-blur">
        <p aria-live="polite" className={`text-sm ${dirty ? "font-medium text-warning" : state.message ? (state.ok ? "text-success" : "text-destructive") : "text-muted-foreground"}`}>
          {dirty ? "Unsaved changes" : state.message || "All changes saved."}
        </p>
        <SubmitButton unchanged={!dirty} />
      </div>
    </form>
  );
}
