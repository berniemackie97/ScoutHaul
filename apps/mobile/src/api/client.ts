import { appConfig } from '../config';
import { authStore } from '../services/auth';
import { getActiveOrganizationId } from '../services/workspaceSession';
import type {
  BudgetSummary,
  Budget,
  DashboardSummary,
  Expense,
  InventoryItem,
  Listing,
  MarketAnalysisResponse,
  MarketplaceLookupResult,
  MarketObservation,
  MarketplaceSearchLinks,
  Organization,
  OrganizationMember,
  OrganizationRoleDefinition,
  OrganizationTeam,
  OrganizationTeamMember,
  ProductIdentity,
  ResaleTask,
  Sale,
  SourcingRouteDetail,
  SourcingTrip,
  SourcingTripStop,
  SourcingTripSummary,
} from '../domain/types';

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  idempotencyKey?: string;
};

async function buildHeaders(includeContentType = true): Promise<Record<string, string>> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
  };

  if (includeContentType) {
    headers['Content-Type'] = 'application/json';
  }

  const token = authStore.getToken();
  const activeOrganizationId = await getActiveOrganizationId();

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  if (activeOrganizationId) {
    headers['X-ScoutHaul-Organization-Id'] = activeOrganizationId;
  }

  return headers;
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  if (!authStore.getToken()) {
    throw new Error('Sign in to access this feature.');
  }

  const response = await fetch(`${appConfig.apiBaseUrl}${path}`, {
    method: options.method ?? 'GET',
    headers: {
      ...(await buildHeaders()),
      ...(options.idempotencyKey ? { 'X-Idempotency-Key': options.idempotencyKey } : {}),
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

async function uploadMultipart<T>(path: string, formData: FormData, idempotencyKey?: string): Promise<T> {
  const response = await fetch(`${appConfig.apiBaseUrl}${path}`, {
    method: 'POST',
    headers: {
      ...(await buildHeaders(false)),
      ...(idempotencyKey ? { 'X-Idempotency-Key': idempotencyKey } : {}),
    },
    body: formData,
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(parseErrorMessage(text) || `Request failed with ${response.status}`);
  }

  return (await response.json()) as T;
}

export const api = {
  organizations: () => apiRequest<Organization[]>('/api/organizations'),
  createOrganization: (body: { name: string }) => apiRequest<Organization>('/api/organizations', { method: 'POST', body }),
  organizationMembers: (organizationId: string) => apiRequest<OrganizationMember[]>(`/api/organizations/${organizationId}/members`),
  addOrganizationMember: (organizationId: string, body: unknown) =>
    apiRequest<OrganizationMember>(`/api/organizations/${organizationId}/members`, { method: 'POST', body }),
  resendOrganizationMemberInvite: (organizationId: string, memberId: string) =>
    apiRequest<OrganizationMember>(`/api/organizations/${organizationId}/members/${memberId}/resend-invite`, { method: 'POST' }),
  removeOrganizationMember: (organizationId: string, memberId: string) =>
    apiRequest<void>(`/api/organizations/${organizationId}/members/${memberId}`, { method: 'DELETE' }),
  organizationPermissions: () => apiRequest<{ permissions: string[] }>('/api/organizations/permissions'),
  organizationRoles: (organizationId: string) => apiRequest<OrganizationRoleDefinition[]>(`/api/organizations/${organizationId}/roles`),
  createOrganizationRole: (organizationId: string, body: unknown) =>
    apiRequest<OrganizationRoleDefinition>(`/api/organizations/${organizationId}/roles`, { method: 'POST', body }),
  updateOrganizationRole: (organizationId: string, roleId: string, body: unknown) =>
    apiRequest<OrganizationRoleDefinition>(`/api/organizations/${organizationId}/roles/${roleId}`, { method: 'PUT', body }),
  deleteOrganizationRole: (organizationId: string, roleId: string) =>
    apiRequest<void>(`/api/organizations/${organizationId}/roles/${roleId}`, { method: 'DELETE' }),
  updateOrganizationMemberRole: (organizationId: string, memberId: string, body: unknown) =>
    apiRequest<OrganizationMember>(`/api/organizations/${organizationId}/members/${memberId}/role`, { method: 'PATCH', body }),
  organizationTeams: (organizationId: string) => apiRequest<OrganizationTeam[]>(`/api/organizations/${organizationId}/teams`),
  createOrganizationTeam: (organizationId: string, body: unknown) =>
    apiRequest<OrganizationTeam>(`/api/organizations/${organizationId}/teams`, { method: 'POST', body }),
  updateOrganizationTeam: (organizationId: string, teamId: string, body: unknown) =>
    apiRequest<OrganizationTeam>(`/api/organizations/${organizationId}/teams/${teamId}`, { method: 'PUT', body }),
  deleteOrganizationTeam: (organizationId: string, teamId: string) =>
    apiRequest<void>(`/api/organizations/${organizationId}/teams/${teamId}`, { method: 'DELETE' }),
  organizationTeamMembers: (organizationId: string, teamId: string) =>
    apiRequest<OrganizationTeamMember[]>(`/api/organizations/${organizationId}/teams/${teamId}/members`),
  addOrganizationTeamMember: (organizationId: string, teamId: string, body: unknown) =>
    apiRequest<OrganizationTeamMember>(`/api/organizations/${organizationId}/teams/${teamId}/members`, { method: 'POST', body }),
  removeOrganizationTeamMember: (organizationId: string, teamId: string, memberId: string) =>
    apiRequest<void>(`/api/organizations/${organizationId}/teams/${teamId}/members/${memberId}`, { method: 'DELETE' }),
  dashboard: () => apiRequest<DashboardSummary>('/api/dashboard/summary'),
  inventory: () => apiRequest<InventoryItem[]>('/api/inventory'),
  inventoryItem: (id: string) => apiRequest<InventoryItem>(`/api/inventory/${id}`),
  createInventory: (body: unknown) => apiRequest<InventoryItem>('/api/inventory', { method: 'POST', body }),
  updateInventory: (id: string, body: unknown) => apiRequest<InventoryItem>(`/api/inventory/${id}`, { method: 'PUT', body }),
  deleteInventory: (id: string) => apiRequest<void>(`/api/inventory/${id}`, { method: 'DELETE' }),
  uploadInventoryPhoto: (id: string, uri: string, isHero: boolean) => {
    const form = new FormData();
    form.append('file', {
      uri,
      name: `inventory-${Date.now()}.jpg`,
      type: 'image/jpeg',
    } as unknown as Blob);
    form.append('isHero', String(isHero));
    return uploadMultipart<{ inventoryItemId: string; photoUrls: string[]; uploadedUrl?: string | null }>(`/api/inventory/${id}/photos`, form);
  },
  deleteInventoryPhoto: (id: string, url: string) =>
    apiRequest<{ inventoryItemId: string; photoUrls: string[]; uploadedUrl?: string | null }>(
      `/api/inventory/${id}/photos?url=${encodeURIComponent(url)}`,
      { method: 'DELETE' },
    ),
  setInventoryHeroPhoto: (id: string, url: string) =>
    apiRequest<{ inventoryItemId: string; photoUrls: string[]; uploadedUrl?: string | null }>(
      `/api/inventory/${id}/photos/hero?url=${encodeURIComponent(url)}`,
      { method: 'PATCH' },
    ),
  changeInventoryStatus: (id: string, status: string) =>
    apiRequest<InventoryItem>(`/api/inventory/${id}/status`, { method: 'PATCH', body: { status } }),
  searchProducts: (search: string) =>
    apiRequest<ProductIdentity[]>(`/api/product-identities?search=${encodeURIComponent(search)}`),
  getProductIdentity: (id: string) => apiRequest<ProductIdentity>(`/api/product-identities/${id}`),
  createProductIdentity: (body: {
    title: string;
    brand?: string | null;
    model?: string | null;
    category?: string | null;
    upc?: string | null;
    ean?: string | null;
    isbn?: string | null;
    searchQuery?: string | null;
    notes?: string | null;
  }) => apiRequest<ProductIdentity>('/api/product-identities', { method: 'POST', body }),
  updateProductIdentity: (id: string, body: {
    title: string;
    brand?: string | null;
    model?: string | null;
    category?: string | null;
    upc?: string | null;
    ean?: string | null;
    isbn?: string | null;
    searchQuery?: string | null;
    notes?: string | null;
  }) => apiRequest<ProductIdentity>(`/api/product-identities/${id}`, { method: 'PUT', body }),
  deleteProductIdentity: (id: string) => apiRequest<void>(`/api/product-identities/${id}`, { method: 'DELETE' }),
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
  currentBudget: () => apiRequest<Budget>('/api/budgets/current'),
  createBudget: (body: unknown) => apiRequest<Budget>('/api/budgets', { method: 'POST', body }),
  deleteBudget: (id: string) => apiRequest<void>(`/api/budgets/${id}`, { method: 'DELETE' }),
  addExpense: (body: unknown) => apiRequest('/api/budgets/expenses', { method: 'POST', body }),
  expenses: () => apiRequest<Expense[]>('/api/budgets/expenses'),
  deleteExpense: (id: string) => apiRequest<void>(`/api/budgets/expenses/${id}`, { method: 'DELETE' }),
  listings: () => apiRequest<Listing[]>('/api/listings'),
  createListing: (body: unknown) => apiRequest<Listing>('/api/listings', { method: 'POST', body }),
  updateListing: (id: string, body: unknown) => apiRequest<Listing>(`/api/listings/${id}`, { method: 'PUT', body }),
  markListingListed: (id: string) => apiRequest<Listing>(`/api/listings/${id}/mark-listed`, { method: 'POST' }),
  deleteListing: (id: string) => apiRequest<void>(`/api/listings/${id}`, { method: 'DELETE' }),
  sales: () => apiRequest<Sale[]>('/api/sales'),
  recordSale: (body: unknown) => apiRequest<Sale>('/api/sales', { method: 'POST', body }),
  markSalePacked: (id: string) => apiRequest<Sale>(`/api/sales/${id}/packed`, { method: 'PATCH' }),
  markSaleShipped: (id: string) => apiRequest<Sale>(`/api/sales/${id}/shipped`, { method: 'PATCH' }),
  markSaleDelivered: (id: string) => apiRequest<Sale>(`/api/sales/${id}/delivered`, { method: 'PATCH' }),
  tasks: () => apiRequest<ResaleTask[]>('/api/tasks'),
  createTask: (body: unknown) => apiRequest<ResaleTask>('/api/tasks', { method: 'POST', body }),
  updateTaskStatus: (id: string, status: string) =>
    apiRequest<ResaleTask>(`/api/tasks/${id}/status`, { method: 'PATCH', body: { status } }),
  deleteTask: (id: string) => apiRequest<void>(`/api/tasks/${id}`, { method: 'DELETE' }),
};
