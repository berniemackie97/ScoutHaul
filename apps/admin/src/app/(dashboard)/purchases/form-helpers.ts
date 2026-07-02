// Shared parsing/validation helpers for the purchases (acquisition lot) server actions.
// Kept in a plain module (no "use server") so both the list and detail actions can import them.

export function textField(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

/** Parses a non-negative money amount. Returns NaN when the value is present but invalid. */
export function moneyField(formData: FormData, name: string): number {
  const raw = textField(formData, name);
  if (raw === null) return 0;
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : Number.NaN;
}

/** Extracts an API problem-details message, falling back to a caller-supplied default. */
export function apiErrorMessage(error: unknown, fallback: string): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return fallback;
}

/**
 * Normalizes a yyyy-MM-dd date input to an ISO instant at midday UTC (avoids off-by-one from
 * timezone shifts). Returns null for an invalid or future date.
 */
export function purchasedAtIso(value: string | null): string | null {
  if (!value) return new Date().toISOString();
  if (value > new Date().toISOString().slice(0, 10)) return null;
  const parsed = new Date(`${value}T12:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}
