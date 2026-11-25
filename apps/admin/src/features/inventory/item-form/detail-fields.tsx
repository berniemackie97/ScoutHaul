"use client";

import { useState } from "react";
import type { components } from "@overhaul/api-client";
import { Label } from "@overhaul/ui";
import { ChoiceGroup, type Choice } from "@/components/choice-group";
import { attributeInputName, isListAttribute, type ItemAttribute } from "../item-profiles";
import { matchOption, matchOptions, MultiOptionField, OptionField, TextField } from "./controls";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];

/** The API allows two colors; past that an item is Multicolor or Patterned. */
export const COLOR_LIMIT = 2;

/** A pick list this short reads at a glance as buttons; longer ones are searched. */
export const SEGMENT_LIMIT = 6;

/** Sizes in a system this many or fewer show as a grid. */
export const SIZE_GRID_LIMIT = 24;

/** A labelled one-of-few pick. */
export function ChoiceField({ label, error, className, children }: { label: string; error?: string; className?: string; children: React.ReactNode }) {
  return (
    <div className={`space-y-2 ${className ?? ""}`}>
      <Label>{label}</Label>
      {children}
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

/** A status pick from its enum, as chips. Uncontrolled; a failed save remounts it under a new `formKey`. */
export function StatusField({ label, name, choices, defaultValue, error, formKey }: {
  label: string;
  name: string;
  choices: ReadonlyArray<readonly [string, string]>;
  defaultValue?: string | null;
  error?: string;
  formKey?: string;
}) {
  const options: Choice[] = choices.map(([value, text]) => ({ value, label: text }));
  return (
    <ChoiceField label={label} error={error}>
      <ChoiceGroup key={`${name}:${formKey ?? "initial"}`} label={label} name={name} choices={options} defaultValue={defaultValue ?? choices[0]?.[0]} />
    </ChoiceField>
  );
}

/** One profile detail: free text, buttons for a short list, a search for a long one, or several picks for a list such as materials. */
export function AttributeField({ attribute, initial, onEdit }: { attribute: ItemAttribute; initial: string | string[] | undefined; onEdit?: () => void }) {
  const label = attribute.label ?? attribute.key!;
  const options = attribute.options ?? [];
  const [values, setValues] = useState(() => matchOptions(typeof initial === "string" ? [initial] : initial, options));
  const [value, setValue] = useState(() => (typeof initial === "string" ? initial : (initial?.[0] ?? "")));
  const changed = <T,>(set: (next: T) => void) => (next: T) => {
    set(next);
    onEdit?.();
  };

  if (isListAttribute(attribute)) {
    return <MultiOptionField label={label} name={attributeInputName(attribute)} values={values} options={options} max={attribute.maxValues ?? 1} onChange={changed(setValues)} />;
  }

  if (options.length > 0) {
    const choices = options.map((option) => ({ value: option, label: option }));
    const picked = matchOption(value, choices);
    if (options.length <= SEGMENT_LIMIT) {
      return (
        <ChoiceField label={label} className="sm:col-span-2">
          <ChoiceGroup label={label} name={attributeInputName(attribute)} variant="segmented" choices={choices} value={picked} onChange={changed(setValue)} clearable />
        </ChoiceField>
      );
    }
    return <OptionField label={label} name={attributeInputName(attribute)} value={picked} options={choices} onChange={changed(setValue)} />;
  }

  return <TextField label={label} name={attributeInputName(attribute)} defaultValue={value} />;
}

export function CountryField({ initial, reference, error, onEdit }: { initial: string; reference: ReferenceData; error?: string; onEdit?: () => void }) {
  const [country, setCountry] = useState(initial);
  return (
    <OptionField
      label="Country of origin"
      name="countryOfOrigin"
      value={matchOption(country, reference.countries ?? [])}
      options={reference.countries ?? []}
      onChange={(value) => {
        setCountry(value);
        onEdit?.();
      }}
      error={error}
    />
  );
}
