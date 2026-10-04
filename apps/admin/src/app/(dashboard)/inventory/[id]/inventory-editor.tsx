"use client";

import { useActionState, useEffect, useMemo, useState } from "react";
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
import { Combobox } from "@/components/combobox";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { updateInventoryItem, type InventoryActionResult } from "./actions";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];
type ReferenceOption = components["schemas"]["InventoryReferenceOptionDto"];

const initialState: InventoryActionResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm ring-offset-background focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50";
const otherValue = "Other";

const ownershipOptions = [
  ["Owned", "Owned inventory"],
  ["Consigned", "Consigned inventory"],
  ["CustomerRepair", "Customer-owned repair"],
  ["VendorManaged", "Vendor-managed inventory"],
] as const;
const functionalOptions = [
  ["Unknown", "Unknown / not recorded"], ["Untested", "Untested"],
  ["FullyFunctional", "Fully functional"], ["FunctionalWithIssues", "Functional with issues"],
  ["Intermittent", "Intermittent"], ["NonFunctional", "Non-functional"], ["PartsOnly", "Parts only"],
] as const;
const completenessOptions = [
  ["Unknown", "Unknown / not recorded"], ["Complete", "Complete"],
  ["MissingAccessories", "Missing accessories"], ["MissingParts", "Missing parts"], ["Incomplete", "Incomplete set"],
] as const;
const authenticityOptions = [
  ["NotAssessed", "Not assessed"], ["Authentic", "Authenticated"], ["LikelyAuthentic", "Likely authentic"],
  ["NeedsVerification", "Needs verification"], ["Replica", "Replica (disclosed)"], ["Counterfeit", "Counterfeit — do not list"],
] as const;
const packagingOptions = [
  ["NotApplicable", "Not applicable"], ["FactorySealed", "Factory sealed"],
  ["OriginalExcellent", "Original packaging — excellent"], ["OriginalWorn", "Original packaging — worn"],
  ["OriginalDamaged", "Original packaging — damaged"], ["ReplacementPackaging", "Replacement packaging"],
  ["LooseNoPackaging", "Loose / no packaging"],
] as const;

function SubmitButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Saving…" : "Save changes"}</Button>;
}

