import { apiRequest } from './client';
import type { AcquisitionLot } from '../domain/types';

export const acquisitionApi = {
  lots: () => apiRequest<AcquisitionLot[]>('/api/acquisition-lots'),
  lot: (id: string) => apiRequest<AcquisitionLot>(`/api/acquisition-lots/${id}`),
};
