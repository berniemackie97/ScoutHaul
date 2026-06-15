import { describe, it, expect } from "vitest";
import { cn, formatCurrency, formatDate, truncate } from "../src/lib/utils";

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
