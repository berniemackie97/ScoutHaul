"use client";

import type { components } from "@overhaul/api-client";
import { Label } from "@overhaul/ui";
import { ChoiceGroup } from "@/components/choice-group";
import { Combobox } from "@/components/combobox";
import type { ItemProfile } from "../item-profiles";
import { CHIP_LIMIT, conditionChoices } from "./condition-scale";

type ReferenceOption = components["schemas"]["InventoryReferenceOptionDto"];

/** The item's condition on the scale its kind is described in: chips with what each means, or a search when the list is long. */
export function ConditionField({ profile, options, value, onChange, error }: {
  profile: ItemProfile;
  options: readonly ReferenceOption[];
  value: string;
  onChange: (value: string) => void;
  error?: string;
}) {
  const choices = conditionChoices(profile, options, value);
  const matched = choices.find((choice) => choice.value.toLowerCase() === value.trim().toLowerCase())?.value ?? "";
  const scaleNote = profile.key === "general" ? "Choose a category for its scale" : `${profile.label} scale`;
  return (
    <div className="space-y-2">
      <Label id="condition-label">
        Condition <span className="text-muted-foreground font-normal">· {scaleNote}</span>
      </Label>
      {choices.length <= CHIP_LIMIT ? (
        <ChoiceGroup label="Condition" name="condition" choices={choices} value={matched} onChange={onChange} clearable />
      ) : (
        <Combobox name="condition" value={matched} onChange={onChange} options={choices.map((choice) => ({ value: choice.value, label: choice.label }))} placeholder="Search conditions…" allowCustom={false} />
      )}
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}
