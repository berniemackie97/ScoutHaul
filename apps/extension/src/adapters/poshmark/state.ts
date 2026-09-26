import { siteChanged } from "./pages";

/**
 * Poshmark renders its pages from a state object it writes into the HTML as `window.__INITIAL_STATE__`. Everything
 * a scan needs is in there already — who is signed in, and every listing on a closet page with its own fields — so
 * pages are read as they are served and no API is called.
 */
export function readState(html: string, what: string): Record<string, unknown> {
  const start = html.indexOf("__INITIAL_STATE__");
  if (start < 0) {
    throw siteChanged(what);
  }

  const opening = html.indexOf("{", start);
  if (opening < 0) {
    throw siteChanged(what);
  }

  const json = html.slice(opening, balanced(html, opening, what) + 1);
  try {
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    throw siteChanged(what);
  }
}

/** The index of the brace closing the object that opens at <paramref name="opening" />, strings and escapes aside. */
function balanced(html: string, opening: number, what: string): number {
  let depth = 0;
  let inString = false;
  let escaped = false;
  for (let at = opening; at < html.length; at++) {
    const character = html[at]!;
    if (escaped) {
      escaped = false;
      continue;
    }

    if (inString) {
      if (character === "\\") {
        escaped = true;
      } else if (character === '"') {
        inString = false;
      }

      continue;
    }

    if (character === '"') {
      inString = true;
    } else if (character === "{") {
      depth++;
    } else if (character === "}" && --depth === 0) {
      return at;
    }
  }

  throw siteChanged(what);
}
