/**
 * Reads JSON a page embeds in its HTML — the data a server-rendered page hydrates from.
 *
 * Marketplaces render their seller pages on the server and embed the same data as JSON for the page
 * to take over from. Reading that JSON is far steadier than reading the rendered table, and the
 * same code runs in any background context (a Chrome service worker has no DOM parser).
 */

/** The JSON object or array that starts at the first `{` or `[` at or after `from`. */
export function jsonAt(text: string, from: number): unknown {
  let start = -1;
  for (let index = from; index < text.length; index++) {
    const char = text[index];
    if (char === "{" || char === "[") {
      start = index;
      break;
    }
  }

  if (start < 0) {
    return undefined;
  }

  let depth = 0;
  let inString = false;
  let escaped = false;
  for (let index = start; index < text.length; index++) {
    const char = text[index];
    if (inString) {
      if (escaped) {
        escaped = false;
      } else if (char === "\\") {
        escaped = true;
      } else if (char === '"') {
        inString = false;
      }
      continue;
    }

    if (char === '"') {
      inString = true;
    } else if (char === "{" || char === "[") {
      depth++;
    } else if (char === "}" || char === "]") {
      depth--;
      if (depth === 0) {
        try {
          return JSON.parse(text.slice(start, index + 1));
        } catch {
          return undefined;
        }
      }
    }
  }

  return undefined;
}

/** The JSON value that follows `anchor` — e.g. `"ordersData":` — or undefined when the anchor is absent. */
export function jsonAfter(text: string, anchor: string): unknown {
  const at = text.indexOf(anchor);
  return at < 0 ? undefined : jsonAt(text, at + anchor.length);
}

/** Follows a path of keys through parsed JSON, returning undefined at the first gap. */
export function dig(value: unknown, ...path: (string | number)[]): unknown {
  let current: unknown = value;
  for (const key of path) {
    if (current === null || typeof current !== "object") {
      return undefined;
    }

    current = (current as Record<string | number, unknown>)[key];
  }

  return current;
}

/**
 * The words in one of the marketplace's rich-text values — `{ textSpans: [{ text }] }` — joined.
 * Null when there are none.
 */
export function spans(value: unknown, separator = " "): string | null {
  const list = dig(value, "textSpans");
  if (!Array.isArray(list)) {
    return null;
  }

  const text = list
    .map((span) => (typeof dig(span, "text") === "string" ? (dig(span, "text") as string) : ""))
    .filter((part) => part.trim().length > 0)
    .join(separator)
    .trim();
  return text.length > 0 ? text : null;
}
