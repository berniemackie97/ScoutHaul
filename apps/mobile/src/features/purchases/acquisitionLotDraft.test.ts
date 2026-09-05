import {
  emptyAcquisitionLotDraft,
  landedTotal,
  localDateValue,
  validateAcquisitionLotDraft,
  type AcquisitionLotDraft,
} from './acquisitionLotDraft';

const draft = (overrides: Partial<AcquisitionLotDraft> = {}): AcquisitionLotDraft => ({
  ...emptyAcquisitionLotDraft('2026-09-05'),
  name: 'Estate sale — Maple St',
  purchaseSubtotal: '120',
  ...overrides,
});

const request = (input: AcquisitionLotDraft, today = '2026-09-05') => {
  const result = validateAcquisitionLotDraft(input, today);
  if (!result.ok) throw new Error(`expected a valid draft, got ${JSON.stringify(result.errors)}`);
  return result.request;
};

const errors = (input: AcquisitionLotDraft, today = '2026-09-05') => {
  const result = validateAcquisitionLotDraft(input, today);
  if (result.ok) throw new Error('expected the draft to be refused');
  return result.errors;
};

describe('validateAcquisitionLotDraft', () => {
  it('builds a costs-only lot, which is what a purchase is at the till', () => {
    // Items come later. The money is known when you pay; what was in the pile is not.
    expect(request(draft({ shippingCost: '15', taxCost: '9.60' }))).toMatchObject({
      name: 'Estate sale — Maple St',
      purchaseSubtotal: 120,
      shippingCost: 15,
      taxCost: 9.6,
      feeCost: 0,
      otherCost: 0,
      allocationMethod: 'Equal',
      items: [],
    });
  });

  it('needs a name to be findable later', () => {
    expect(errors(draft({ name: '  ' }))).toMatchObject({ name: expect.any(String) });
  });

  it('needs the purchase to have cost something', () => {
    expect(errors(draft({ purchaseSubtotal: '' }))).toMatchObject({
      purchaseSubtotal: expect.any(String),
    });
  });

  it('accepts a lot whose whole cost is shipping and fees', () => {
    expect(request(draft({ purchaseSubtotal: '0', shippingCost: '22' }))).toMatchObject({
      purchaseSubtotal: 0,
      shippingCost: 22,
    });
  });

  it('refuses a negative cost rather than sending it', () => {
    expect(errors(draft({ shippingCost: '-5' }))).toMatchObject({ shippingCost: expect.any(String) });
  });

  it('refuses an amount that is not a number', () => {
    expect(errors(draft({ taxCost: '9,60' }))).toMatchObject({ taxCost: expect.any(String) });
  });

  it('refuses a purchase dated in the future', () => {
    expect(errors(draft({ purchasedAt: '2026-09-06' }))).toMatchObject({
      purchasedAt: expect.any(String),
    });
  });

  it('accepts a purchase dated today', () => {
    // The one that matters: this is every purchase recorded in the field.
    expect(request(draft({ purchasedAt: '2026-09-05' })).purchasedAt).toBe('2026-09-05T12:00:00.000Z');
  });

  it('sends the day as midday so it cannot land on the day before', () => {
    // UTC midnight is the previous calendar day for everyone west of London.
    const sent = request(draft({ purchasedAt: '2026-01-15' })).purchasedAt!;
    expect(new Date(sent).toISOString()).toBe('2026-01-15T12:00:00.000Z');
  });

  it('refuses a date it cannot read instead of guessing', () => {
    expect(errors(draft({ purchasedAt: '15/01/2026' }))).toMatchObject({
      purchasedAt: expect.any(String),
    });
  });

  it('treats a cleared date as no date', () => {
    expect(request(draft({ purchasedAt: '' })).purchasedAt).toBeNull();
  });

  it('sends blank optional text as null rather than empty strings', () => {
    expect(request(draft())).toMatchObject({
      supplierName: null,
      externalReference: null,
      notes: null,
    });
  });

  it('trims what it does send', () => {
    expect(request(draft({ supplierName: '  Maple St Estate  ', notes: ' box of tools ' }))).toMatchObject({
      supplierName: 'Maple St Estate',
      notes: 'box of tools',
    });
  });
});

describe('landedTotal', () => {
  it('adds every cost the lot carries', () => {
    expect(landedTotal(draft({ shippingCost: '15', taxCost: '9.6', feeCost: '3', otherCost: '2' }))).toBeCloseTo(149.6);
  });

  it('ignores a field still being typed rather than showing NaN', () => {
    expect(landedTotal(draft({ shippingCost: '-' }))).toBe(120);
  });
});

describe('localDateValue', () => {
  it('reads the calendar day where the phone is, not in UTC', () => {
    // 8pm on the 5th in New York is already the 6th in UTC; the purchase happened on the 5th.
    const evening = new Date('2026-09-06T00:30:00Z');
    jest.spyOn(Date.prototype, 'getTimezoneOffset').mockReturnValue(240);

    expect(localDateValue(evening)).toBe('2026-09-05');

    jest.restoreAllMocks();
  });
});
