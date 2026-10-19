import type { components } from "@overhaul/api-client";

export type PeriodStatus = components["schemas"]["SalesTaxPeriodStatus"];
export type IssueSeverity = components["schemas"]["Dr15IssueSeverity"];
export type Frequency = components["schemas"]["SalesTaxFrequency"];

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const plain = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: false });

/** Exact cents, always: a return is never rounded to the dollar. */
export const cents = (value: number | null | undefined) => money.format(value ?? 0);

/** What to type into eServices: no symbol, no grouping. */
export const entry = (value: number | null | undefined) => plain.format(value ?? 0);

export const percent = (rate: number | null | undefined) => `${((rate ?? 0) * 100).toFixed(rate && (rate * 1000) % 10 !== 0 ? 2 : 1)}%`;

/** A calendar day exactly as sent; never shifted by the browser's zone. */
export function day(value: string | null | undefined, style: "short" | "long" = "short") {
  if (!value) return "—";
  return new Intl.DateTimeFormat("en-US", {
    month: style === "long" ? "long" : "short",
    day: "numeric",
    year: "numeric",
    weekday: style === "long" ? "long" : undefined,
    timeZone: "UTC",
  }).format(new Date(`${value.slice(0, 10)}T00:00:00Z`));
}

export const statusLabel: Record<PeriodStatus, string> = {
  Open: "Period open",
  ReadyToFile: "Ready to file",
  DueSoon: "Due soon",
  Overdue: "Overdue",
  Filed: "Filed",
  FiledLate: "Filed late",
  ChangedSinceFiled: "Changed since filed",
};

export const statusVariant: Record<PeriodStatus, "secondary" | "info" | "warning" | "destructive" | "success"> = {
  Open: "secondary",
  ReadyToFile: "info",
  DueSoon: "warning",
  Overdue: "destructive",
  Filed: "success",
  FiledLate: "warning",
  ChangedSinceFiled: "destructive",
};

export const frequencyLabel: Record<Frequency, string> = {
  Monthly: "Monthly",
  Quarterly: "Quarterly",
  Semiannual: "Semiannual",
  Annual: "Annual",
};

export function countdown(days: number | undefined, status: PeriodStatus | undefined) {
  if (days === undefined || status === undefined) return "";
  if (status === "Filed" || status === "FiledLate" || status === "ChangedSinceFiled") return "";
  if (days < 0) return `${-days} day${days === -1 ? "" : "s"} late`;
  if (days === 0) return "Last day";
  return `${days} day${days === 1 ? "" : "s"} left`;
}

export const severityTone: Record<IssueSeverity, string> = {
  Blocking: "border-destructive/40 bg-destructive/5 text-destructive",
  Warning: "border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-700 dark:bg-amber-950/40 dark:text-amber-100",
  Info: "border-blue-200 bg-blue-50 text-blue-900 dark:border-blue-800 dark:bg-blue-950/40 dark:text-blue-100",
};
