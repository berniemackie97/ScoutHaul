"use client";

import { useActionState, useMemo, useState } from "react";
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
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { updateInventoryItem, type InventoryActionResult } from "./actions";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];
type ReferenceOption = components["schemas"]["InventoryReferenceOptionDto"];

const initialState: InventoryActionResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm ring-offset-background focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50";

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
  return <Button type="submit" disabled={pending}>{pending ? "Saving…" : "Save inventory record"}</Button>;
}

function Field({ label, name, defaultValue, type = "text", placeholder, min, max, step }: {
  label: string;
  name: string;
  defaultValue?: string | number | null;
  type?: string;
  placeholder?: string;
  min?: number;
  max?: number;
  step?: string;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type={type} step={step ?? (type === "number" ? "0.01" : undefined)} min={min ?? (type === "number" ? 0 : undefined)} max={max} defaultValue={defaultValue ?? ""} placeholder={placeholder} />
    </div>
  );
}

function NotesField({ label, name, defaultValue, placeholder, rows = 3 }: {
  label: string;
  name: string;
  defaultValue?: string | null;
  placeholder?: string;
  rows?: number;
}) {
  return <div className="space-y-2"><Label htmlFor={name}>{label}</Label><Textarea id={name} name={name} defaultValue={defaultValue ?? ""} placeholder={placeholder} rows={rows} /></div>;
}

function OptionField({ label, name, value, options, onChange, emptyLabel = "Not assigned", disabled = false }: {
  label: string;
  name: string;
  value: string;
  options: ReferenceOption[];
  onChange?: (value: string) => void;
  emptyLabel?: string;
  disabled?: boolean;
}) {
  const groups = useMemo(() => {
    const result = new Map<string, ReferenceOption[]>();
    for (const option of options) {
      if (!option.value || !option.label) continue;
      const group = option.group ?? "Options";
      result.set(group, [...(result.get(group) ?? []), option]);
    }
    return result;
  }, [options]);
  const hasNamedGroups = [...groups.keys()].some(group => group !== "Options");

  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <select
        id={name}
        name={name}
        value={onChange ? value : undefined}
        defaultValue={onChange ? undefined : value}
        onChange={event => onChange?.(event.target.value)}
        className={selectClass}
        disabled={disabled}
      >
        <option value="">{disabled ? "No approved options configured" : emptyLabel}</option>
        {[...groups.entries()].map(([group, values]) => hasNamedGroups ? (
          <optgroup key={group} label={group}>{values.map(option => <option key={`${option.parentValue ?? ""}:${option.value}`} value={option.value!}>{option.label}</option>)}</optgroup>
        ) : values.map(option => <option key={`${option.parentValue ?? ""}:${option.value}`} value={option.value!}>{option.label}</option>))}
      </select>
    </div>
  );
}

function EnumField({ label, name, value, options }: { label: string; name: string; value: string; options: ReadonlyArray<readonly [string, string]> }) {
  return <div className="space-y-2"><Label htmlFor={name}>{label}</Label><select id={name} name={name} defaultValue={value} className={selectClass}>{options.map(([optionValue, optionLabel]) => <option key={optionValue} value={optionValue}>{optionLabel}</option>)}</select></div>;
}

