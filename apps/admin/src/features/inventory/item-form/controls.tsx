"use client";

import type { components } from "@overhaul/api-client";
import { Input, Label, Textarea } from "@overhaul/ui";
import { Combobox } from "@/components/combobox";
import { MultiCombobox } from "@/components/multi-combobox";

type ReferenceOption = components["schemas"]["InventoryReferenceOptionDto"];

const selectClass =
  "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring disabled:cursor-not-allowed disabled:opacity-50";

function Shell({ label, name, error, children }: { label: string; name: string; error?: string; children: React.ReactNode }) {
  return (
    <div className="space-y-2">
      <Label htmlFor={name}>{label}</Label>
      {children}
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

/**
 * Uncontrolled inputs keyed by `formKey`: a failed save hands back what was typed under a new key, which remounts
 * the input with it rather than reverting to the stored value.
 */
export function TextField({
  label,
  name,
  defaultValue,
  error,
  formKey,
  type = "text",
  placeholder,
  min,
  max,
  step,
}: {
  label: string;
  name: string;
  defaultValue?: string | number | null;
  error?: string;
  formKey?: string;
  type?: "text" | "number";
  placeholder?: string;
  min?: number;
  max?: number;
  step?: string;
}) {
  return (
    <Shell label={label} name={name} error={error}>
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
    </Shell>
  );
}

export function NotesField({ label, name, defaultValue, error, formKey, rows = 3, placeholder }: {
  label: string;
  name: string;
  defaultValue?: string | null;
  error?: string;
  formKey?: string;
  rows?: number;
  placeholder?: string;
}) {
  return (
    <Shell label={label} name={name} error={error}>
      <Textarea
        key={`${name}:${formKey ?? "initial"}`}
        id={name}
        name={name}
        rows={rows}
        defaultValue={defaultValue ?? ""}
        placeholder={placeholder}
        aria-invalid={error ? "true" : undefined}
      />
    </Shell>
  );
}

export function SelectField({ label, name, choices, defaultValue, error, formKey, includeBlank = false }: {
  label: string;
  name: string;
  choices: ReadonlyArray<readonly [string, string]>;
  defaultValue?: string | null;
  error?: string;
  formKey?: string;
  /** A first, empty choice — for optional pick lists, where nothing is a valid answer. */
  includeBlank?: boolean;
}) {
  return (
    <Shell label={label} name={name} error={error}>
      <select
        key={`${name}:${formKey ?? "initial"}`}
        id={name}
        name={name}
        defaultValue={defaultValue ?? (includeBlank ? "" : choices[0]?.[0])}
        className={selectClass}
        aria-invalid={error ? "true" : undefined}
      >
        {includeBlank && <option value="">—</option>}
        {choices.map(([value, text]) => (
          <option key={value} value={value}>{text}</option>
        ))}
      </select>
    </Shell>
  );
}

/** A searchable pick from reference options. `custom` lets a value outside the list be typed in, as brands are. */
export function OptionField({ label, name, value, options, onChange, error, disabled = false, custom = false, placeholder }: {
  label: string;
  name: string;
  value: string;
  options: readonly ReferenceOption[];
  onChange: (value: string) => void;
  error?: string;
  disabled?: boolean;
  custom?: boolean;
  placeholder?: string;
}) {
  const choices = options
    .filter((option) => option.value && !option.isLegacy)
    .map((option) => ({ value: option.value!, label: option.label ?? option.value!, group: option.group }));
  return (
    <Shell label={label} name={name} error={error}>
      <Combobox
        name={name}
        value={value}
        onChange={onChange}
        options={choices}
        placeholder={placeholder ?? `Search ${label.toLowerCase()}…`}
        disabled={disabled}
        allowCustom={custom}
      />
    </Shell>
  );
}

/** Up to `max` picks from a closed list, as chips. Posts the values as a JSON array under `name`. */
export function MultiOptionField({ label, name, values, options, max, onChange, error, placeholder }: {
  label: string;
  name: string;
  values: readonly string[];
  options: readonly string[];
  max: number;
  onChange: (values: string[]) => void;
  error?: string;
  placeholder?: string;
}) {
  return (
    <Shell label={max > 1 ? `${label} (up to ${max})` : label} name={name} error={error}>
      <MultiCombobox
        name={name}
        values={values}
        onChange={onChange}
        options={options.map((option) => ({ value: option, label: option }))}
        max={max}
        placeholder={placeholder ?? `Add ${label.toLowerCase()}…`}
      />
    </Shell>
  );
}

/** The listed options matching values regardless of case, in their order; values off the list are dropped. */
export function matchOptions(values: readonly string[] | null | undefined, options: readonly string[]): string[] {
  const matched = (values ?? []).map((value) => options.find((option) => option.toLowerCase() === value.trim().toLowerCase()));
  return [...new Set(matched.filter((value): value is string => Boolean(value)))];
}

/** The listed option matching a value regardless of case, or the value itself when `custom` values are allowed. */
export function matchOption(value: string | null | undefined, options: readonly ReferenceOption[], custom = false): string {
  if (!value) return "";
  const match = options.find((option) => option.value?.toLowerCase() === value.trim().toLowerCase());
  return match?.value ?? (custom ? value.trim() : "");
}
