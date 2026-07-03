"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { createInventoryItem, type NewInventoryResult } from "./actions";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];
type ReferenceOption = components["schemas"]["InventoryReferenceOptionDto"];

const initial: NewInventoryResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring disabled:cursor-not-allowed disabled:opacity-50";
const sizedCategories = new Set(["Clothing", "Shoes", "Handbags", "Baby"]);
const otherValue = "Other";

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

function EnumField({ label, name, options }: { label: string; name: string; options: readonly (readonly [string, string])[] }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <select id={name} name={name} className={selectClass} defaultValue={options[0]?.[0]}>
        {options.map(([value, text]) => <option key={value} value={value}>{text}</option>)}
      </select>
    </div>
  );
}

function OptionField({ label, name, value, options, onChange, disabled = false }: {
  label: string; name: string; value: string; options: ReferenceOption[]; onChange?: (value: string) => void; disabled?: boolean;
}) {
  const approvedOptions = useMemo(() => options.filter((option) => !option.isLegacy && option.value && option.label), [options]);
  const effectiveValue = normalizeOptionValue(value, approvedOptions);
  const groups = useMemo(() => {
    const map = new Map<string, ReferenceOption[]>();
    for (const option of approvedOptions) {
      const group = option.group ?? "";
      map.set(group, [...(map.get(group) ?? []), option]);
    }
    return map;
  }, [approvedOptions]);
  const named = [...groups.keys()].some((group) => group.length > 0);
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <select
        id={name}
        name={name}
        value={onChange ? effectiveValue : undefined}
        defaultValue={onChange ? undefined : effectiveValue}
        onChange={(event) => onChange?.(event.target.value)}
        className={selectClass}
        disabled={disabled}
      >
        <option value="">Not assigned</option>
        {[...groups.entries()].map(([group, values]) =>
          named ? (
            <optgroup key={group} label={group}>
              {values.map((option) => <option key={`${option.parentValue ?? ""}:${option.value}`} value={option.value!}>{option.label}</option>)}
            </optgroup>
          ) : (
            values.map((option) => <option key={`${option.parentValue ?? ""}:${option.value}`} value={option.value!}>{option.label}</option>)
          )
        )}
      </select>
    </div>
  );
}

function normalizeOptionValue(value: string, options: ReferenceOption[]) {
  if (!value) return "";
  if (options.some((option) => option.value?.toLowerCase() === value.toLowerCase())) return value;
  return options.some((option) => option.value === otherValue) ? otherValue : "";
}

export function ItemCreator({ referenceData }: { referenceData: ReferenceData }) {
  const [state, action] = useActionState(createInventoryItem, initial);
  const [category, setCategory] = useState("");
  const [brand, setBrand] = useState("");
  const [model, setModel] = useState("");
  const [storageLocation, setStorageLocation] = useState("");
  const [sizeSystem, setSizeSystem] = useState("");
  const [sizeValue, setSizeValue] = useState("");

  const categories = referenceData.categories ?? [];
  const safeCategory = normalizeOptionValue(category, categories);
  const brands = (referenceData.brands ?? []).filter((option) => !option.isLegacy && (!option.parentValue || !safeCategory || option.parentValue === safeCategory));
  const safeBrand = normalizeOptionValue(brand, brands);
  const models = (referenceData.models ?? []).filter((option) => !option.isLegacy && (!option.parentValue || !safeBrand || option.parentValue === safeBrand));
  const safeSizeSystem = normalizeOptionValue(sizeSystem, referenceData.sizeSystems ?? []);
  const sizes = (referenceData.sizes ?? []).filter((option) => !option.isLegacy && (!option.parentValue || option.parentValue === safeSizeSystem));
  const showSizing = category === "" || sizedCategories.has(category);

  return (
    <form action={action} className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
      <div className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Item identity</CardTitle>
            <CardDescription>Only a title is required. Pick a category first — the brand list narrows to that category.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="title">Title</Label>
              <Input id="title" name="title" required placeholder="e.g. Super Mario 64, N64, complete in box" />
            </div>
            <OptionField label="Category" name="category" value={category} options={categories} onChange={(value) => { setCategory(value); setBrand(""); setModel(""); }} />
            <OptionField label="Brand" name="brand" value={brand} options={brands} onChange={(value) => { setBrand(value); setModel(""); }} />
            <OptionField label="Model / product line" name="model" value={model} options={models} onChange={setModel} disabled={!safeBrand || models.length === 0} />
            <OptionField label="Color" name="color" value="" options={referenceData.colors ?? []} />
            <OptionField label="Condition" name="condition" value="" options={referenceData.conditions ?? []} />
            <OptionField label="Storage location" name="storageLocation" value={storageLocation} options={referenceData.storageLocations ?? []} onChange={setStorageLocation} />
            {showSizing && <OptionField label="Size system" name="sizeSystem" value={sizeSystem} options={referenceData.sizeSystems ?? []} onChange={(value) => { setSizeSystem(value); setSizeValue(""); }} />}
            {showSizing && <OptionField label="Size" name="sizeValue" value={sizeValue} options={sizes} onChange={setSizeValue} disabled={!sizeSystem} />}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Classification</CardTitle>
            <CardDescription>Ownership and functional state drive consignment, repair, and listing routing.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <EnumField label="Ownership" name="ownershipType" options={ownershipOptions} />
            <EnumField label="Functional status" name="functionalStatus" options={functionalOptions} />
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
          <p className="text-muted-foreground mt-2">After creating, you land on the item to add photos, generate a listing, cross-list, and set its stage.</p>
        </Card>
        {state.message && (
          <div className="border-destructive/30 bg-destructive/5 text-destructive rounded-lg border p-3 text-sm">{state.message}</div>
        )}
        <SubmitButton />
      </aside>
    </form>
  );
}
