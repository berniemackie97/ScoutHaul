import type { AcquisitionLotAllocationMethod } from '../domain/types';

/**
 * The acquisition-lot write contract.
 *
 * Separate from `acquisition.ts` because that module pulls in the HTTP client, and through it auth,
 * Supabase and AsyncStorage. Shaping a body needs none of that.
 */

export type AcquisitionLotItemInput = {
  inventoryItemId: string;
  manualAllocatedCost?: number | null;
  allocationWeight?: number | null;
  notes?: string | null;
};

/**
 * A lot may be recorded with no items: the money is known at the till, the itemizing happens later.
 * That is the order sourcing actually happens in, and the server allows it.
 */
export type UpsertAcquisitionLotRequest = {
  name: string;
  purchasedAt: string | null;
  purchaseSubtotal: number;
  shippingCost: number;
  taxCost: number;
  feeCost: number;
  otherCost: number;
  allocationMethod: AcquisitionLotAllocationMethod;
  supplierName: string | null;
  sourceLocationId: string | null;
  sourcingTripId: string | null;
  sourcingTripStopId: string | null;
  externalReference: string | null;
  notes: string | null;
  items: AcquisitionLotItemInput[];
};
