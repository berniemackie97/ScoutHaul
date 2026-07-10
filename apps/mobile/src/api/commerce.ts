import { apiRequest } from './client';
import type { Budget, BudgetSummary, Expense, ExpenseCategory, Listing, ListingStatus, Sale, SaleStatus } from '../domain/types';

export type CreateListingRequest = {
  inventoryItemId: string;
  platform: string;
  listingUrl?: string | null;
  listPrice: number;
  notes?: string | null;
};

export type UpdateListingRequest = {
  platform: string;
  listingUrl?: string | null;
  listPrice: number;
  status?: ListingStatus;
  notes?: string | null;
};

export type RecordSaleRequest = {
  inventoryItemId: string;
  listingId?: string | null;
  platform: string;
  soldPrice: number;
  platformFees: number;
  paymentFees: number;
  shippingCost: number;
  suppliesCost: number;
  otherCosts: number;
  salesTaxCollected: number;
  taxRemittedByPlatform: boolean;
  shippingCharged?: number;
  soldAt: string;
  notes?: string | null;
  externalOrderId?: string | null;
  buyerReference?: string | null;
  buyerName?: string | null;
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
};

export type SaleChannelOption = { value: string; label: string; group: string; isLegacy?: boolean };
export type SalesReferenceData = { channels: SaleChannelOption[] };
export type SaleStatusTransitionRequest = {
  status: SaleStatus;
  occurredAt?: string | null;
  reason?: string | null;
  carrier?: string | null;
  trackingNumber?: string | null;
};

export type CreateBudgetRequest = {
  name: string;
  startingAmount: number;
  startsAt?: string;
  endsAt?: string | null;
};

export type CreateExpenseRequest = {
  inventoryItemId?: string | null;
  sourcingTripId?: string | null;
  sourcingTripStopId?: string | null;
  category: ExpenseCategory;
  description: string;
  amount: number;
  occurredAt?: string;
};

export const commerceApi = {
  listings: () => apiRequest<Listing[]>('/api/listings'),
  createListing: (body: CreateListingRequest) => apiRequest<Listing>('/api/listings', { method: 'POST', body }),
  updateListing: (id: string, body: UpdateListingRequest) => apiRequest<Listing>(`/api/listings/${id}`, { method: 'PUT', body }),
  markListingListed: (id: string) => apiRequest<Listing>(`/api/listings/${id}/mark-listed`, { method: 'POST' }),
  deleteListing: (id: string) => apiRequest<void>(`/api/listings/${id}`, { method: 'DELETE' }),

  sales: () => apiRequest<Sale[]>('/api/sales'),
  sale: (id: string) => apiRequest<Sale>(`/api/sales/${id}`),
  salesReferenceData: () => apiRequest<SalesReferenceData>('/api/sales/reference-data'),
  recordSale: (body: RecordSaleRequest) => apiRequest<Sale>('/api/sales', { method: 'POST', body }),
  transitionSale: (id: string, body: SaleStatusTransitionRequest) => apiRequest<Sale>(`/api/sales/${id}/status`, { method: 'PATCH', body }),
  markSalePacked: (id: string) => apiRequest<Sale>(`/api/sales/${id}/packed`, { method: 'PATCH' }),
  markSaleShipped: (id: string) => apiRequest<Sale>(`/api/sales/${id}/shipped`, { method: 'PATCH' }),
  markSaleDelivered: (id: string) => apiRequest<Sale>(`/api/sales/${id}/delivered`, { method: 'PATCH' }),

  budgetSummary: () => apiRequest<BudgetSummary>('/api/budgets/summary'),
  currentBudget: () => apiRequest<Budget>('/api/budgets/current'),
  createBudget: (body: CreateBudgetRequest) => apiRequest<Budget>('/api/budgets', { method: 'POST', body }),
  deleteBudget: (id: string) => apiRequest<void>(`/api/budgets/${id}`, { method: 'DELETE' }),
  addExpense: (body: CreateExpenseRequest) => apiRequest<Expense>('/api/budgets/expenses', { method: 'POST', body }),
  expenses: () => apiRequest<Expense[]>('/api/budgets/expenses'),
  deleteExpense: (id: string) => apiRequest<void>(`/api/budgets/expenses/${id}`, { method: 'DELETE' }),
};
