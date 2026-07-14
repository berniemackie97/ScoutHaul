import type { ChannelFeeProfile } from './order.types';

export type ChannelListingStatus = 'NotListed' | 'Draft' | 'Active' | 'Sold' | 'PendingDelist' | 'Delisted' | 'Error';

export interface ChannelAccount {
  id: string;
  channel: string;
  displayName: string;
  externalAccountId: string | null;
  defaultFeeProfileId: string | null;
  isNativelySynced: boolean;
  isActive: boolean;
  connectedAt: string | null;
  notes: string | null;
  activeListingCount: number;
}

export interface UpsertChannelAccountRequest {
  channel: string;
  displayName: string;
  externalAccountId?: string | null;
  defaultFeeProfileId?: string | null;
  isActive: boolean;
  notes?: string | null;
}

export interface ChannelListing {
  id: string;
  inventoryItemId: string;
  inventoryItemTitle: string;
  channelAccountId: string;
  channelAccountName: string;
  channel: string;
  status: ChannelListingStatus;
  externalListingId: string | null;
  externalUrl: string | null;
  listedPrice: number | null;
  quantity: number | null;
  imageHash: string | null;
  listedAt: string | null;
  soldAt: string | null;
  lastSyncedAt: string | null;
  lastError: string | null;
}

export interface MobileChannelOption {
  value: string;
  label: string;
  group: string;
  isLegacy?: boolean;
  feeProfiles?: ChannelFeeProfile[] | null;
}
