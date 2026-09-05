import { apiRequest } from './client';
import type { UpsertAcquisitionLotRequest } from './acquisitionRequests';
import type { AcquisitionLot } from '../domain/types';

export type { AcquisitionLotItemInput, UpsertAcquisitionLotRequest } from './acquisitionRequests';

export const acquisitionApi = {
  lots: () => apiRequest<AcquisitionLot[]>('/api/acquisition-lots'),
  lot: (id: string) => apiRequest<AcquisitionLot>(`/api/acquisition-lots/${id}`),
  createLot: (body: UpsertAcquisitionLotRequest, idempotencyKey?: string) =>
    apiRequest<AcquisitionLot>('/api/acquisition-lots', { method: 'POST', body, idempotencyKey }),
};
