import {
  archiveInventoryPolicy,
  inventoryCostBasis,
  inventoryCostBasisLabel,
  inventorySummaryBadges,
  isOrderManagedInventory,
} from './inventoryOperations';
import type { InventoryItem } from './types';

function item(overrides: Partial<InventoryItem> = {}): InventoryItem {
  return {
    id: 'item-1',
    title: 'Test item',
    status: 'Draft',
    photoUrls: [],
    isArchived: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

describe('inventory operations', () => {
  it('locks archive for listed items until they are delisted', () => {
    expect(archiveInventoryPolicy(item({ status: 'Listed' }))).toMatchObject({
      allowed: false,
      recovery: 'delist-first',
    });
  });

  it('locks archive for order-managed items', () => {
    const sold = item({ status: 'Sold' });

    expect(isOrderManagedInventory(sold)).toBe(true);
    expect(archiveInventoryPolicy(sold)).toMatchObject({
      allowed: false,
      recovery: 'orders',
    });
  });

  it('prefers acquisition allocated cost for cost basis', () => {
    const acquired = item({
      acquisitionAllocatedCost: 12.34,
      purchasePrice: 25,
      sellerAskingPrice: 30,
    });

    expect(inventoryCostBasis(acquired)).toBe(12.34);
    expect(inventoryCostBasisLabel(acquired)).toBe('Allocated cost');
  });

  it('summarizes bundle and lot context', () => {
    expect(inventorySummaryBadges(item({
      isBundle: true,
      bundleComponentCount: 3,
      acquisitionLotName: 'Estate buy',
      lifecycleGroup: 'Prep',
    }))).toEqual(['Bundle · 3 items', 'Lot · Estate buy', 'Prep']);
  });
});
