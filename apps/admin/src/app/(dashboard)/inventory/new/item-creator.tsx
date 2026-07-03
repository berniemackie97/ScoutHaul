"use client";

import { useActionState, useEffect, useMemo, useState } from "react";
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

function Field({
  label,
  name,
  type = "text",
  placeholder,
  step,
  min,
  defaultValue,
  error,
  formKey,
}: {
  label: string;
  name: string;
  type?: string;
  placeholder?: string;
  step?: string;
  min?: string;
  defaultValue?: string | number | null;
  error?: string;
  formKey?: string;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input
        key={`${name}:${formKey ?? "initial"}`}
        id={name}
        name={name}
        type={type}
        placeholder={placeholder}
        step={step}
        min={min}
        defaultValue={defaultValue ?? ""}
        aria-invalid={error ? "true" : undefined}
      />
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

function EnumField({
  label,
  name,
  options,
  defaultValue,
  error,
  formKey,
}: {
  label: string;
  name: string;
  options: readonly (readonly [string, string])[];
  defaultValue?: string;
  error?: string;
  formKey?: string;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <select
        key={`${name}:${formKey ?? "initial"}`}
        id={name}
        name={name}
        className={selectClass}
        defaultValue={defaultValue ?? options[0]?.[0]}
        aria-invalid={error ? "true" : undefined}
      >
        {options.map(([value, text]) => <option key={value} value={value}>{text}</option>)}
      </select>
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

function OptionField({ label, name, value, options, onChange, disabled = false, error, formKey }: {
  label: string; name: string; value: string; options: ReferenceOption[]; onChange?: (value: string) => void; disabled?: boolean; error?: string; formKey?: string;
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
        key={`${name}:${onChange ? "controlled" : formKey ?? "initial"}`}
        id={name}
        name={name}
        value={onChange ? effectiveValue : undefined}
        defaultValue={onChange ? undefined : effectiveValue}
        onChange={(event) => onChange?.(event.target.value)}
        className={selectClass}
        disabled={disabled}
        aria-invalid={error ? "true" : undefined}
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
      {error && <p className="text-destructive text-xs">{error}</p>}
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
  const values = state.values ?? {};
  const fieldErrors = state.fieldErrors ?? {};
  const formKey = state.formKey ?? "initial";
  const [category, setCategory] = useState("");
  const [brand, setBrand] = useState("");
  const [model, setModel] = useState("");
  const [storageLocation, setStorageLocation] = useState("");
  const [sizeSystem, setSizeSystem] = useState("");
  const [sizeValue, setSizeValue] = useState("");

  useEffect(() => {
    if (!state.values) return;
    setCategory(state.values.category ?? "");
    setBrand(state.values.brand ?? "");
    setModel(state.values.model ?? "");
    setStorageLocation(state.values.storageLocation ?? "");
    setSizeSystem(state.values.sizeSystem ?? "");
    setSizeValue(state.values.sizeValue ?? "");
  }, [state.formKey, state.values]);

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
              <Input
                key={`title:${formKey}`}
                id="title"
                name="title"
                required
                placeholder="e.g. Super Mario 64, N64, complete in box"
                defaultValue={values.title ?? ""}
                aria-invalid={fieldErrors.title ? "true" : undefined}
              />
              {fieldErrors.title && <p className="text-destructive text-xs">{fieldErrors.title}</p>}
            </div>
            <OptionField label="Category" name="category" value={category} options={categories} onChange={(value) => { setCategory(value); setBrand(""); setModel(""); }} error={fieldErrors.category} />
            <OptionField label="Brand" name="brand" value={brand} options={brands} onChange={(value) => { setBrand(value); setModel(""); }} error={fieldErrors.brand} />
            <OptionField label="Model / product line" name="model" value={model} options={models} onChange={setModel} disabled={!safeBrand || models.length === 0} error={fieldErrors.model} />
            <OptionField label="Color" name="color" value={values.color ?? ""} options={referenceData.colors ?? []} error={fieldErrors.color} formKey={formKey} />
            <OptionField label="Condition" name="condition" value={values.condition ?? ""} options={referenceData.conditions ?? []} error={fieldErrors.condition} formKey={formKey} />
            <OptionField label="Storage location" name="storageLocation" value={storageLocation} options={referenceData.storageLocations ?? []} onChange={setStorageLocation} error={fieldErrors.storageLocation} />
            {showSizing && <OptionField label="Size system" name="sizeSystem" value={sizeSystem} options={referenceData.sizeSystems ?? []} onChange={(value) => { setSizeSystem(value); setSizeValue(""); }} error={fieldErrors.sizeSystem} />}
            {showSizing && <OptionField label="Size" name="sizeValue" value={sizeValue} options={sizes} onChange={setSizeValue} disabled={!sizeSystem} error={fieldErrors.sizeValue} />}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Classification</CardTitle>
            <CardDescription>Ownership and functional state drive consignment, repair, and listing routing.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <EnumField label="Ownership" name="ownershipType" options={ownershipOptions} defaultValue={values.ownershipType} error={fieldErrors.ownershipType} formKey={formKey} />
            <EnumField label="Functional status" name="functionalStatus" options={functionalOptions} defaultValue={values.functionalStatus} error={fieldErrors.functionalStatus} formKey={formKey} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Cost and valuation</CardTitle>
            <CardDescription>Single-item purchase cost becomes this item&rsquo;s cost basis for profit reporting.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-3">
            <Field label="Purchase price" name="purchasePrice" type="number" step="0.01" min="0" defaultValue={values.purchasePrice} error={fieldErrors.purchasePrice} formKey={formKey} />
            <Field label="Seller asking price" name="sellerAskingPrice" type="number" step="0.01" min="0" defaultValue={values.sellerAskingPrice} error={fieldErrors.sellerAskingPrice} formKey={formKey} />
            <Field label="Estimated sale price" name="estimatedSalePrice" type="number" step="0.01" min="0" defaultValue={values.estimatedSalePrice} error={fieldErrors.estimatedSalePrice} formKey={formKey} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Details and notes</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <Field label="Barcode / UPC" name="barcode" defaultValue={values.barcode} error={fieldErrors.barcode} formKey={formKey} />
            <Field label="Serial number" name="serialNumber" defaultValue={values.serialNumber} error={fieldErrors.serialNumber} formKey={formKey} />
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="description">Description</Label>
              <Textarea key={`description:${formKey}`} id="description" name="description" rows={3} placeholder="Condition detail, provenance, measurements…" defaultValue={values.description ?? ""} />
            </div>
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="notes">Internal notes</Label>
              <Textarea key={`notes:${formKey}`} id="notes" name="notes" rows={2} placeholder="Where/how acquired, follow-up work…" defaultValue={values.notes ?? ""} />
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
