import { apiRequest } from './client';
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
  list: (filters?: CustomerListFilters) => apiRequest<CustomerSummary[]>(`/api/customers${query(filters)}`),
  get: (id: string) => apiRequest<CustomerDetail>(`/api/customers/${id}`),
};
