"use client";

import { useSyncExternalStore } from "react";

/**
 * Today's date as the operator's own clock sees it.
 *
 * The server renders in UTC, so a date formatted there is tomorrow's for anyone in the Americas
 * after late afternoon. This renders nothing on the server and the local date once in the browser.
 */
export function LocalDate({ className }: { className?: string }) {
  const label = useSyncExternalStore(
    () => () => {},
    () => new Intl.DateTimeFormat(undefined, { weekday: "long", month: "long", day: "numeric" }).format(new Date()),
    () => null,
  );
  return label ? <p className={className}>{label}</p> : null;
}
