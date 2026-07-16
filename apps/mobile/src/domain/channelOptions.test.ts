import { fallbackChannelOptions, usableChannelOptions } from './channelOptions';
import type { SalesReferenceData } from './order.types';

describe('channel options', () => {
  it('uses controlled backend options when available', () => {
    const referenceData: SalesReferenceData = {
      channels: [
        { value: 'LegacyText', label: 'Legacy text', group: 'Existing saved values', isLegacy: true },
        { value: 'eBay', label: 'eBay', group: 'Marketplaces' },
      ],
    };

    expect(usableChannelOptions(referenceData)).toEqual([
      { value: 'eBay', label: 'eBay', group: 'Marketplaces' },
    ]);
  });

  it('falls back when reference data is unavailable', () => {
    expect(usableChannelOptions(null)).toBe(fallbackChannelOptions);
    expect(usableChannelOptions(undefined)).toBe(fallbackChannelOptions);
  });

  it('falls back when reference data only has legacy or blank values', () => {
    expect(usableChannelOptions({
      channels: [
        { value: '   ', label: 'Blank', group: 'Broken' },
        { value: 'Stored typo', label: 'Stored typo', group: 'Existing saved values', isLegacy: true },
      ],
    })).toBe(fallbackChannelOptions);
  });

  it('falls back when the backend response is malformed', () => {
    expect(usableChannelOptions({} as SalesReferenceData)).toBe(fallbackChannelOptions);
  });

  it('includes broad manual marketplace coverage', () => {
    expect(fallbackChannelOptions.map((option) => option.value)).toEqual(expect.arrayContaining([
      'Storefront',
      'eBay',
      'Depop',
      'Poshmark',
      'Mercari',
      'Vinted',
      'FacebookMarketplace',
      'Whatnot',
      'Etsy',
      'Amazon',
      'OfferUp',
      'Other',
    ]));
  });
});
