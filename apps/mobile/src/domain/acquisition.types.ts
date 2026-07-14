import type { InventoryStatus } from './inventory.types';

export type AcquisitionLotAllocationMethod = 'Equal' | 'WeightedByExpectedValue' | 'Manual';

export type AcquisitionLotItem = {
  id: string;
  inventoryItemId: string;
  inventoryItemTitle?: string | null;
  inventoryItemStatus?: InventoryStatus;
  allocatedCost?: number;
  allocationWeight?: number | null;
  notes?: string | null;
};

export type AcquisitionLot = {
  id: string;
  name?: string | null;
  supplierName?: string | null;
  sourceLocationId?: string | null;
  sourcingTripId?: string | null;
  sourcingTripStopId?: string | null;
  purchasedAt?: string | null;
  purchaseSubtotal?: number;
  shippingCost?: number;
  taxCost?: number;
  feeCost?: number;
  otherCost?: number;
  totalLandedCost?: number;
  totalAllocatedCost?: number;
  allocationMethod?: AcquisitionLotAllocationMethod;
  externalReference?: string | null;
  notes?: string | null;
  items?: AcquisitionLotItem[] | null;
  createdAt?: string | null;
  updatedAt?: string | null;
};
