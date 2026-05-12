import { appConfig } from '../config';
import { authStore } from '../services/auth';
import type {
  BudgetSummary,
  DashboardSummary,
  InventoryItem,
  Listing,
  MarketAnalysisResponse,
  MarketObservation,
  MarketplaceSearchLinks,
  ProductIdentity,
  ResaleTask,
  SourcingTrip,
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
  changeInventoryStatus: (id: string, status: string) =>
    apiRequest<InventoryItem>(`/api/inventory/${id}/status`, { method: 'PATCH', body: { status } }),
  searchProducts: (search: string) =>
    apiRequest<ProductIdentity[]>(`/api/product-identities?search=${encodeURIComponent(search)}`),
  marketplaceLinks: (query: string) =>
    apiRequest<MarketplaceSearchLinks>(`/api/marketplace-links?query=${encodeURIComponent(query)}`),
  addComp: (body: unknown) => apiRequest<MarketObservation>('/api/market-observations', { method: 'POST', body }),
  comps: (params: string) => apiRequest<MarketObservation[]>(`/api/market-observations?${params}`),
  analyze: (body: unknown) => apiRequest<MarketAnalysisResponse>('/api/market-analysis/calculate', { method: 'POST', body }),
  trips: () => apiRequest<SourcingTrip[]>('/api/sourcing-trips'),
  tripSummary: (id: string) => apiRequest<SourcingTripSummary>(`/api/sourcing-trips/${id}/summary`),
  createTrip: (body: unknown) => apiRequest<SourcingTrip>('/api/sourcing-trips', { method: 'POST', body }),
  budgetSummary: () => apiRequest<BudgetSummary>('/api/budgets/summary'),
  addExpense: (body: unknown) => apiRequest('/api/budgets/expenses', { method: 'POST', body }),
  listings: () => apiRequest<Listing[]>('/api/listings'),
  createListing: (body: unknown) => apiRequest<Listing>('/api/listings', { method: 'POST', body }),
  tasks: () => apiRequest<ResaleTask[]>('/api/tasks'),
  createTask: (body: unknown) => apiRequest<ResaleTask>('/api/tasks', { method: 'POST', body }),
  updateTaskStatus: (id: string, status: string) =>
    apiRequest<ResaleTask>(`/api/tasks/${id}/status`, { method: 'PATCH', body: { status } }),
};
