import { apiRequest } from './client';
import type { ResaleTask, WorkTaskStatus } from '../domain/types';

export type CreateTaskRequest = {
  title: string;
  description?: string | null;
  inventoryItemId?: string | null;
  dueAt?: string | null;
};

export const tasksApi = {
  list: () => apiRequest<ResaleTask[]>('/api/tasks'),
  create: (body: CreateTaskRequest) => apiRequest<ResaleTask>('/api/tasks', { method: 'POST', body }),
  updateStatus: (id: string, status: WorkTaskStatus) =>
    apiRequest<ResaleTask>(`/api/tasks/${id}/status`, { method: 'PATCH', body: { status } }),
  delete: (id: string) => apiRequest<void>(`/api/tasks/${id}`, { method: 'DELETE' }),
};
