import {
  damageFilled,
  detailsFilled,
  essentialsFilled,
  physicalFilled,
  pricingFilled,
  projectedMargin,
  workFilled,
} from './completeness';

describe('essentialsFilled', () => {
  const none = { title: '', condition: '', category: '', brand: '' };

  it('counts nothing when nothing is entered', () => {
    expect(essentialsFilled(none)).toBe(0);
  });

  it('counts each field once', () => {
    expect(essentialsFilled({ ...none, title: 'Brass Lamp' })).toBe(1);
    expect(essentialsFilled({ title: 'Brass Lamp', condition: 'Good', category: 'Lighting', brand: 'Unknown' })).toBe(4);
  });

  it('does not count whitespace as a typed answer', () => {
    expect(essentialsFilled({ ...none, title: '   ', brand: '  ' })).toBe(0);
  });
});

describe('pricingFilled', () => {
  it('counts the two prices', () => {
    expect(pricingFilled({ purchasePrice: '', estimatedSalePrice: '' })).toBe(0);
    expect(pricingFilled({ purchasePrice: '20', estimatedSalePrice: '' })).toBe(1);
    expect(pricingFilled({ purchasePrice: '20', estimatedSalePrice: '60' })).toBe(2);
  });
});

describe('detailsFilled', () => {
  it('counts size plus every attribute that was filled in', () => {
    expect(detailsFilled({ sizeSystem: 'US', sizeValue: '10', attributes: { material: 'oak' } })).toBe(3);
  });

  it('ignores attributes left blank', () => {
    expect(detailsFilled({ sizeSystem: '', sizeValue: '', attributes: { a: '', b: '  ', c: 'x' } })).toBe(1);
  });
});

describe('physicalFilled', () => {
  const none = {
    barcode: '', manufacturerPartNumber: '', serialNumber: '', countryOfOrigin: '',
    manufactureYear: '', length: '', width: '', height: '', weight: '',
  };

  it('counts nothing when nothing is entered', () => {
    expect(physicalFilled(none)).toBe(0);
  });

  it('counts the three dimensions together as one measurement', () => {
    // Nobody fills length alone on purpose; three separate ticks would overstate progress.
    expect(physicalFilled({ ...none, length: '10' })).toBe(1);
    expect(physicalFilled({ ...none, length: '10', width: '4', height: '2' })).toBe(1);
  });

  it('counts weight separately from the dimensions', () => {
    expect(physicalFilled({ ...none, length: '10', weight: '3' })).toBe(2);
  });
});

describe('workFilled', () => {
  it('counts each note and the ownership picker', () => {
    const none = {
      ownershipType: '', storageLocation: '', accessories: '',
      testingNotes: '', cleaningNotes: '', listingNotes: '', missingParts: '',
    };
    expect(workFilled(none)).toBe(0);
    expect(workFilled({ ...none, ownershipType: 'Owned', storageLocation: 'Bin 4' })).toBe(2);
  });
});

describe('damageFilled', () => {
  it('counts the type, the severity and the note', () => {
    expect(damageFilled({ damageType: '', damageSeverity: '', damageNote: '' })).toBe(0);
    expect(damageFilled({ damageType: 'Scratch', damageSeverity: 'Minor', damageNote: 'on the base' })).toBe(3);
  });
});

describe('projectedMargin', () => {
  it('works out the profit and the percentage of the sale price', () => {
    expect(projectedMargin('40', '100')).toEqual({ profit: 60, pct: '60', positive: true });
  });

  it('says so when the item would lose money', () => {
    const margin = projectedMargin('100', '40');
    expect(margin?.profit).toBe(-60);
    expect(margin?.positive).toBe(false);
  });

  it('shows nothing until both prices are known', () => {
    expect(projectedMargin('', '100')).toBeNull();
    expect(projectedMargin('40', '')).toBeNull();
    expect(projectedMargin('', '')).toBeNull();
  });

  it('shows nothing for a sale price of zero rather than dividing by it', () => {
    expect(projectedMargin('40', '0')).toBeNull();
  });

  it('treats breaking even as not positive', () => {
    expect(projectedMargin('50', '50')).toEqual({ profit: 0, pct: '0', positive: false });
  });
});
