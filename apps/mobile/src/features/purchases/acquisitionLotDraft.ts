import type { UpsertAcquisitionLotRequest } from '../../api/acquisitionRequests';
import type { AcquisitionLotAllocationMethod } from '../../domain/types';
import { parseOptionalValidatedNumber } from '../inventoryEdit/validation';

/**
 * A purchase as it is being typed: every money field is still a string, because that is what a
 * keyboard produces and because "" has to stay distinguishable from 0 while the form is open.
 */
export type AcquisitionLotDraft = {
  name: string;
  supplierName: string;
  /** Calendar day, `YYYY-MM-DD`, as the date field on this app writes it. */
  purchasedAt: string;
  purchaseSubtotal: string;
  shippingCost: string;
  taxCost: string;
  feeCost: string;
  otherCost: string;
  allocationMethod: AcquisitionLotAllocationMethod;
  externalReference: string;
  notes: string;
};

export const emptyAcquisitionLotDraft = (today: string): AcquisitionLotDraft => ({
  name: '',
  supplierName: '',
  purchasedAt: today,
  purchaseSubtotal: '',
  shippingCost: '',
  taxCost: '',
  feeCost: '',
  otherCost: '',
  // Equal is the only method that means anything before the lot has been itemized, and it is what
  // the field capture is: a pile bought for a number, split later.
  allocationMethod: 'Equal',
  externalReference: '',
  notes: '',
});

const COST_FIELDS = [
  ['purchaseSubtotal', 'Subtotal'],
  ['shippingCost', 'Shipping'],
  ['taxCost', 'Tax'],
  ['feeCost', 'Fees'],
  ['otherCost', 'Other costs'],
] as const;

type CostField = (typeof COST_FIELDS)[number][0];

/** What the lot cost all in — the number the allocation later divides up. */
export function landedTotal(draft: AcquisitionLotDraft): number {
  return COST_FIELDS.reduce((sum, [field]) => {
    const parsed = Number(draft[field].trim());
    return sum + (Number.isFinite(parsed) ? parsed : 0);
  }, 0);
}

export type DraftValidation =
  | { ok: true; request: UpsertAcquisitionLotRequest }
  | { ok: false; errors: Partial<Record<keyof AcquisitionLotDraft, string>> };

/**
 * Turns a typed draft into a request, or into the sentences to show beside the fields.
 *
 * These checks mirror the server's, so someone standing in a driveway with no signal finds out that
 * the total is zero now — while the receipt is still in their hand — rather than when the queued
 * write is finally rejected hours later.
 *
 * `today` is the local calendar day, not an instant, because that is what the person typing means by
 * "today" and comparing `YYYY-MM-DD` strings cannot drift across a timezone the way two Dates can.
 */
export function validateAcquisitionLotDraft(
  draft: AcquisitionLotDraft,
  today: string = localDateValue(),
): DraftValidation {
  const errors: Partial<Record<keyof AcquisitionLotDraft, string>> = {};

  if (!draft.name.trim()) {
    errors.name = 'Give this purchase a name so you can find it again.';
  }

  const costs = {} as Record<CostField, number>;
  for (const [field, label] of COST_FIELDS) {
    const parsed = parseOptionalValidatedNumber(draft[field], label, { min: 0 });
    if (parsed.error) errors[field] = parsed.error;
    costs[field] = parsed.value ?? 0;
  }

  const day = draft.purchasedAt.trim();
  const purchasedAt = parsePurchasedAt(day);
  if (day && !purchasedAt) {
    errors.purchasedAt = 'Enter the date as YYYY-MM-DD.';
  } else if (purchasedAt && day > today) {
    errors.purchasedAt = 'Purchase date cannot be in the future.';
  }

  const total = COST_FIELDS.reduce((sum, [field]) => sum + costs[field], 0);
  if (!errors.purchaseSubtotal && total <= 0) {
    errors.purchaseSubtotal = 'A purchase needs a cost greater than zero.';
  }

  if (Object.keys(errors).length > 0) {
    return { ok: false, errors };
  }

  return {
    ok: true,
    request: {
      name: draft.name.trim(),
      purchasedAt: purchasedAt ? purchasedAt.toISOString() : null,
      purchaseSubtotal: costs.purchaseSubtotal,
      shippingCost: costs.shippingCost,
      taxCost: costs.taxCost,
      feeCost: costs.feeCost,
      otherCost: costs.otherCost,
      allocationMethod: draft.allocationMethod,
      supplierName: draft.supplierName.trim() || null,
      sourceLocationId: null,
      sourcingTripId: null,
      sourcingTripStopId: null,
      externalReference: draft.externalReference.trim() || null,
      notes: draft.notes.trim() || null,
      // Costs-only for now. The lot is itemized afterwards, which is the order the buying happens in.
      items: [],
    },
  };
}

/**
 * Reads the calendar day as midday UTC.
 *
 * Midnight is the trap: a date sent as UTC midnight is the previous day everywhere west of London,
 * so a purchase made on the 15th files itself under the 14th and the server may read it as future.
 */
function parsePurchasedAt(value: string): Date | null {
  const trimmed = value.trim();
  if (!/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) return null;
  const parsed = new Date(`${trimmed}T12:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/** Today where the phone is, as `YYYY-MM-DD`. */
export function localDateValue(now: Date = new Date()): string {
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
}