export function InventoryEditor({ item, referenceData }: { item: InventoryItem; referenceData: ReferenceData }) {
  const action = updateInventoryItem.bind(null, item.id!);
  const [state, formAction] = useActionState(action, initialState);
  const [category, setCategory] = useState(item.category ?? "");
  const [brand, setBrand] = useState(item.brand ?? "");
  const [model, setModel] = useState(item.model ?? "");
  const [sizeSystem, setSizeSystem] = useState(item.sizeSystem ?? "");
  const [sizeValue, setSizeValue] = useState(item.sizeValue ?? "");

  // Brands scoped to the chosen category (universal/legacy brands with no parent always show).
  const brands = (referenceData.brands ?? []).filter(option => !option.parentValue || !category || option.parentValue === category);
  const models = (referenceData.models ?? []).filter(option => !option.parentValue || !brand || option.parentValue === brand);
  const sizes = (referenceData.sizes ?? []).filter(option => !option.parentValue || option.parentValue === sizeSystem);
  // Sizing only applies to worn/carried goods; hide it elsewhere to declutter.
  const sizedCategories = new Set(["Clothing", "Shoes", "Handbags", "Baby"]);
  const showSizing = category === "" || sizedCategories.has(category);

  return (
    <form action={formAction} className="space-y-6">
      <input type="hidden" name="attributesJson" value={item.attributesJson ?? ""} />
      <input type="hidden" name="measurementsJson" value={item.measurementsJson ?? ""} />

      <Card>
        <CardHeader><CardTitle className="text-lg">Identity and classification</CardTitle><CardDescription>Canonical values used by inventory search, storefront merchandising, mobile workflows, reporting, and channel integrations.</CardDescription></CardHeader>
        <CardContent className="space-y-5">
          <Field label="Title" name="title" defaultValue={item.title} />
          <NotesField label="Description" name="description" defaultValue={item.description} rows={5} />
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <OptionField label="Category" name="category" value={category} options={referenceData.categories ?? []} onChange={value => { setCategory(value); setBrand(""); setModel(""); }} />
            <OptionField label="Brand" name="brand" value={brand} options={brands} onChange={value => { setBrand(value); setModel(""); }} disabled={brands.length === 0} />
            <OptionField label="Model" name="model" value={model} options={models} onChange={setModel} disabled={models.length === 0} />
            <OptionField label="Color" name="color" value={item.color ?? ""} options={referenceData.colors ?? []} />
            <OptionField label="Cosmetic condition" name="condition" value={item.condition ?? ""} options={referenceData.conditions ?? []} emptyLabel="Select condition" />
            <EnumField label="Ownership" name="ownershipType" value={item.ownershipType ?? "Owned"} options={ownershipOptions} />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-lg">Condition controls</CardTitle><CardDescription>Separate condition dimensions prevent ambiguous listings and support category-specific readiness rules.</CardDescription></CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <EnumField label="Functional status" name="functionalStatus" value={item.functionalStatus ?? "Unknown"} options={functionalOptions} />
          <EnumField label="Completeness" name="completenessStatus" value={item.completenessStatus ?? "Unknown"} options={completenessOptions} />
          <EnumField label="Authenticity" name="authenticityStatus" value={item.authenticityStatus ?? "NotAssessed"} options={authenticityOptions} />
          <EnumField label="Packaging" name="packagingStatus" value={item.packagingStatus ?? "NotApplicable"} options={packagingOptions} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-lg">Identifiers and physical data</CardTitle><CardDescription>Identifiers stay unique text values; standardized systems, countries, measurements, and units remain controlled.</CardDescription></CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Field label="Barcode / GTIN" name="barcode" defaultValue={item.barcode} placeholder="UPC, EAN, ISBN" />
            <Field label="Manufacturer part number" name="manufacturerPartNumber" defaultValue={item.manufacturerPartNumber} />
            <Field label="Serial number" name="serialNumber" defaultValue={item.serialNumber} />
            <Field label="Manufacture year" name="manufactureYear" type="number" min={1800} max={2200} step="1" defaultValue={item.manufactureYear} />
            <OptionField label="Country of origin" name="countryOfOrigin" value={item.countryOfOrigin ?? ""} options={referenceData.countries ?? []} />
            <OptionField label="Storage location" name="storageLocation" value={item.storageLocation ?? ""} options={referenceData.storageLocations ?? []} disabled={(referenceData.storageLocations ?? []).length === 0} />
            {showSizing && <OptionField label="Size system" name="sizeSystem" value={sizeSystem} options={referenceData.sizeSystems ?? []} onChange={value => { setSizeSystem(value); setSizeValue(""); }} />}
            {showSizing && <OptionField label="Size" name="sizeValue" value={sizeValue} options={sizes} onChange={setSizeValue} disabled={!sizeSystem || sizes.length === 0} />}
          </div>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <Field label="Length" name="length" type="number" defaultValue={item.length} />
            <Field label="Width" name="width" type="number" defaultValue={item.width} />
            <Field label="Height" name="height" type="number" defaultValue={item.height} />
            <EnumField label="Dimension unit" name="dimensionUnit" value={item.dimensionUnit ?? "Inches"} options={[["Inches", "Inches"], ["Centimeters", "Centimeters"]]} />
            <div className="grid grid-cols-[1fr_130px] gap-2"><Field label="Weight" name="weight" type="number" defaultValue={item.weight} /><EnumField label="Unit" name="weightUnit" value={item.weightUnit ?? "Ounces"} options={[["Ounces", "Ounces"], ["Pounds", "Pounds"], ["Grams", "Grams"], ["Kilograms", "Kilograms"]]} /></div>
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
          <Field label="Seller asking price" name="sellerAskingPrice" type="number" defaultValue={item.sellerAskingPrice} />
          <Field label={item.acquisitionLotId ? "Acquisition cost (allocated)" : "Acquisition cost"} name="purchasePrice" type="number" defaultValue={item.purchasePrice} />
          <Field label="Estimated sale price" name="estimatedSalePrice" type="number" defaultValue={item.estimatedSalePrice} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-lg">Preparation and listing</CardTitle><CardDescription>Open-ended operational evidence and disclosures that must travel with the item.</CardDescription></CardHeader>
        <CardContent className="grid gap-5 lg:grid-cols-2">
          <NotesField label="Accessories included" name="accessories" defaultValue={item.accessories} />
          <NotesField label="Missing parts / disclosed defects" name="missingParts" defaultValue={item.missingParts} />
          <NotesField label="Testing notes" name="testingNotes" defaultValue={item.testingNotes} />
          <NotesField label="Cleaning notes" name="cleaningNotes" defaultValue={item.cleaningNotes} />
          <NotesField label="Listing notes" name="listingNotes" defaultValue={item.listingNotes} />
          <NotesField label="Internal notes" name="notes" defaultValue={item.notes} />
        </CardContent>
      </Card>

      <div className="sticky bottom-4 flex items-center justify-between gap-4 rounded-xl border bg-background/95 p-4 shadow-lg backdrop-blur">
        <div aria-live="polite" className={`text-sm ${state.message ? (state.ok ? "text-green-700" : "text-destructive") : "text-muted-foreground"}`}>{state.message || "One canonical record updates admin, mobile, storefront, reports, and channel workflows."}</div>
        <SubmitButton />
      </div>
    </form>
  );
}
