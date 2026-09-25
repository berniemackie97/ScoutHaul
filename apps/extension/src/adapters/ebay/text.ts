import { cents } from "../amounts";

/**
 * eBay's printed values: money like "$20.00" or "+$6.99 | shipping", and sale times like
 * "Sep 18 | 12:48am PDT" — no year, in the seller's time zone. Strict: anything unexpected is null,
 * never a guess, so a changed format surfaces as a rejected record rather than a wrong number.
 */

const months: Record<string, number> = {
  jan: 0, feb: 1, mar: 2, apr: 3, may: 4, jun: 5, jul: 6, aug: 7, sep: 8, oct: 9, nov: 10, dec: 11,
};

/** Hours from UTC for the zone abbreviations eBay prints for US sellers. */
const zones: Record<string, number> = {
  PST: -8, PDT: -7, MST: -7, MDT: -6, CST: -6, CDT: -5, EST: -5, EDT: -4,
  AKST: -9, AKDT: -8, HST: -10, GMT: 0, UTC: 0,
};

/** "$1,234.50", "+$6.99 | shipping", "-$0.52" → a number; "Free shipping" → 0; anything else → null. */
export function money(text: string | null | undefined): number | null {
  if (text === null || text === undefined) {
    return null;
  }

  const printed = (text.split("|")[0] ?? "").trim();
  if (/^free\b/i.test(printed)) {
    return 0;
  }

  const match = /^([+-])?\s*(?:US\s*)?\$\s*([\d,]+(?:\.\d{1,2})?)$/.exec(printed);
  if (!match) {
    return null;
  }

  const value = cents(Number(match[2]!.replace(/,/g, "")));
  return match[1] === "-" ? -value : value;
}

/**
 * A sale time as the order list prints it — "Sep 18 | 12:48am PDT" — as an instant. eBay leaves the
 * year out; it is the latest year that does not put the sale in the future. Null for anything else,
 * including a bare date with no time or an unknown zone.
 */
export function saleTime(text: string | null | undefined, now: Date): Date | null {
  if (!text) {
    return null;
  }

  const match = /^([A-Za-z]{3})[a-z]*\.?\s+(\d{1,2})(?:,\s*(\d{4}))?\s*\|?\s*(\d{1,2}):(\d{2})\s*([ap])\.?m\.?\s+([A-Za-z]{2,4})$/i.exec(text.trim());
  if (!match) {
    return null;
  }

  const month = months[match[1]!.toLowerCase()];
  const offset = zones[match[7]!.toUpperCase()];
  if (month === undefined || offset === undefined) {
    return null;
  }

  const day = Number(match[2]);
  let hour = Number(match[4]) % 12;
  if (match[6]!.toLowerCase() === "p") {
    hour += 12;
  }

  const minute = Number(match[5]);
  const at = (year: number) => new Date(Date.UTC(year, month, day, hour - offset, minute));
  if (match[3]) {
    return at(Number(match[3]));
  }

  // A sale is never in the future; allow a day for clocks and zones.
  const thisYear = at(now.getUTCFullYear());
  return thisYear.getTime() > now.getTime() + 24 * 60 * 60 * 1000 ? at(now.getUTCFullYear() - 1) : thisYear;
}

/** The first eBay order number in a piece of text: "Order 26-15152-72360" → "26-15152-72360". */
export function orderNumber(text: string): string | null {
  return /\b(\d{2}-\d{5}-\d{5})\b/.exec(text)?.[1] ?? null;
}

/** yyyy-mm-dd in UTC, the form eBay's ledger filter takes. */
export function isoDay(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/**
 * A day as eBay writes it beside a status — "Sep 22", or "Sep 22, 2025". No time of day is given, so the day is
 * taken at noon UTC: near enough for a delivery date, and far enough from midnight that no time zone moves it.
 * A day that would fall in the future belongs to last year.
 */
export function dayFrom(text: string | null | undefined, now: Date): Date | null {
  const match = /([A-Za-z]{3})[a-z]*\.?\s+(\d{1,2})(?:,\s*(\d{4}))?/.exec((text ?? "").trim());
  if (!match) {
    return null;
  }

  const month = months[match[1]!.toLowerCase()];
  if (month === undefined) {
    return null;
  }

  const day = Number(match[2]);
  const year = match[3] ? Number(match[3]) : now.getUTCFullYear();
  const at = new Date(Date.UTC(year, month, day, 12));
  return !match[3] && at.getTime() > now.getTime() + 2 * 24 * 60 * 60 * 1000
    ? new Date(Date.UTC(year - 1, month, day, 12))
    : at;
}
