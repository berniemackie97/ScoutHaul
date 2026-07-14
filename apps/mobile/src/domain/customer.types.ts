import type { OrderSummary } from './order.types';

export interface CustomerChannelIdentity {
  id: string;
  channel: string;
  externalUserId: string;
  displayName: string | null;
  email: string | null;
  firstSeenAt: string;
  lastSeenAt: string;
}

export interface CustomerAddress {
  id: string;
  label: string | null;
  recipientName: string;
  line1: string;
  line2: string | null;
  city: string;
  region: string;
  postalCode: string;
  country: string;
  phone: string | null;
  isDefault: boolean;
}

export interface CustomerSummary {
  id: string;
  email: string | null;
  displayName: string | null;
  phone: string | null;
  acceptsMarketing: boolean;
  storeCreditBalance: number;
  orderCount: number;
  lifetimeSpend: number;
  createdAt: string;
  lastOrderAt: string | null;
  channels: string[] | null;
}

export interface CustomerDetail extends CustomerSummary {
  addresses: CustomerAddress[];
  orders: OrderSummary[];
  channelIdentities: CustomerChannelIdentity[] | null;
}
