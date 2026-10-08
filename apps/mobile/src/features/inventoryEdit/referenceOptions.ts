import type { InventoryReferenceOption } from '../../api/inventory';
import type { SelectOption } from '../../components/SelectField';

/**
 * The choices for a picker: what the server offers, or a built-in list when it has not answered.
 *
 * An item can hold a value the current list no longer contains — a discontinued brand, a category
 * that was renamed. Dropping it would silently rewrite the item the moment anything else on the form
 * was saved, so the existing value is appended and labelled as such.
 */
export function referenceOptions(
  remote: InventoryReferenceOption[] | undefined,
  fallback: SelectOption[],
  current?: string | readonly string[],
): SelectOption[] {
  const options = remote?.map((option) => ({
    value: option.value,
    label: option.label,
    group: option.group ?? undefined,
  })) ?? fallback;
  const missing = (typeof current === 'string' ? [current] : (current ?? [])).filter(
    (value) => value && !options.some((option) => option.value === value),
  );
  return [...options, ...missing.map((value) => ({ value, label: `${value} (existing value)`, group: 'Existing data' }))];
}
