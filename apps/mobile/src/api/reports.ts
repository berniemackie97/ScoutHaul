import { apiRequest } from './client';
import type { BusinessReport } from '../domain/types';

export type ReportOverviewParams = {
  from?: string;
  to?: string;
};

function query(params: ReportOverviewParams = {}) {
  const search = new URLSearchParams();
  if (params.from) search.set('from', params.from);
  if (params.to) search.set('to', params.to);
  const value = search.toString();
  return value ? `?${value}` : '';
}

export const reportsApi = {
  overview: (params?: ReportOverviewParams) =>
    apiRequest<BusinessReport>(`/api/reports/overview${query(params)}`),
};
