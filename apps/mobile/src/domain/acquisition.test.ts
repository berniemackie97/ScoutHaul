import {
  acquisitionAllocatedTotal,
  acquisitionDisplayName,
  acquisitionItemCount,
  acquisitionMethodLabel,
  acquisitionUnallocatedCost,
} from './acquisition';
import type { AcquisitionLot } from './acquisition.types';

function lot(overrides: Partial<AcquisitionLot> = {}): AcquisitionLot {
  return {
    id: 'lot-1',
    name: 'Auction buy',
    purchaseSubtotal: 80,
    shippingCost: 10,
    taxCost: 5,
    feeCost: 3,
    otherCost: 2,
    totalLandedCost: 100,
    totalAllocatedCost: 75,
    allocationMethod: 'Equal',
    items: [],
    ...overrides,
  };
}

describe('acquisition helpers', () => {
  it('computes unallocated landed cost', () => {
    expect(acquisitionAllocatedTotal(lot())).toBe(75);
    expect(acquisitionUnallocatedCost(lot())).toBe(25);
  });

  it('falls back to item allocations if summary allocation is absent', () => {
    const subject = lot({
      totalAllocatedCost: undefined,
      items: [
        { id: 'line-1', inventoryItemId: 'item-1', allocatedCost: 12 },
        { id: 'line-2', inventoryItemId: 'item-2', allocatedCost: 13 },
      ],
    });

    expect(acquisitionAllocatedTotal(subject)).toBe(25);
    expect(acquisitionItemCount(subject)).toBe(2);
  });

  it('labels allocation methods and unnamed lots', () => {
    expect(acquisitionMethodLabel('WeightedByExpectedValue')).toBe('Weighted by value');
    expect(acquisitionMethodLabel('Manual')).toBe('Manual');
    expect(acquisitionDisplayName(lot({ name: '  ' }))).toBe('Untitled purchase');
  });
});
