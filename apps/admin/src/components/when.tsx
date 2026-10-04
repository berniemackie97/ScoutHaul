"use client";

import { useSyncExternalStore } from "react";

const noop = () => () => {};

/** A timestamp in the operator's own time zone. The server renders in UTC, so this formats in the browser. */
export function When({
  value,
  time = false,
  fallback = "—",
  className,
}: {
  value: string | null | undefined;
  time?: boolean;
  fallback?: string;
  className?: string;
}) {
  const label = useSyncExternalStore(
    noop,
    () => format(value, time),
    () => null,
  );
  if (!value) return <span className={className}>{fallback}</span>;
  return (
    <time dateTime={value} className={className} suppressHydrationWarning>
      {label ?? ""}
    </time>
  );
}

function format(value: string | null | undefined, time: boolean) {
  if (!value) return null;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return null;
  return new Intl.DateTimeFormat(undefined, time ? { dateStyle: "medium", timeStyle: "short" } : { dateStyle: "medium" }).format(parsed);
}
