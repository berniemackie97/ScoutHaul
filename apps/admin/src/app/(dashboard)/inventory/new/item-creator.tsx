"use client";

import { useActionState, useEffect, useMemo, useState, type ChangeEvent } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { Combobox } from "@/components/combobox";
import type { InventoryStatus } from "@/features/inventory/inventory-operations";
import { CREATE_STAGE_OPTIONS, readinessBlockers } from "@/features/inventory/lifecycle-readiness";
import { ReadinessChecklist } from "@/features/inventory/readiness-checklist";
import { createInventoryItem, type NewInventoryResult } from "./actions";

function toCombo(options: ReferenceOption[]) {
  return options.filter((option) => option.value).map((option) => ({ value: option.value!, label: option.label ?? option.value!, group: option.group }));
}

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

function SubmitButton({ disabledReason }: { disabledReason?: string }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending || Boolean(disabledReason)}>{pending ? "Creating…" : "Create item"}</Button>;
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
  value,
  onChange,
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
  value?: string;
  onChange?: (value: string) => void;
}) {
  const controlProps = onChange ? { value: value ?? "", onChange: (event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value) } : { defaultValue: defaultValue ?? "" };
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
        aria-invalid={error ? "true" : undefined}
        {...controlProps}
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
  value,
  onChange,
}: {
  label: string;
  name: string;
  options: readonly (readonly [string, string])[];
  defaultValue?: string;
  error?: string;
  formKey?: string;
  value?: string;
  onChange?: (value: string) => void;
}) {
  const controlProps = onChange ? { value: value ?? "", onChange: (event: ChangeEvent<HTMLSelectElement>) => onChange(event.target.value) } : { defaultValue: defaultValue ?? options[0]?.[0] };
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      <select
        key={`${name}:${formKey ?? "initial"}`}
        id={name}
        name={name}
        className={selectClass}
        aria-invalid={error ? "true" : undefined}
        {...controlProps}
      >
        {options.map(([value, text]) => <option key={value} value={value}>{text}</option>)}
      </select>
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

function OptionField({ label, name, value, options, onChange, disabled = false, error, formKey, allowCustom = false, placeholder = "Search options…" }: {
  label: string; name: string; value: string; options: ReferenceOption[]; onChange?: (value: string) => void; disabled?: boolean; error?: string; formKey?: string; allowCustom?: boolean; placeholder?: string;
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
        options={toCombo(approvedOptions)}
        placeholder={disabled ? "No approved options configured" : placeholder}
        disabled={disabled}
        allowCustom={allowCustom}
      />
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

export function ItemCreator({ referenceData }: { referenceData: ReferenceData }) {
  const [state, action] = useActionState(createInventoryItem, initial);
  const values = state.values ?? {};
  const fieldErrors = state.fieldErrors ?? {};
  const formKey = state.formKey ?? "initial";
  const [title, setTitle] = useState("");
  const [status, setStatus] = useState<InventoryStatus>("Draft");
  const [category, setCategory] = useState("");
  const [brand, setBrand] = useState("");
  const [model, setModel] = useState("");
  const [condition, setCondition] = useState("");
  const [storageLocation, setStorageLocation] = useState("");
  const [sizeSystem, setSizeSystem] = useState("");
  const [sizeValue, setSizeValue] = useState("");
  const [functionalStatus, setFunctionalStatus] = useState("Unknown");
  const [completenessStatus, setCompletenessStatus] = useState("Unknown");
  const [authenticityStatus, setAuthenticityStatus] = useState("NotAssessed");
  const [packagingStatus, setPackagingStatus] = useState("NotApplicable");
  const [testingNotes, setTestingNotes] = useState("");
  const [estimatedSalePrice, setEstimatedSalePrice] = useState("");

  useEffect(() => {
    if (!state.values) return;
    setTitle(state.values.title ?? "");
    setStatus((state.values.status as InventoryStatus | undefined) ?? "Draft");
    setCategory(state.values.category ?? "");
    setBrand(state.values.brand ?? "");
    setModel(state.values.model ?? "");
    setCondition(state.values.condition ?? "");
    setStorageLocation(state.values.storageLocation ?? "");
    setSizeSystem(state.values.sizeSystem ?? "");
    setSizeValue(state.values.sizeValue ?? "");
    setFunctionalStatus(state.values.functionalStatus ?? "Unknown");
    setCompletenessStatus(state.values.completenessStatus ?? "Unknown");
    setAuthenticityStatus(state.values.authenticityStatus ?? "NotAssessed");
    setPackagingStatus(state.values.packagingStatus ?? "NotApplicable");
    setTestingNotes(state.values.testingNotes ?? "");
    setEstimatedSalePrice(state.values.estimatedSalePrice ?? "");
  }, [state.formKey, state.values]);

  const categories = referenceData.categories ?? [];
  const safeCategory = normalizeOptionValue(category, categories);
  const brands = (referenceData.brands ?? []).filter((option) => !option.isLegacy && (!option.parentValue || !safeCategory || option.parentValue === safeCategory));
  const canonicalBrand = exactOptionValue(brand, brands);
  const canEnterModel = brand.trim().length > 0;
  const models = (referenceData.models ?? []).filter((option) => !option.isLegacy && (!option.parentValue || !canonicalBrand || option.parentValue === canonicalBrand));
  const safeSizeSystem = normalizeOptionValue(sizeSystem, referenceData.sizeSystems ?? []);
  const sizes = (referenceData.sizes ?? []).filter((option) => !option.isLegacy && (!option.parentValue || option.parentValue === safeSizeSystem));
  const showSizing = category === "" || sizedCategories.has(category);
  const readinessFacts = {
    title,
    category,
    condition,
    functionalStatus,
    completenessStatus,
    authenticityStatus,
    packagingStatus,
    testingNotes,
    estimatedSalePrice,
    photoCount: null,
  };
  const blockers = readinessBlockers(status, readinessFacts);
  const disabledReason = status === "ReadyToList" && blockers.length > 0 ? "Resolve readiness blockers before creating directly as Ready to list." : undefined;

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
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                aria-invalid={fieldErrors.title ? "true" : undefined}
              />
              {fieldErrors.title && <p className="text-destructive text-xs">{fieldErrors.title}</p>}
            </div>
            <OptionField label="Category" name="category" value={category} options={categories} onChange={(value) => { setCategory(value); setBrand(""); setModel(""); }} error={fieldErrors.category} />
            <div className="space-y-2">
              <Label htmlFor="brand">Brand</Label>
              <Combobox name="brand" value={brand} onChange={(value) => { setBrand(value); setModel(""); }} options={toCombo(brands)} placeholder={safeCategory ? `Search ${safeCategory} brands…` : "Search brands…"} />
              {fieldErrors.brand && <p className="text-destructive text-xs">{fieldErrors.brand}</p>}
            </div>
            <OptionField label="Model / product line" name="model" value={model} options={models} onChange={setModel} disabled={!canEnterModel} allowCustom error={fieldErrors.model} placeholder="Search or enter model…" />
            <OptionField label="Color" name="color" value={values.color ?? ""} options={referenceData.colors ?? []} error={fieldErrors.color} formKey={formKey} placeholder="Search colors…" />
            <OptionField label="Condition" name="condition" value={condition} options={referenceData.conditions ?? []} onChange={setCondition} error={fieldErrors.condition} formKey={formKey} placeholder="Search conditions…" />
            <OptionField label="Storage location" name="storageLocation" value={storageLocation} options={referenceData.storageLocations ?? []} onChange={setStorageLocation} allowCustom error={fieldErrors.storageLocation} placeholder="Search or enter location…" />
            {showSizing && <OptionField label="Size system" name="sizeSystem" value={sizeSystem} options={referenceData.sizeSystems ?? []} onChange={(value) => { setSizeSystem(value); setSizeValue(""); }} error={fieldErrors.sizeSystem} placeholder="Search size systems…" />}
            {showSizing && <OptionField label="Size" name="sizeValue" value={sizeValue} options={sizes} onChange={setSizeValue} disabled={!sizeSystem} error={fieldErrors.sizeValue} placeholder="Search sizes…" />}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Classification</CardTitle>
            <CardDescription>Ownership and functional state drive consignment, repair, and listing routing.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <EnumField label="Ownership" name="ownershipType" options={ownershipOptions} defaultValue={values.ownershipType} error={fieldErrors.ownershipType} formKey={formKey} />
            <EnumField label="Starting stage" name="status" options={CREATE_STAGE_OPTIONS.map((option) => [option.value, option.label] as const)} value={status} onChange={(value) => setStatus(value as InventoryStatus)} error={fieldErrors.status} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Condition and readiness</CardTitle>
            <CardDescription>Capture the category-specific facts that determine whether this item can move through intake, testing, photos, and listing.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <EnumField label="Functional / tested state" name="functionalStatus" options={functionalOptions} value={functionalStatus} onChange={setFunctionalStatus} error={fieldErrors.functionalStatus} />
            <EnumField label="Completeness" name="completenessStatus" options={completenessOptions} value={completenessStatus} onChange={setCompletenessStatus} error={fieldErrors.completenessStatus} />
            <EnumField label="Authenticity" name="authenticityStatus" options={authenticityOptions} value={authenticityStatus} onChange={setAuthenticityStatus} error={fieldErrors.authenticityStatus} />
            <EnumField label="Packaging / CIB state" name="packagingStatus" options={packagingOptions} value={packagingStatus} onChange={setPackagingStatus} error={fieldErrors.packagingStatus} />
            <div className="space-y-2 sm:col-span-2 lg:col-span-4">
              <Label htmlFor="testingNotes">Testing / inspection notes</Label>
              <Textarea id="testingNotes" name="testingNotes" rows={3} value={testingNotes} onChange={(event) => setTestingNotes(event.target.value)} placeholder="For games/electronics: tested console, disc/cart read, controller ports, battery, defects, or parts-only disclosure." />
            </div>
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
            <Field label="Estimated sale price" name="estimatedSalePrice" type="number" step="0.01" min="0" value={estimatedSalePrice} onChange={setEstimatedSalePrice} error={fieldErrors.estimatedSalePrice} formKey={formKey} />
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
          <p className="text-muted-foreground mt-2">Choose the starting stage now. The record opens next so photos, listing generation, channel tracking, and sale workflow stay attached to the same canonical item.</p>
        </Card>
        <ReadinessChecklist targetStatus={status} facts={readinessFacts} compact />
        {disabledReason && <div className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-800">{disabledReason}</div>}
        {state.message && (
          <div className="border-destructive/30 bg-destructive/5 text-destructive rounded-lg border p-3 text-sm">{state.message}</div>
        )}
        <SubmitButton disabledReason={disabledReason} />
      </aside>
    </form>
  );
}
