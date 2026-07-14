import type { AcquisitionLot } from './acquisition.types';

export function acquisitionAllocatedTotal(lot: AcquisitionLot): number {
  return lot.totalAllocatedCost ?? (lot.items ?? []).reduce((sum, item) => sum + (item.allocatedCost ?? 0), 0);
}

export function acquisitionUnallocatedCost(lot: AcquisitionLot): number {
  return (lot.totalLandedCost ?? 0) - acquisitionAllocatedTotal(lot);
}

export function acquisitionItemCount(lot: AcquisitionLot): number {
  return lot.items?.length ?? 0;
}

export function acquisitionMethodLabel(method: AcquisitionLot['allocationMethod']): string {
  switch (method) {
    case 'WeightedByExpectedValue':
      return 'Weighted by value';
    case 'Manual':
      return 'Manual';
    case 'Equal':
    default:
      return 'Equal split';
  }
}

export function acquisitionDisplayName(lot: AcquisitionLot): string {
  return lot.name?.trim() || 'Untitled purchase';
}