function Field({ label, name, defaultValue, type = "text", placeholder, min, max, step, error, formKey }: {
  label: string;
  name: string;
  defaultValue?: string | number | null;
  type?: string;
  placeholder?: string;
  min?: number;
  max?: number;
  step?: string;
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
        step={step ?? (type === "number" ? "0.01" : undefined)}
        min={min ?? (type === "number" ? 0 : undefined)}
        max={max}
        defaultValue={defaultValue ?? ""}
        placeholder={placeholder}
        aria-invalid={error ? "true" : undefined}
      />
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

function NotesField({ label, name, defaultValue, placeholder, rows = 3, error, formKey }: {
  label: string;
  name: string;
  defaultValue?: string | null;
  placeholder?: string;
  rows?: number;
  error?: string;
  formKey?: string;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Textarea
        key={`${name}:${formKey ?? "initial"}`}
        id={name}
        name={name}
        defaultValue={defaultValue ?? ""}
        placeholder={placeholder}
        rows={rows}
        aria-invalid={error ? "true" : undefined}
      />
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

function OptionField({ label, name, value, options, onChange, emptyLabel = "Not assigned", disabled = false, error, formKey, allowCustom = false, placeholder = "Search options…" }: {
  label: string;
  name: string;
  value: string;
  options: ReferenceOption[];
  onChange?: (value: string) => void;
  emptyLabel?: string;
  disabled?: boolean;
  error?: string;
  formKey?: string;
  allowCustom?: boolean;
  placeholder?: string;
}) {
  const approvedOptions = useMemo(() => options.filter((option) => !option.isLegacy && option.value && option.label), [options]);
  const effectiveValue = normalizeOptionValue(value, approvedOptions, allowCustom);
  const [internalValue, setInternalValue] = useState(effectiveValue);
  useEffect(() => {
    if (!onChange) setInternalValue(effectiveValue);
  }, [effectiveValue, formKey, onChange]);
  const currentValue = onChange ? effectiveValue : internalValue;
  const updateValue = onChange ?? setInternalValue;

  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Combobox
        key={`${name}:${formKey ?? "initial"}`}
        name={name}
        value={currentValue}
        onChange={updateValue}
        options={approvedOptions.map(option => ({ value: option.value!, label: option.label ?? option.value!, group: option.group }))}
        placeholder={disabled ? "No approved options configured" : placeholder || emptyLabel}
        disabled={disabled}
        allowCustom={allowCustom}
        emptyLabel={emptyLabel}
      />
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

function EnumField({ label, name, value, options, error, formKey }: { label: string; name: string; value: string; options: ReadonlyArray<readonly [string, string]>; error?: string; formKey?: string }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <select
        key={`${name}:${formKey ?? "initial"}`}
        id={name}
        name={name}
        defaultValue={value}
        className={selectClass}
        aria-invalid={error ? "true" : undefined}
      >
        {options.map(([optionValue, optionLabel]) => <option key={optionValue} value={optionValue}>{optionLabel}</option>)}
      </select>
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

function normalizeOptionValue(value: string, options: ReferenceOption[], preserveCustom = false) {
  if (!value) return "";
  const match = options.find((option) => option.value?.toLowerCase() === value.toLowerCase());
  if (match?.value) return match.value;
  if (preserveCustom) return value.trim();
  return options.some((option) => option.value === otherValue) ? otherValue : "";
}

function exactOptionValue(value: string, options: ReferenceOption[]) {
  if (!value) return "";
  return options.find((option) => option.value?.toLowerCase() === value.toLowerCase())?.value ?? "";
}

export function InventoryEditor({ item, referenceData }: { item: InventoryItem; referenceData: ReferenceData }) {
  const action = updateInventoryItem.bind(null, item.id!);
  const [state, formAction] = useActionState(action, initialState);
  const values = state.values ?? {};
  const fieldErrors = state.fieldErrors ?? {};
  const formKey = state.formKey ?? "initial";
  function valueFor(name: string, fallback?: string | null): string;
  function valueFor(name: string, fallback?: number | null): string | number;
  function valueFor(name: string, fallback?: string | number | null) {
    return values[name] ?? fallback ?? "";
  }
  const [category, setCategory] = useState(item.category ?? "");
  const [brand, setBrand] = useState(item.brand ?? "");
  const [model, setModel] = useState(item.model ?? "");
  const [sizeSystem, setSizeSystem] = useState(item.sizeSystem ?? "");
  const [sizeValue, setSizeValue] = useState(item.sizeValue ?? "");
  const [storageLocation, setStorageLocation] = useState(item.storageLocation ?? "");

  useEffect(() => {
    if (!state.values) return;
    setCategory(state.values.category ?? "");
    setBrand(state.values.brand ?? "");
    setModel(state.values.model ?? "");
    setSizeSystem(state.values.sizeSystem ?? "");
    setSizeValue(state.values.sizeValue ?? "");
    setStorageLocation(state.values.storageLocation ?? "");
  }, [state.formKey, state.values]);

  const categories = referenceData.categories ?? [];
  const safeCategory = normalizeOptionValue(category, categories);
  const brands = (referenceData.brands ?? []).filter(option => !option.isLegacy && (!option.parentValue || !safeCategory || option.parentValue === safeCategory));
  const canonicalBrand = exactOptionValue(brand, brands);
  const canEnterModel = brand.trim().length > 0;
  const models = (referenceData.models ?? []).filter(option => !option.isLegacy && (!option.parentValue || !canonicalBrand || option.parentValue === canonicalBrand));
  const safeSizeSystem = normalizeOptionValue(sizeSystem, referenceData.sizeSystems ?? []);
  const sizes = (referenceData.sizes ?? []).filter(option => !option.isLegacy && (!option.parentValue || option.parentValue === safeSizeSystem));
  // Sizing only applies to worn/carried goods; hide it elsewhere to declutter.
  const sizedCategories = new Set(["Clothing", "Shoes", "Handbags", "Baby"]);
  const showSizing = category === "" || sizedCategories.has(category);

  // Leaving with unsaved edits loses them silently; the browser's own prompt is the guard.
  const [dirty, setDirty] = useState(false);
  useEffect(() => {
    if (state.ok) setDirty(false);
  }, [state]);
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  return (
    <form action={formAction} onChange={() => setDirty(true)} className="space-y-6">
      <input type="hidden" name="attributesJson" value={item.attributesJson ?? ""} />
      <input type="hidden" name="measurementsJson" value={item.measurementsJson ?? ""} />

      <Card>
        <CardHeader><CardTitle className="text-lg">Identity and classification</CardTitle><CardDescription>Canonical values used by inventory search, storefront merchandising, mobile workflows, reporting, and channel integrations.</CardDescription></CardHeader>
        <CardContent className="space-y-5">
          <Field label="Title" name="title" defaultValue={valueFor("title", item.title)} error={fieldErrors.title} formKey={formKey} />
          <NotesField label="Description" name="description" defaultValue={valueFor("description", item.description)} rows={5} error={fieldErrors.description} formKey={formKey} />
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <OptionField label="Category" name="category" value={category} options={categories} onChange={value => { setCategory(value); setBrand(""); setModel(""); }} error={fieldErrors.category} placeholder="Search categories…" />
            <div className="space-y-2">
              <Label htmlFor="brand">Brand</Label>
              <Combobox name="brand" value={brand} onChange={value => { setBrand(value); setModel(""); }} options={brands.filter(o => o.value).map(o => ({ value: o.value!, label: o.label ?? o.value!, group: o.group }))} placeholder={safeCategory ? `Search ${safeCategory} brands…` : "Search brands…"} />
              {fieldErrors.brand && <p className="text-destructive text-xs">{fieldErrors.brand}</p>}
            </div>
            <OptionField label="Model / product line" name="model" value={model} options={models} onChange={setModel} disabled={!canEnterModel} allowCustom error={fieldErrors.model} placeholder="Search or enter model…" />
            <OptionField label="Color" name="color" value={valueFor("color", item.color)} options={referenceData.colors ?? []} error={fieldErrors.color} formKey={formKey} placeholder="Search colors…" />
            <OptionField label="Cosmetic condition" name="condition" value={valueFor("condition", item.condition)} options={referenceData.conditions ?? []} emptyLabel="Select condition" error={fieldErrors.condition} formKey={formKey} placeholder="Search conditions…" />
            <EnumField label="Ownership" name="ownershipType" value={valueFor("ownershipType", item.ownershipType ?? "Owned")} options={ownershipOptions} error={fieldErrors.ownershipType} formKey={formKey} />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-lg">Condition controls</CardTitle><CardDescription>Separate condition dimensions prevent ambiguous listings and support category-specific readiness rules.</CardDescription></CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <EnumField label="Functional status" name="functionalStatus" value={valueFor("functionalStatus", item.functionalStatus ?? "Unknown")} options={functionalOptions} error={fieldErrors.functionalStatus} formKey={formKey} />
          <EnumField label="Completeness" name="completenessStatus" value={valueFor("completenessStatus", item.completenessStatus ?? "Unknown")} options={completenessOptions} error={fieldErrors.completenessStatus} formKey={formKey} />
          <EnumField label="Authenticity" name="authenticityStatus" value={valueFor("authenticityStatus", item.authenticityStatus ?? "NotAssessed")} options={authenticityOptions} error={fieldErrors.authenticityStatus} formKey={formKey} />
          <EnumField label="Packaging" name="packagingStatus" value={valueFor("packagingStatus", item.packagingStatus ?? "NotApplicable")} options={packagingOptions} error={fieldErrors.packagingStatus} formKey={formKey} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-lg">Identifiers and physical data</CardTitle><CardDescription>Identifiers stay unique text values; standardized systems, countries, measurements, and units remain controlled.</CardDescription></CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Field label="Barcode / GTIN" name="barcode" defaultValue={valueFor("barcode", item.barcode)} placeholder="UPC, EAN, ISBN" error={fieldErrors.barcode} formKey={formKey} />
            <Field label="Manufacturer part number" name="manufacturerPartNumber" defaultValue={valueFor("manufacturerPartNumber", item.manufacturerPartNumber)} error={fieldErrors.manufacturerPartNumber} formKey={formKey} />
            <Field label="Serial number" name="serialNumber" defaultValue={valueFor("serialNumber", item.serialNumber)} error={fieldErrors.serialNumber} formKey={formKey} />
            <Field label="Manufacture year" name="manufactureYear" type="number" min={1800} max={2200} step="1" defaultValue={valueFor("manufactureYear", item.manufactureYear)} error={fieldErrors.manufactureYear} formKey={formKey} />
            <OptionField label="Country of origin" name="countryOfOrigin" value={valueFor("countryOfOrigin", item.countryOfOrigin)} options={referenceData.countries ?? []} error={fieldErrors.countryOfOrigin} formKey={formKey} placeholder="Search countries…" />
            <OptionField label="Storage location" name="storageLocation" value={storageLocation} options={referenceData.storageLocations ?? []} onChange={setStorageLocation} allowCustom error={fieldErrors.storageLocation} placeholder="Search or enter location…" />
            {showSizing && <OptionField label="Size system" name="sizeSystem" value={sizeSystem} options={referenceData.sizeSystems ?? []} onChange={value => { setSizeSystem(value); setSizeValue(""); }} error={fieldErrors.sizeSystem} placeholder="Search size systems…" />}
            {showSizing && <OptionField label="Size" name="sizeValue" value={sizeValue} options={sizes} onChange={setSizeValue} disabled={!sizeSystem || sizes.length === 0} error={fieldErrors.sizeValue} placeholder="Search sizes…" />}
          </div>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <Field label="Length" name="length" type="number" defaultValue={valueFor("length", item.length)} error={fieldErrors.length} formKey={formKey} />
            <Field label="Width" name="width" type="number" defaultValue={valueFor("width", item.width)} error={fieldErrors.width} formKey={formKey} />
            <Field label="Height" name="height" type="number" defaultValue={valueFor("height", item.height)} error={fieldErrors.height} formKey={formKey} />
            <EnumField label="Dimension unit" name="dimensionUnit" value={valueFor("dimensionUnit", item.dimensionUnit ?? "Inches")} options={[["Inches", "Inches"], ["Centimeters", "Centimeters"]]} error={fieldErrors.dimensionUnit} formKey={formKey} />
            <div className="grid grid-cols-[1fr_130px] gap-2"><Field label="Weight" name="weight" type="number" defaultValue={valueFor("weight", item.weight)} error={fieldErrors.weight} formKey={formKey} /><EnumField label="Unit" name="weightUnit" value={valueFor("weightUnit", item.weightUnit ?? "Ounces")} options={[["Ounces", "Ounces"], ["Pounds", "Pounds"], ["Grams", "Grams"], ["Kilograms", "Kilograms"]]} error={fieldErrors.weightUnit} formKey={formKey} /></div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Cost and valuation</CardTitle>
          <CardDescription>
            {item.acquisitionLotId
              ? "Cost basis is allocated from an acquisition lot. Change the landed cost or allocation on the lot page to keep it in sync — editing here overrides only this item."
              : "Specific-item cost basis and expected sale value used by budgets and profit reporting."}
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-3">
          <Field label="Seller asking price" name="sellerAskingPrice" type="number" defaultValue={valueFor("sellerAskingPrice", item.sellerAskingPrice)} error={fieldErrors.sellerAskingPrice} formKey={formKey} />
          <Field label={item.acquisitionLotId ? "Acquisition cost (allocated)" : "Acquisition cost"} name="purchasePrice" type="number" defaultValue={valueFor("purchasePrice", item.purchasePrice)} error={fieldErrors.purchasePrice} formKey={formKey} />
          <Field label="Estimated sale price" name="estimatedSalePrice" type="number" defaultValue={valueFor("estimatedSalePrice", item.estimatedSalePrice)} error={fieldErrors.estimatedSalePrice} formKey={formKey} />
          {item.isSold && (
            <p className="text-muted-foreground sm:col-span-3 text-xs">
              This item is sold — fixing its acquisition cost re-corrects the recorded sale&rsquo;s COGS and true net, with an audit entry on the sale.
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-lg">Preparation and listing</CardTitle><CardDescription>Open-ended operational evidence and disclosures that must travel with the item.</CardDescription></CardHeader>
        <CardContent className="grid gap-5 lg:grid-cols-2">
          <NotesField label="Accessories included" name="accessories" defaultValue={valueFor("accessories", item.accessories)} error={fieldErrors.accessories} formKey={formKey} />
          <NotesField label="Missing parts / disclosed defects" name="missingParts" defaultValue={valueFor("missingParts", item.missingParts)} error={fieldErrors.missingParts} formKey={formKey} />
          <NotesField label="Testing notes" name="testingNotes" defaultValue={valueFor("testingNotes", item.testingNotes)} error={fieldErrors.testingNotes} formKey={formKey} />
          <NotesField label="Cleaning notes" name="cleaningNotes" defaultValue={valueFor("cleaningNotes", item.cleaningNotes)} error={fieldErrors.cleaningNotes} formKey={formKey} />
          <NotesField label="Listing notes" name="listingNotes" defaultValue={valueFor("listingNotes", item.listingNotes)} error={fieldErrors.listingNotes} formKey={formKey} />
          <NotesField label="Internal notes" name="notes" defaultValue={valueFor("notes", item.notes)} error={fieldErrors.notes} formKey={formKey} />
        </CardContent>
      </Card>

      <div className="sticky bottom-4 flex items-center justify-between gap-4 rounded-xl border bg-background/95 p-4 shadow-lg backdrop-blur">
        <div aria-live="polite" className={`text-sm ${state.message && !dirty ? (state.ok ? "text-green-700" : "text-destructive") : dirty ? "font-medium text-amber-700 dark:text-amber-400" : "text-muted-foreground"}`}>
          {dirty ? "Unsaved changes" : state.message || "All changes saved."}
        </div>
        <SubmitButton />
      </div>
    </form>
  );
}
