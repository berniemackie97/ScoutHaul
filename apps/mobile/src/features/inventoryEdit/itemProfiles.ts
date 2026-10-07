import type { ItemAttribute, ItemField, ItemFieldSpec, ItemProfile } from '../../api/inventory';

/** Before a category is chosen, and for one the API does not know: only the fields every item has. */
export const GENERAL_PROFILE: ItemProfile = {
  key: 'general',
  label: 'General',
  categories: [],
  fields: [],
  attributes: [],
  sizeSystems: [],
  brandLabel: 'Brand',
};

/** The profile a category belongs to, from the profiles the API serves. */
export function profileFor(profiles: readonly ItemProfile[] | null | undefined, category: string | null | undefined): ItemProfile {
  if (!category) return GENERAL_PROFILE;
  const wanted = category.toLowerCase();
  return profiles?.find((profile) => profile.categories.some((entry) => entry.toLowerCase() === wanted)) ?? GENERAL_PROFILE;
}

export function fieldSpec(profile: ItemProfile, field: ItemField): ItemFieldSpec | undefined {
  return profile.fields.find((spec) => spec.field === field);
}

/** A detail that takes several terms from its list, such as materials. */
export function isListAttribute(attribute: ItemAttribute): boolean {
  return attribute.maxValues > 1 && (attribute.options?.length ?? 0) > 0;
}
