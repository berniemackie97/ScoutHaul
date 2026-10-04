import type { components } from "@overhaul/api-client";

type Schemas = components["schemas"];
export type ItemProfile = Schemas["ItemProfileDto"];
export type ItemField = Schemas["ItemField"];
export type ItemFieldSpec = Schemas["ItemFieldDto"];

/** Before a category is chosen, and the answer for one the API does not know: only the common fields. */
export const GENERAL_PROFILE: ItemProfile = { key: "general", label: "General", categories: [], fields: [], attributes: [], sizeSystems: [], brandLabel: "Brand" };

/** The profile a category belongs to. Profiles come from the API, which enforces the same table on save. */
export function profileFor(profiles: readonly ItemProfile[] | null | undefined, category: string | null | undefined): ItemProfile {
  if (!category) return GENERAL_PROFILE;
  const wanted = category.toLowerCase();
  return profiles?.find((profile) => profile.categories?.some((entry) => entry.toLowerCase() === wanted)) ?? GENERAL_PROFILE;
}

export function fieldSpec(profile: ItemProfile, field: ItemField): ItemFieldSpec | undefined {
  return profile.fields?.find((spec) => spec.field === field);
}

export function hasField(profile: ItemProfile, field: ItemField): boolean {
  return fieldSpec(profile, field) !== undefined;
}

const ATTRIBUTE_PREFIX = "attr.";

export function attributeInputName(key: string): string {
  return `${ATTRIBUTE_PREFIX}${key}`;
}

/** The item's stored details as a plain map; anything unreadable reads as none. */
export function readAttributes(json: string | null | undefined): Record<string, string> {
  if (!json) return {};
  try {
    const parsed: unknown = JSON.parse(json);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return {};
    return Object.fromEntries(Object.entries(parsed).filter((entry): entry is [string, string] => typeof entry[1] === "string"));
  } catch {
    return {};
  }
}

/** The details among posted form entries, keyed without their input prefix. */
export function pickAttributes(entries: Iterable<[string, unknown]>): Record<string, string> {
  const attributes: Record<string, string> = {};
  for (const [name, value] of entries) {
    if (!name.startsWith(ATTRIBUTE_PREFIX) || typeof value !== "string") continue;
    const trimmed = value.trim();
    if (trimmed) attributes[name.slice(ATTRIBUTE_PREFIX.length)] = trimmed;
  }
  return attributes;
}

/** The details a form posted, as the JSON the API stores. The API keeps only the keys the item's profile defines. */
export function attributesFromForm(formData: FormData): string | null {
  const attributes = pickAttributes(formData.entries());
  return Object.keys(attributes).length > 0 ? JSON.stringify(attributes) : null;
}
