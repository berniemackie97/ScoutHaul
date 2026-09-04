import type { MarketplaceLookupResult } from '../../domain/types';
import { emptyLookupResult, isConclusiveLookup } from './types';

const result = (over: Partial<MarketplaceLookupResult> = {}): MarketplaceLookupResult => ({
  ...emptyLookupResult('nike air max'),
  ...over,
});

describe('emptyLookupResult', () => {
  it('is a lookup that found nothing, not a failure', () => {
    const empty = emptyLookupResult('nike air max');

    expect(empty.query).toBe('nike air max');
    expect(empty.listings).toEqual([]);
    expect(empty.totalResults).toBe(0);
    // Nulls, not zeros: no price was found, which is different from a price of zero.
    expect(empty.averageSoldPrice).toBeNull();
    expect(empty.lowestPrice).toBeNull();
    expect(empty.highestPrice).toBeNull();
  });
});

describe('isConclusiveLookup', () => {
  it('stops once listings came back', () => {
    expect(isConclusiveLookup(result({ totalResults: 3, providersUsed: ['ebay'] }))).toBe(true);
  });

  it('stops once a product was named, even with no listings', () => {
    // A name is enough to price against; the retries only exist to find one.
    expect(isConclusiveLookup(result({ productTitle: 'Nike Air Max 90', providersUsed: ['ebay'] }))).toBe(true);
  });

  it('stops when nobody was asked', () => {
    // No providers configured, so a different phrasing would ask nobody again. Retrying would only
    // delay the result the scanner already has from vision.
    expect(isConclusiveLookup(result({ providersUsed: [] }))).toBe(true);
    expect(isConclusiveLookup(result({ providersUsed: undefined }))).toBe(true);
  });

  it('keeps trying when providers answered and found nothing', () => {
    // This is the one case worth another phrasing.
    expect(isConclusiveLookup(result({ totalResults: 0, productTitle: null, providersUsed: ['ebay'] }))).toBe(false);
  });

  it('does not treat an empty product title as a name', () => {
    expect(isConclusiveLookup(result({ productTitle: '', providersUsed: ['ebay'] }))).toBe(false);
  });
});
