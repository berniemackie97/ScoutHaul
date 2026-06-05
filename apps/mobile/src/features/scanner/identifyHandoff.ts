// Module-level handoff for returning identify results from the scanner back to
// a caller screen (e.g. inventory edit) via router.back(). expo-router cannot
// pass data *backwards* through navigation params, so the scanner stashes the
// resolved fields here and the caller reads them once on focus.
//
// Payload is the same flat string map the scanner builds for forward navigation
// (title/brand/model/category/color/condition/barcode/estimatedSalePrice/photoUri),
// so both paths share one shape.
export type IdentifyHandoff = Record<string, string>;

let pending: IdentifyHandoff | null = null;

export function setIdentifyHandoff(payload: IdentifyHandoff): void {
  pending = payload;
}

// Reads and clears the pending handoff. Returns null when nothing is waiting,
// so repeated focus events after the first consume are no-ops.
export function takeIdentifyHandoff(): IdentifyHandoff | null {
  const value = pending;
  pending = null;
  return value;
}
