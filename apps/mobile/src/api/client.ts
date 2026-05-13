import { appConfig } from '../config';
import { authStore } from '../services/auth';
import type {
  BudgetSummary,
  DashboardSummary,
  InventoryItem,
  Listing,
  MarketAnalysisResponse,
  MarketplaceLookupResult,
  MarketObservation,
  MarketplaceSearchLinks,
  ProductIdentity,
  ResaleTask,
  SourcingRouteDetail,
  SourcingTrip,
  SourcingTripStop,
  SourcingTripSummary,
} from '../domain/types';

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
};

function buildHeaders(): Record<string, string> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'Content-Type': 'application/json',
  };

  if (appConfig.isStubAuth) {
    headers['X-ScoutHaul-Organization-Id'] = appConfig.organizationId;
    headers['X-ScoutHaul-User-Id'] = appConfig.userId;
  } else {
    const token = authStore.getToken();
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }
  }

  return headers;
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await fetch(`${appConfig.apiBaseUrl}${path}`, {
    method: options.method ?? 'GET',
    headers: buildHeaders(),
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(parseErrorMessage(text) || `Request failed with ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

function parseErrorMessage(text: string): string {
  if (!text) {
    return '';
  }

  try {
    const parsed = JSON.parse(text) as { title?: string; detail?: string; errors?: Record<string, string[]> };
    const validationErrors = parsed.errors ? Object.values(parsed.errors).flat() : [];
    return validationErrors[0] ?? parsed.detail ?? parsed.title ?? text;
  } catch {
    return text.replace(/^"|"$/g, '');
  }
}

export const api = {
  dashboard: () => apiRequest<DashboardSummary>('/api/dashboard/summary'),
  inventory: () => apiRequest<InventoryItem[]>('/api/inventory'),
  inventoryItem: (id: string) => apiRequest<InventoryItem>(`/api/inventory/${id}`),
  createInventory: (body: unknown) => apiRequest<InventoryItem>('/api/inventory', { method: 'POST', body }),
  updateInventory: (id: string, body: unknown) => apiRequest<InventoryItem>(`/api/inventory/${id}`, { method: 'PUT', body }),
  deleteInventory: (id: string) => apiRequest<void>(`/api/inventory/${id}`, { method: 'DELETE' }),
  changeInventoryStatus: (id: string, status: string) =>
    apiRequest<InventoryItem>(`/api/inventory/${id}/status`, { method: 'PATCH', body: { status } }),
  searchProducts: (search: string) =>
    apiRequest<ProductIdentity[]>(`/api/product-identities?search=${encodeURIComponent(search)}`),
  marketplaceLinks: (query: string) =>
    apiRequest<MarketplaceSearchLinks>(`/api/marketplace-links?query=${encodeURIComponent(query)}`),
  lookupBarcode: (barcode: string) =>
    apiRequest<MarketplaceLookupResult>(`/api/marketplace-lookup/barcode/${encodeURIComponent(barcode)}`),
  lookupSearch: (query: string) =>
    apiRequest<MarketplaceLookupResult>(`/api/marketplace-lookup/search?q=${encodeURIComponent(query)}`),
  addComp: (body: unknown) => apiRequest<MarketObservation>('/api/market-observations', { method: 'POST', body }),
  comps: (params: string) => apiRequest<MarketObservation[]>(`/api/market-observations?${params}`),
  analyze: (body: unknown) => apiRequest<MarketAnalysisResponse>('/api/market-analysis/calculate', { method: 'POST', body }),
  trips: () => apiRequest<SourcingTrip[]>('/api/sourcing-trips'),
  route: (id: string) => apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${id}/route`),
  tripSummary: (id: string) => apiRequest<SourcingTripSummary>(`/api/sourcing-trips/${id}/summary`),
  createTrip: (body: unknown) => apiRequest<SourcingTrip>('/api/sourcing-trips', { method: 'POST', body }),
  deleteTrip: (id: string) => apiRequest<void>(`/api/sourcing-trips/${id}`, { method: 'DELETE' }),
  updateRouteStatus: (id: string, status: string) =>
    apiRequest<SourcingTrip>(`/api/sourcing-trips/${id}/status`, { method: 'PATCH', body: { status } }),
  startRoute: (id: string) => apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${id}/start`, { method: 'POST' }),
  pauseRoute: (id: string) => apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${id}/pause`, { method: 'POST' }),
  completeRoute: (id: string) => apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${id}/complete-route`, { method: 'POST' }),
  createRouteStop: (tripId: string, body: unknown) =>
    apiRequest<SourcingTripStop>(`/api/sourcing-trips/${tripId}/stops`, { method: 'POST', body }),
  updateRouteStop: (tripId: string, stopId: string, body: unknown) =>
    apiRequest<SourcingTripStop>(`/api/sourcing-trips/${tripId}/stops/${stopId}`, { method: 'PUT', body }),
  deleteRouteStop: (tripId: string, stopId: string) =>
    apiRequest<void>(`/api/sourcing-trips/${tripId}/stops/${stopId}`, { method: 'DELETE' }),
  reorderRouteStops: (tripId: string, stopIds: string[]) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/reorder`, { method: 'POST', body: { stopIds } }),
  applyStopChecklistTemplate: (tripId: string, stopId: string) =>
    apiRequest<SourcingTripStop>(`/api/sourcing-trips/${tripId}/stops/${stopId}/apply-checklist-template`, { method: 'POST' }),
  navigateStop: (tripId: string, stopId: string) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/navigate`, { method: 'POST' }),
  arriveAtStop: (tripId: string, stopId: string) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/arrive`, { method: 'POST' }),
  completeStop: (tripId: string, stopId: string, force = false) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/complete`, { method: 'POST', body: { force } }),
  skipStop: (tripId: string, stopId: string, reason?: string) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/skip`, { method: 'POST', body: { reason: reason ?? null } }),
  updateChecklistItem: (tripId: string, stopId: string, checklistItemId: string, isCompleted: boolean) =>
    apiRequest<SourcingRouteDetail>(`/api/sourcing-trips/${tripId}/stops/${stopId}/checklist/${checklistItemId}`, {
      method: 'PATCH',
      body: { isCompleted },
    }),
  createChecklistItem: (tripId: string, stopId: string, body: unknown) =>
    apiRequest(`/api/sourcing-trips/${tripId}/stops/${stopId}/checklist`, { method: 'POST', body }),
  budgetSummary: () => apiRequest<BudgetSummary>('/api/budgets/summary'),
  addExpense: (body: unknown) => apiRequest('/api/budgets/expenses', { method: 'POST', body }),
  listings: () => apiRequest<Listing[]>('/api/listings'),
  createListing: (body: unknown) => apiRequest<Listing>('/api/listings', { method: 'POST', body }),
  updateListing: (id: string, body: unknown) => apiRequest<Listing>(`/api/listings/${id}`, { method: 'PUT', body }),
  markListingListed: (id: string) => apiRequest<Listing>(`/api/listings/${id}/mark-listed`, { method: 'POST' }),
  deleteListing: (id: string) => apiRequest<void>(`/api/listings/${id}`, { method: 'DELETE' }),
  tasks: () => apiRequest<ResaleTask[]>('/api/tasks'),
  createTask: (body: unknown) => apiRequest<ResaleTask>('/api/tasks', { method: 'POST', body }),
  updateTaskStatus: (id: string, status: string) =>
    apiRequest<ResaleTask>(`/api/tasks/${id}/status`, { method: 'PATCH', body: { status } }),
};
