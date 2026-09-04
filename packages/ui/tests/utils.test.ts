import { describe, it, expect } from "vitest";
import { ABSENT, cn, formatCurrency, formatDate, formatDateTime, truncate } from "../src/lib/utils";

describe("cn", () => {
  it("merges class names", () => {
    expect(cn("foo", "bar")).toBe("foo bar");
  });

  it("handles conditional classes", () => {
    expect(cn("base", false && "hidden", "visible")).toBe("base visible");
  });

  it("merges tailwind conflicts", () => {
    expect(cn("p-4", "p-6")).toBe("p-6");
  });
});

describe("formatCurrency", () => {
  it("formats number as USD", () => {
    expect(formatCurrency(29.99)).toBe("$29.99");
  });

  it("formats string as USD", () => {
    expect(formatCurrency("150")).toBe("$150.00");
  });

  it("handles zero", () => {
    expect(formatCurrency(0)).toBe("$0.00");
  });

  it("formats large numbers with commas", () => {
    expect(formatCurrency(1234.56)).toBe("$1,234.56");
  });
});

describe("formatDate", () => {
  it("formats date string", () => {
    const result = formatDate("2026-01-15");
    expect(result).toContain("Jan");
    expect(result).toContain("15");
    expect(result).toContain("2026");
  });

  it("formats Date object", () => {
    const result = formatDate(new Date("2026-06-01"));
    expect(result).toContain("2026");
  });
});

describe("truncate", () => {
  it("truncates long strings", () => {
    expect(truncate("Hello World", 5)).toBe("Hello...");
  });

  it("leaves short strings alone", () => {
    expect(truncate("Hi", 10)).toBe("Hi");
  });

  it("handles exact length", () => {
    expect(truncate("Hello", 5)).toBe("Hello");
  });
});

// ── Absence, and the local-day rule ───────────────────────────────────────────

describe("formatCurrency", () => {
  it("formats an amount the way an operator reads it", () => {
    expect(formatCurrency(1234.5)).toBe("$1,234.50");
    expect(formatCurrency("42.75")).toBe("$42.75");
  });

  it("shows absence as absence, not as free", () => {
    // Admin had 32 copies of this and they disagreed: most showed a dash, two showed $0.00. A
    // missing purchase price means nobody recorded one — saying the item was free is a different
    // and wrong claim.
    expect(formatCurrency(null)).toBe(ABSENT);
    expect(formatCurrency(undefined)).toBe(ABSENT);
    expect(formatCurrency("")).toBe(ABSENT);
  });

  it("still formats a real zero", () => {
    expect(formatCurrency(0)).toBe("$0.00");
  });

  it("lets a caller ask for something else when nothing is there", () => {
    // Aggregates on the dashboard genuinely are nought when there are no sales.
    expect(formatCurrency(null, { whenAbsent: "$0" })).toBe("$0");
    expect(formatCurrency(null, { whenAbsent: "" })).toBe("");
  });

  it("drops the cents when asked, for tiles that show round numbers", () => {
    expect(formatCurrency(1234.5, { maximumFractionDigits: 0 })).toBe("$1,235");
  });

  it("does not render nonsense for a value that is not a number", () => {
    expect(formatCurrency("not a number")).toBe(ABSENT);
    expect(formatCurrency(Number.NaN)).toBe(ABSENT);
    expect(formatCurrency(Number.POSITIVE_INFINITY)).toBe(ABSENT);
  });
});

describe("formatDate", () => {
  it("formats a date an operator can scan", () => {
    expect(formatDate("2026-09-04T12:00:00Z")).toMatch(/Sep \d, 2026/);
    expect(formatDate(new Date("2026-09-04T12:00:00Z"))).toMatch(/Sep \d, 2026/);
  });

  it("shows absence rather than the epoch", () => {
    expect(formatDate(null)).toBe(ABSENT);
    expect(formatDate(undefined)).toBe(ABSENT);
    expect(formatDate("")).toBe(ABSENT);
  });

  it("shows absence rather than 'Invalid Date' in the middle of a table", () => {
    expect(formatDate("not a date")).toBe(ABSENT);
  });
});

describe("formatDateTime", () => {
  it("includes the time, for audit trails where the hour matters", () => {
    expect(formatDateTime("2026-09-04T12:00:00Z")).toMatch(/2026/);
    expect(formatDateTime("2026-09-04T12:00:00Z")).toMatch(/\d{1,2}:\d{2}/);
  });

  it("shows absence the same way", () => {
    expect(formatDateTime(null)).toBe(ABSENT);
    expect(formatDateTime("not a date")).toBe(ABSENT);
  });
});
