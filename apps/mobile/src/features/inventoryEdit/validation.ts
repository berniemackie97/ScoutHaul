/**
 * Reads an optional numeric field, returning either the number or the sentence to show the user.
 *
 * Blank is valid and means "not given" — these fields are optional, and an empty box must not read as
 * an error. Anything else that is not a usable number is refused with a message naming the field,
 * because a form that silently drops a mistyped price is worse than one that says so.
 */
export function parseOptionalValidatedNumber(
  value: string,
  label: string,
  rules: { min?: number; max?: number; integer?: boolean } = {},
): { value: number | null; error: string | null } {
  const trimmed = value.trim();
  if (!trimmed) return { value: null, error: null };

  const parsed = Number(trimmed);
  if (!Number.isFinite(parsed)) {
    return { value: null, error: `${label} must be a valid number.` };
  }

  if (rules.integer && !Number.isInteger(parsed)) {
    return { value: null, error: `${label} must be a whole number.` };
  }

  if (rules.min !== undefined && parsed < rules.min) {
    return { value: null, error: `${label} must be at least ${rules.min}.` };
  }

  if (rules.max !== undefined && parsed > rules.max) {
    return { value: null, error: `${label} must be ${rules.max} or less.` };
  }

  return { value: parsed, error: null };
}
