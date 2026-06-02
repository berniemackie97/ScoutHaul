import { apiRequest } from './client';
import type {
  SourcingRouteDetail,
  SourcingRouteType,
  SourcingStopPriority,
  SourcingStopSourceType,
  SourcingTrip,
  SourcingTripStop,
  SourcingTripSummary,
} from '../domain/types';

export type CreateTripRequest = {
  name: string;
  startedAt: string;
  routeDate: string;
  plannedStartAt: string;
  routeType: SourcingRouteType;
  cashBudget?: number | null;
  notes?: string | null;
};

export type CreateStopRequest = {
  name: string;
  sourceType: SourcingStopSourceType;
  address: string;
  phone?: string | null;
  url?: string | null;
  notes?: string | null;
  plannedOrder: number;
  plannedArrivalAt?: string | null;
  estimatedDurationMinutes: number;
  priority: SourcingStopPriority;
  isTimeSensitive: boolean;
  timeWindowStartAt?: string | null;
  timeWindowEndAt?: string | null;
  canBeSkipped: boolean;
  budgetCap?: number | null;
};

export type CreateChecklistItemRequest = {
  text: string;
  isRequired: boolean;
  sortOrder?: number;
};

export const sourcingApi = {
  trips: () => apiRequest<SourcingTrip[]>('/api/sourcing-trips'),
  route: (id: string) => apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${id}/route`),
  tripSummary: (id: string) => apiRequest<SourcingTripSummary>(`/api/sourcing-trips/${id}/summary`),
  createTrip: (body: CreateTripRequest) => apiRequest<SourcingTrip>('/api/sourcing-trips', { method: 'POST', body }),
  deleteTrip: (id: string) => apiRequest<void>(`/api/sourcing-trips/${id}`, { method: 'DELETE' }),

  updateStatus: (id: string, status: string) =>
    apiRequest<SourcingTrip>(`/api/sourcing-trips/${id}/status`, { method: 'PATCH', body: { status } }),
  start: (id: string) => apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${id}/start`, { method: 'POST' }),
  pause: (id: string) => apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${id}/pause`, { method: 'POST' }),
  complete: (id: string) => apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${id}/complete-route`, { method: 'POST' }),

  createStop: (tripId: string, body: CreateStopRequest) =>
    apiRequest<SourcingTripStop>(`/api/sourcing-trips/${tripId}/stops`, { method: 'POST', body }),
  updateStop: (tripId: string, stopId: string, body: CreateStopRequest) =>
    apiRequest<SourcingTripStop>(`/api/sourcing-trips/${tripId}/stops/${stopId}`, { method: 'PUT', body }),
  deleteStop: (tripId: string, stopId: string) =>
    apiRequest<void>(`/api/sourcing-trips/${tripId}/stops/${stopId}`, { method: 'DELETE' }),
  reorderStops: (tripId: string, stopIds: string[]) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/reorder`, { method: 'POST', body: { stopIds } }),

  navigateStop: (tripId: string, stopId: string) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/navigate`, { method: 'POST' }),
  arriveAtStop: (tripId: string, stopId: string) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/arrive`, { method: 'POST' }),
  completeStop: (tripId: string, stopId: string, force = false) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/complete`, { method: 'POST', body: { force } }),
  skipStop: (tripId: string, stopId: string, reason?: string) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/skip`, { method: 'POST', body: { reason: reason ?? null } }),
  applyChecklistTemplate: (tripId: string, stopId: string) =>
    apiRequest<SourcingTripStop>(`/api/sourcing-trips/${tripId}/stops/${stopId}/apply-checklist-template`, { method: 'POST' }),
  updateChecklistItem: (tripId: string, stopId: string, checklistItemId: string, isCompleted: boolean) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/checklist/${checklistItemId}`, {
      method: 'PATCH', body: { isCompleted },
    }),
  createChecklistItem: (tripId: string, stopId: string, body: CreateChecklistItemRequest) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/checklist`, { method: 'POST', body }),
};
