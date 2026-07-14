import { commerceManagedStatuses } from './inventoryOptions';
import type { InventoryItem, InventoryStatus } from './types';

export type ArchivePolicy =
  | { allowed: true }
  | { allowed: false; reason: string; recovery: 'orders' | 'delist-first' };

const orderManagedStatuses = new Set<InventoryStatus>(commerceManagedStatuses);

export function isOrderManagedInventory(item: InventoryItem): boolean {
  return orderManagedStatuses.has(item.status);
}

export function archiveInventoryPolicy(item: InventoryItem): ArchivePolicy {
  if (isOrderManagedInventory(item)) {
    return {
      allowed: false,
      recovery: 'orders',
      reason: 'This item is controlled by an order. Correct or close the order before archiving the inventory record.',
    };
  }

  if (item.status === 'Bundled') {
    return {
      allowed: false,
      recovery: 'delist-first',
      reason: 'This item is part of a bundle. Remove it from the bundle before archiving the component record.',
    };
  }

  if (item.status === 'Listed') {
    return {
      allowed: false,
      recovery: 'delist-first',
      reason: 'This item is listed. Delist it or move it out of Listed before archiving so channel state stays accurate.',
    };
  }

  return { allowed: true };
}

export function inventoryCostBasis(item: InventoryItem): number | null {
  return item.acquisitionAllocatedCost ?? item.purchasePrice ?? item.sellerAskingPrice ?? null;
}

export function inventoryCostBasisLabel(item: InventoryItem): string {
  if (item.acquisitionAllocatedCost != null) return 'Allocated cost';
  if (item.purchasePrice != null) return 'Purchase cost';
  if (item.sellerAskingPrice != null) return 'Seller ask';
  return 'Cost basis';
}

export function inventorySummaryBadges(item: InventoryItem): string[] {
  const badges: string[] = [];
  if (item.isBundle) {
    const count = item.bundleComponentCount ?? 0;
    badges.push(`Bundle${count > 0 ? ` · ${count} item${count === 1 ? '' : 's'}` : ''}`);
  }
  if (item.acquisitionLotName) {
    badges.push(`Lot · ${item.acquisitionLotName}`);
  }
  if (item.lifecycleGroup) {
    badges.push(item.lifecycleGroup);
  }
  return badges;
}
