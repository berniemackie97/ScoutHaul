"use client";

import { useId } from "react";

export type Choice = { value: string; label: string; hint?: string | null };

const variants = {
  chips: {
    group: "flex flex-wrap gap-2",
    item:
      "min-h-11 rounded-lg border px-3.5 py-1.5 text-left text-sm transition-colors hover:bg-muted/60 peer-checked:border-primary peer-checked:bg-primary/10 peer-checked:font-semibold peer-checked:text-foreground peer-focus-visible:ring-2 peer-focus-visible:ring-ring",
  },
  segmented: {
    group: "inline-flex flex-wrap gap-0.5 rounded-lg border p-0.5",
    item:
      "flex h-9 items-center rounded-md px-3.5 text-sm transition-colors hover:bg-muted peer-checked:bg-foreground peer-checked:font-semibold peer-checked:text-background peer-focus-visible:ring-2 peer-focus-visible:ring-ring",
  },
  grid: {
    group: "flex flex-wrap gap-1.5",
    item:
      "flex h-10 min-w-12 items-center justify-center rounded-lg border px-2 text-sm tabular-nums transition-colors hover:bg-muted/60 peer-checked:border-primary peer-checked:bg-primary/10 peer-checked:font-bold peer-focus-visible:ring-2 peer-focus-visible:ring-ring",
  },
} as const;

/**
 * One pick from a short list, drawn as chips, a segmented control or a size grid. Native radios underneath, so it
 * posts under `name`, raises the form's change event and gets arrow-key movement for free. Controlled when `value`
 * is given; otherwise `defaultValue` seeds it (remount with a new `key` to reset).
 */
export function ChoiceGroup({
  label,
  name,
  choices,
  value,
  defaultValue,
  onChange,
  variant = "chips",
  disabled = false,
  clearable = false,
}: {
  label: string;
  name: string;
  choices: readonly Choice[];
  value?: string;
  defaultValue?: string | null;
  onChange?: (value: string) => void;
  variant?: keyof typeof variants;
  disabled?: boolean;
  /** Clicking the picked choice again clears it. Controlled and empty, an empty value posts either way. */
  clearable?: boolean;
}) {
  const id = useId();
  const style = variants[variant];
  const controlled = value !== undefined;
  return (
    <div role="radiogroup" aria-label={label} className={style.group}>
      {controlled && !value && <input type="hidden" name={name} value="" />}
      {choices.map((choice) => {
        const inputId = `${id}-${choice.value}`;
        const checkedProps = controlled ? { checked: value === choice.value } : { defaultChecked: (defaultValue ?? "") === choice.value };
        return (
          <div key={choice.value} className="relative">
            <input
              id={inputId}
              type="radio"
              name={name}
              value={choice.value}
              disabled={disabled}
              className="peer sr-only"
              onChange={() => onChange?.(choice.value)}
              onClick={() => {
                if (clearable && controlled && value === choice.value) onChange?.("");
              }}
              {...checkedProps}
            />
            <label htmlFor={inputId} className={`${style.item} block cursor-pointer peer-disabled:cursor-not-allowed peer-disabled:opacity-50`}>
              {choice.label}
              {choice.hint && <span className="text-muted-foreground block text-xs font-normal">{choice.hint}</span>}
            </label>
          </div>
        );
      })}
    </div>
  );
}
