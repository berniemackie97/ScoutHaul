"use client";

import { useActionState, useEffect, useState, type FormEvent } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import type { InventoryStatus } from "@/features/inventory/inventory-operations";
import { profileFor } from "@/features/inventory/item-profiles";
import { CREATE_STAGE_OPTIONS, readinessBlockers, type InventoryReadinessFacts } from "@/features/inventory/lifecycle-readiness";
import { ReadinessChecklist } from "@/features/inventory/readiness-checklist";
import { matchOption, NotesField, OptionField, SelectField, TextField } from "@/features/inventory/item-form/controls";
import { ItemDetails } from "@/features/inventory/item-form/item-details";
import { OWNERSHIP, WEIGHT_UNITS } from "@/features/inventory/item-form/options";
import { createInventoryItem, type NewInventoryResult } from "./actions";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];

const initial: NewInventoryResult = { ok: false, message: "" };
const STAGES = CREATE_STAGE_OPTIONS.map((option) => [option.value, option.label] as const);

function SubmitButton({ disabledReason }: { disabledReason?: string }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" className="w-full" disabled={pending || Boolean(disabledReason)}>
      {pending ? "Creating…" : "Create item"}
    </Button>
  );
}

export function ItemCreator({ referenceData }: { referenceData: ReferenceData }) {
  const [state, action] = useActionState(createInventoryItem, initial);
  const values = state.values;
  const errors = state.fieldErrors ?? {};
  const formKey = state.formKey ?? "initial";

  const [category, setCategory] = useState("");
  const [condition, setCondition] = useState("");
  const [storageLocation, setStorageLocation] = useState("");
  // What the readiness checklist reads, kept current from the form's own change events.
  const [typed, setTyped] = useState<Record<string, string>>({ status: "Draft" });
  useEffect(() => {
    if (!values) return;
    setCategory(values.category ?? "");
    setCondition(values.condition ?? "");
    setStorageLocation(values.storageLocation ?? "");
    setTyped(values);
  }, [formKey, values]);

  function track(event: FormEvent<HTMLFormElement>) {
    const target = event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
    if (target.name) setTyped((current) => ({ ...current, [target.name]: target.value }));
  }

  const profile = profileFor(referenceData.profiles, category);
  const status = (typed.status as InventoryStatus | undefined) ?? "Draft";
  const facts: InventoryReadinessFacts = {
    profile,
    title: typed.title,
    category,
    condition,
    functionalStatus: typed.functionalStatus,
    completenessStatus: typed.completenessStatus,
    authenticityStatus: typed.authenticityStatus,
    packagingStatus: typed.packagingStatus,
    testingNotes: typed.testingNotes,
    estimatedSalePrice: typed.estimatedSalePrice,
    photoCount: null,
  };
  const blockers = readinessBlockers(status, facts);
  const disabledReason = status === "ReadyToList" && blockers.length > 0 ? "Clear the blockers to create it as Ready to list." : undefined;

  return (
    <form action={action} onChange={track} className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
      <div className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">What it is</CardTitle>
            <CardDescription>Only a title is needed to start. The category decides which details apply.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <TextField label="Title" name="title" defaultValue={values?.title} placeholder="e.g. Super Mario 64, N64, complete in box" error={errors.title} formKey={formKey} />
            <div className="grid gap-4 sm:grid-cols-3">
              <OptionField label="Category" name="category" value={matchOption(category, referenceData.categories ?? [])} options={referenceData.categories ?? []} onChange={setCategory} error={errors.category} />
              <OptionField label="Condition" name="condition" value={matchOption(condition, referenceData.conditions ?? [])} options={referenceData.conditions ?? []} onChange={setCondition} error={errors.condition} />
              <SelectField label="Starting stage" name="status" choices={STAGES} defaultValue={values?.status ?? "Draft"} error={errors.status} formKey={formKey} />
            </div>
            <NotesField label="Description" name="description" defaultValue={values?.description} placeholder="Condition detail, provenance, measurements…" formKey={formKey} />
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
              initial={{}}
              values={values}
              errors={errors}
              formKey={formKey}
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Cost, price and storage</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-3">
            <TextField label="Cost" name="purchasePrice" type="number" defaultValue={values?.purchasePrice} error={errors.purchasePrice} formKey={formKey} />
            <TextField label="Expected price" name="estimatedSalePrice" type="number" defaultValue={values?.estimatedSalePrice} error={errors.estimatedSalePrice} formKey={formKey} />
            <SelectField label="Ownership" name="ownershipType" choices={OWNERSHIP} defaultValue={values?.ownershipType} formKey={formKey} />
            <TextField label="Barcode or SKU" name="barcode" defaultValue={values?.barcode} error={errors.barcode} formKey={formKey} />
            <OptionField
              label="Storage location"
              name="storageLocation"
              value={matchOption(storageLocation, referenceData.storageLocations ?? [], true)}
              options={referenceData.storageLocations ?? []}
              onChange={setStorageLocation}
              custom
              placeholder="Search or type a bin, shelf or tote…"
            />
            <div className="grid grid-cols-[1fr_88px] gap-2">
              <TextField label="Weight" name="weight" type="number" defaultValue={values?.weight} error={errors.weight} formKey={formKey} />
              <SelectField label="Unit" name="weightUnit" choices={WEIGHT_UNITS} defaultValue={values?.weightUnit} formKey={formKey} />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Notes</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 lg:grid-cols-2">
            <NotesField label="Testing notes" name="testingNotes" defaultValue={values?.testingNotes} placeholder="What was tested, and anything that does not work." formKey={formKey} />
            <NotesField label="Internal notes" name="notes" defaultValue={values?.notes} placeholder="Where it came from, follow-up work…" formKey={formKey} />
          </CardContent>
        </Card>
      </div>

      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <ReadinessChecklist targetStatus={status} facts={facts} compact />
        {disabledReason && <div className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-800">{disabledReason}</div>}
        {state.message && <div className="border-destructive/30 bg-destructive/5 text-destructive rounded-lg border p-3 text-sm">{state.message}</div>}
        <SubmitButton disabledReason={disabledReason} />
        <p className="text-muted-foreground text-xs">Photos, listings and channels are added on the item once it exists.</p>
      </aside>
    </form>
  );
}
