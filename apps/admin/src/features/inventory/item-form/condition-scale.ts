import type { components } from "@overhaul/api-client";
import type { Choice } from "@/components/choice-group";
import type { ItemProfile } from "../item-profiles";

type ReferenceOption = components["schemas"]["InventoryReferenceOptionDto"];

/** Past this many, a list reads better as a search than as chips. */
export const CHIP_LIMIT = 12;

/**
 * The conditions offered for a kind of item: its own scale, in order, plus any the organization added (those have
 * no hint). A stored condition off the scale stays offered, marked, so opening and saving an item never drops it.
 */
export function conditionChoices(profile: ItemProfile, options: readonly ReferenceOption[], current: string): Choice[] {
  const listed = options.filter((option) => option.value && !option.isLegacy);
  const scale = profile.conditions ?? [];
  const onScale =
    scale.length === 0
      ? listed
      : [
          ...scale.flatMap((term) => listed.filter((option) => option.value!.toLowerCase() === term.toLowerCase())),
          ...listed.filter((option) => !option.hint),
        ];
  const choices: Choice[] = onScale.map((option) => ({ value: option.value!, label: option.label ?? option.value!, hint: option.hint }));
  const wanted = current.trim().toLowerCase();
  if (wanted && !choices.some((choice) => choice.value.toLowerCase() === wanted)) {
    const stored = listed.find((option) => option.value!.toLowerCase() === wanted);
    choices.push({ value: stored?.value ?? current.trim(), label: stored?.label ?? current.trim(), hint: `Not on the ${profile.label ?? "item"} scale` });
  }
  return choices;
}
