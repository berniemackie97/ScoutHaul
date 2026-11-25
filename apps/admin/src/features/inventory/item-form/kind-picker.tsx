"use client";

import { useState } from "react";
import type { components } from "@overhaul/api-client";
import { ChoiceGroup } from "@/components/choice-group";
import { profileFor, type ItemProfile } from "../item-profiles";

type ReferenceOption = components["schemas"]["InventoryReferenceOptionDto"];

/** What the item is, first: a kind of item, then one of its categories. The rest of the form follows from the pick. */
export function KindPicker({ profiles, categories, category, onCategory }: {
  profiles: readonly ItemProfile[];
  categories: readonly ReferenceOption[];
  category: string;
  onCategory: (category: string) => void;
}) {
  const kinds = profiles.filter((profile) => profile.key !== "general");
  const [picked, setPicked] = useState<string>(() => profileFor(profiles, category).key ?? "");
  const kind = kinds.find((profile) => profile.key === (category ? profileFor(profiles, category).key : picked));
  const inKind = categories.filter(
    (option) => option.value && !option.isLegacy && kind?.categories?.some((entry) => entry.toLowerCase() === option.value!.toLowerCase()),
  );

  return (
    <div className="space-y-3">
      <div role="group" aria-label="Kind of item" className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
        {kinds.map((profile) => {
          const selected = profile.key === kind?.key;
          return (
            <button
              key={profile.key}
              type="button"
              aria-pressed={selected}
              onClick={() => {
                setPicked(profile.key ?? "");
                const only = categories.filter((option) => option.value && !option.isLegacy && profile.categories?.some((entry) => entry.toLowerCase() === option.value!.toLowerCase()));
                if (only.length === 1 && only[0]?.value) onCategory(only[0].value);
                else if (category && profileFor(profiles, category).key !== profile.key) onCategory("");
              }}
              className={`min-h-11 rounded-lg border px-3 py-2 text-left text-sm transition-colors ${selected ? "border-primary bg-primary/10 font-semibold" : "hover:bg-muted/60"}`}
            >
              {profile.label}
            </button>
          );
        })}
      </div>
      {kind && inKind.length > 1 && (
        <ChoiceGroup
          label={`${kind.label} category`}
          name="kindCategory"
          choices={inKind.map((option) => ({ value: option.value!, label: option.label ?? option.value! }))}
          value={inKind.find((option) => option.value!.toLowerCase() === category.toLowerCase())?.value ?? ""}
          onChange={onCategory}
        />
      )}
    </div>
  );
}
