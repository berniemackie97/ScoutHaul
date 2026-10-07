/** A list posted through one form field, as a `MultiCombobox` writes it: a JSON array of text. */
export function writeListInput(values: readonly string[]): string {
  return JSON.stringify(values);
}

/** The values a list field posted, or null when the form did not carry the field at all. */
export function readListInput(value: FormDataEntryValue | string | null | undefined): string[] | null {
  if (typeof value !== "string") return null;
  try {
    const parsed: unknown = JSON.parse(value);
    return Array.isArray(parsed) ? parsed.filter((entry): entry is string => typeof entry === "string") : null;
  } catch {
    return null;
  }
}
