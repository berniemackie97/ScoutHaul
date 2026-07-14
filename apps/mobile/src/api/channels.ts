import { apiRequest } from './client';
import type { ChannelAccount, ChannelListing, ChannelListingStatus, UpsertChannelAccountRequest } from '../domain/types';

type ChannelListingFilters = {
  status?: ChannelListingStatus;
};

function query(filters: ChannelListingFilters = {}) {
  const search = new URLSearchParams();
  if (filters.status) search.set('status', filters.status);
  const value = search.toString();
  return value ? `?${value}` : '';
}

export const channelsApi = {
  accounts: () => apiRequest<ChannelAccount[]>('/api/channel-accounts'),
  account: (id: string) => apiRequest<ChannelAccount>(`/api/channel-accounts/${id}`),
  createAccount: (body: UpsertChannelAccountRequest) =>
    apiRequest<ChannelAccount>('/api/channel-accounts', { method: 'POST', body }),
  updateAccount: (id: string, body: UpsertChannelAccountRequest) =>
    apiRequest<ChannelAccount>(`/api/channel-accounts/${id}`, { method: 'PUT', body }),
  deleteAccount: (id: string) =>
    apiRequest<void>(`/api/channel-accounts/${id}`, { method: 'DELETE' }),
  listings: (filters?: ChannelListingFilters) =>
    apiRequest<ChannelListing[]>(`/api/channel-listings${query(filters)}`),
};
