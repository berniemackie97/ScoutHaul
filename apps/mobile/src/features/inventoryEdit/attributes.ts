/** Category-specific fields, stored on the item as a JSON object of string values. */
export type AttributeMap = Record<string, string>;

/**
 * Reads the stored attribute JSON, tolerating anything that is not the shape we expect.
 *
 * The value comes back from the API as free-form text, so a null, a legacy string, an array or an
 * object holding numbers all have to degrade to "no attributes" rather than crash the form. A detail
 * stored as a list (materials) reads as one line, "Silk, Cotton"; the API reads that line back into
 * the list on save, so editing it here keeps every entry.
 */
export function parseAttributes(value?: string | null): AttributeMap {
  if (!value) return {};
  try {
    const parsed = JSON.parse(value) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {};
    const attributes: AttributeMap = {};
    for (const [key, entry] of Object.entries(parsed as Record<string, unknown>)) {
      if (typeof entry === 'string') attributes[key] = entry;
      else if (Array.isArray(entry)) {
        const text = entry.filter((item): item is string => typeof item === 'string').join(', ');
        if (text) attributes[key] = text;
      }
    }
    return attributes;
  } catch {
    return {};
  }
}

/** Writes the attribute JSON back, dropping blanks so an untouched form stores nothing at all. */
export function serializeAttributes(value: AttributeMap): string | null {
  const cleaned = Object.fromEntries(
    Object.entries(value)
      .map(([key, entry]) => [key, entry.trim()])
      .filter(([, entry]) => entry.length > 0),
  );
  return Object.keys(cleaned).length > 0 ? JSON.stringify(cleaned) : null;
}
