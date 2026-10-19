import { basisLabel, compDateLabel, conditionFromGrade, parseSoldDay } from './labels';
import type { Comp, Price } from './types';

const comp = (overrides: Partial<Comp>): Comp => ({
  kind: 'Sold',
  source: 'eBay',
  title: 'Sony WH-1000XM5',
  price: 200,
  shipping: null,
  condition: null,
  url: null,
  date: null,
  verified: true,
  included: true,
  excludedBecause: null,
  ...overrides,
});

const price = (basis: Price['basis']): Price => ({
  basis,
  quickSale: null,
  list: null,
  high: null,
  confidence: 0,
  level: 'None',
  reasons: [],
});

describe('labels', () => {
  it('counts only the sales that priced the item', () => {
    const comps = [comp({}), comp({ kind: 'YourSale' }), comp({ included: false }), comp({ kind: 'Asking' })];

    expect(basisLabel(price('Sales'), comps)).toBe('2 matching sales');
    expect(basisLabel(price('Asking'), [comp({ kind: 'Asking' })])).toBe('1 current listing, no sales');
  });

  it("maps a photo's condition grade onto the editor's list", () => {
    expect(conditionFromGrade('like-new')).toBe('Excellent');
    expect(conditionFromGrade('for-parts')).toBe('For parts');
    expect(conditionFromGrade('mystery')).toBeNull();
    expect(conditionFromGrade(null)).toBeNull();
  });

  it('reads a typed sale date, treating blank as today and refusing the future', () => {
    const now = new Date('2026-10-18T15:00:00Z');

    expect(parseSoldDay('', now)).toBe(now.toISOString());
    expect(parseSoldDay('2026-10-02', now)).toBe('2026-10-02T12:00:00.000Z');
    expect(parseSoldDay('2026-11-02', now)).toBeNull();
    expect(parseSoldDay('10/02/2026', now)).toBeNull();
  });

  it('dates a sale relative to now and a listing as listed', () => {
    const now = new Date('2026-10-18T12:00:00Z');

    expect(compDateLabel(comp({ date: '2026-10-08T12:00:00Z' }), now)).toBe('Sold 10 days ago');
    expect(compDateLabel(comp({ kind: 'Asking', date: '2026-10-18T08:00:00Z' }), now)).toBe('Listed today');
    expect(compDateLabel(comp({ date: null }), now)).toBeNull();
  });
});
