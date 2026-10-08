/** Profile details, stored on the item as a JSON object: text, or for a list detail (materials) a list of text. */
export type AttributeMap = Record<string, string | string[]>;

/**
 * Reads the stored attribute JSON, tolerating anything that is not the shape we expect.
 *
 * The value comes back from the API as free-form text, so a null, a legacy string, an array or an
 * object holding numbers all have to degrade to "no attributes" rather than crash the form.
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
        const list = entry.filter((item): item is string => typeof item === 'string');
        if (list.length > 0) attributes[key] = list;
      }
    }
    return attributes;
  } catch {
    return {};
  }
}

/** Writes the attribute JSON back, dropping blanks so an untouched form stores nothing at all. */
export function serializeAttributes(value: AttributeMap): string | null {
  const cleaned: AttributeMap = {};
  for (const [key, entry] of Object.entries(value)) {
    if (Array.isArray(entry)) {
      if (entry.length > 0) cleaned[key] = entry;
    } else if (entry.trim()) cleaned[key] = entry.trim();
  }
  return Object.keys(cleaned).length > 0 ? JSON.stringify(cleaned) : null;
}

/** Whether a detail holds an answer: text with something in it, or a list with an entry. */
export function hasAttributeValue(entry: string | string[] | undefined): boolean {
  return Array.isArray(entry) ? entry.length > 0 : Boolean(entry?.trim());
}
