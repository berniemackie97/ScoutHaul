import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/** What a formatter shows when it has nothing to show. An em dash, not a zero. */
export const ABSENT = "—";

/**
 * Turns a value into a Date, reading a date-only string as a local day.
 *
 * `new Date("2026-01-15")` is midnight **UTC**, which renders as the 14th anywhere west of it — so a
 * purchase dated the 15th showed as the 14th for the whole of the Americas. A date with no time in
 * it is a calendar day, not an instant, so it is built in local time. Strings that do carry a time
 * are instants and are left alone.
 */
function toDate(value: Date | string): Date {
  if (value instanceof Date) return value;

  const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (dateOnly) {
    const [, year, month, day] = dateOnly;
    return new Date(Number(year), Number(month) - 1, Number(day));
  }
  return new Date(value);
}

/**
 * Money, as an operator reads it.
 *
 * Absence is not zero. A missing purchase price means nobody recorded one; rendering it as $0.00
 * says the item was free, which is a different and wrong claim. Callers that genuinely want a zero —
 * an aggregate where "no sales" really is nought — pass `whenAbsent: "$0"` or a digit count and
 * handle it deliberately.
 */
export function formatCurrency(
  amount: number | string | null | undefined,
  options: { whenAbsent?: string; maximumFractionDigits?: number } = {},
): string {
  const { whenAbsent = ABSENT, maximumFractionDigits } = options;
  if (amount === null || amount === undefined || amount === "") return whenAbsent;

  const value = Number(amount);
  if (!Number.isFinite(value)) return whenAbsent;

  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
    ...(maximumFractionDigits === undefined ? {} : { maximumFractionDigits }),
  }).format(value);
}

/** A date an operator can scan in a table. Absence shows as {@link ABSENT}, never as the epoch. */
export function formatDate(
  date: Date | string | null | undefined,
  options: { whenAbsent?: string } = {},
): string {
  const { whenAbsent = ABSENT } = options;
  if (date === null || date === undefined || date === "") return whenAbsent;

  const parsed = toDate(date);
  // An unparseable string used to render as "Invalid Date" in the middle of a table.
  if (Number.isNaN(parsed.getTime())) return whenAbsent;

  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(parsed);
}

/** A date with the time, for audit trails where the hour matters. */
export function formatDateTime(
  date: Date | string | null | undefined,
  options: { whenAbsent?: string } = {},
): string {
  const { whenAbsent = ABSENT } = options;
  if (date === null || date === undefined || date === "") return whenAbsent;

  const parsed = toDate(date);
  if (Number.isNaN(parsed.getTime())) return whenAbsent;

  return new Intl.DateTimeFormat("en-US", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(parsed);
}

export function truncate(str: string, maxLength: number): string {
  if (str.length <= maxLength) return str;
  return str.slice(0, maxLength) + "...";
}
