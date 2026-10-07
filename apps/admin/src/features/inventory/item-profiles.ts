import type { components } from "@overhaul/api-client";
import { readListInput } from "@/lib/list-input";

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

export type ItemAttribute = Schemas["ItemAttributeDto"];
export type AttributeValues = Record<string, string | string[]>;

const ATTRIBUTE_PREFIX = "attr.";
const LIST_PREFIX = "attrs.";

/** A detail that takes several terms from its list, such as materials. */
export function isListAttribute(attribute: ItemAttribute): boolean {
  return (attribute.maxValues ?? 1) > 1 && (attribute.options?.length ?? 0) > 0;
}

/** The input a detail posts under: one value, or for a list, the JSON array a `MultiCombobox` writes. */
export function attributeInputName(attribute: ItemAttribute): string {
  return `${isListAttribute(attribute) ? LIST_PREFIX : ATTRIBUTE_PREFIX}${attribute.key}`;
}

/** The item's stored details as a plain map of text and lists of text; anything unreadable reads as none. */
export function readAttributes(json: string | null | undefined): AttributeValues {
  if (!json) return {};
  try {
    const parsed: unknown = JSON.parse(json);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return {};
    const values: AttributeValues = {};
    for (const [key, value] of Object.entries(parsed)) {
      if (typeof value === "string") values[key] = value;
      else if (Array.isArray(value)) values[key] = value.filter((entry): entry is string => typeof entry === "string");
    }
    return values;
  } catch {
    return {};
  }
}

/** The details among posted form entries, keyed without their input prefix. */
export function pickAttributes(entries: Iterable<[string, unknown]>): AttributeValues {
  const attributes: AttributeValues = {};
  for (const [name, value] of entries) {
    if (typeof value !== "string") continue;
    if (name.startsWith(LIST_PREFIX)) {
      const list = readListInput(value);
      if (list && list.length > 0) attributes[name.slice(LIST_PREFIX.length)] = list;
    } else if (name.startsWith(ATTRIBUTE_PREFIX) && value.trim()) {
      attributes[name.slice(ATTRIBUTE_PREFIX.length)] = value.trim();
    }
  }
  return attributes;
}

/** The details a form posted, as the JSON the API stores. The API keeps only the keys the item's profile defines. */
export function attributesFromForm(formData: FormData): string | null {
  const attributes = pickAttributes(formData.entries());
  return Object.keys(attributes).length > 0 ? JSON.stringify(attributes) : null;
}
