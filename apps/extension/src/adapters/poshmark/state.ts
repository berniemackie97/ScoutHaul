import type { PageResponse } from "../types";
import { siteChanged } from "./pages";

/** The global Poshmark renders its pages from. Its script removes itself once run, so it is read from the page, not the HTML. */
export const stateGlobal = "__INITIAL_STATE__";

export function readState(response: PageResponse, what: string): Record<string, unknown> {
  if (!response.global) {
    throw siteChanged(what);
  }

  try {
    const state: unknown = JSON.parse(response.global);
    if (state === null || typeof state !== "object") {
      throw siteChanged(what);
    }

    return state as Record<string, unknown>;
  } catch {
    throw siteChanged(what);
  }
}
