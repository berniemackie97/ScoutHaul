import Constants from 'expo-constants';
import { Platform } from 'react-native';
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

function resolveApiBaseUrl(): string {
  if (process.env.EXPO_PUBLIC_API_BASE_URL) {
    return process.env.EXPO_PUBLIC_API_BASE_URL.replace(/\/+$/, '');
  }
  if (__DEV__) {
    const debuggerHost = Constants.expoConfig?.hostUri ?? Constants.experienceUrl ?? '';
    const lanHost = debuggerHost.split(':')[0];
    if (lanHost && Platform.OS !== 'web') {
      return `http://${lanHost}:5080`;
    }
  }
  return 'http://localhost:5080';
}

const API_BASE_URL = resolveApiBaseUrl();
const DEFAULT_ORGANIZATION_ID = process.env.EXPO_PUBLIC_SCOUTHAUL_ORG_ID ?? '9d3c0c44-218b-49d9-90bf-07b748610001';
const DEFAULT_USER_ID = process.env.EXPO_PUBLIC_SCOUTHAUL_USER_ID ?? '9d3c0c44-218b-49d9-90bf-07b748610002';

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
};

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: options.method ?? 'GET',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'X-ScoutHaul-Organization-Id': DEFAULT_ORGANIZATION_ID,
      'X-ScoutHaul-User-Id': DEFAULT_USER_ID,
    },
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
  baseUrl: API_BASE_URL,
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
