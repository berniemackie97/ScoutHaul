import { apiRequest, type CallOptions } from './client';
import type { CustomerDetail, CustomerSummary } from '../domain/types';

type CustomerListFilters = {
  search?: string;
  channel?: string;
};

function query(filters: CustomerListFilters = {}) {
  const search = new URLSearchParams();
  if (filters.search?.trim()) search.set('search', filters.search.trim());
  if (filters.channel?.trim()) search.set('channel', filters.channel.trim());
  const value = search.toString();
  return value ? `?${value}` : '';
}

export const customersApi = {
  // `options` carries the caller's abort signal, so a screen that closes mid-request stops waiting
  // for it. Every read should take it; these are the two the resource hook uses so far.
  list: (filters?: CustomerListFilters, options?: CallOptions) =>
    apiRequest<CustomerSummary[]>(`/api/customers${query(filters)}`, options),
  get: (id: string, options?: CallOptions) =>
    apiRequest<CustomerDetail>(`/api/customers/${id}`, options),
};
