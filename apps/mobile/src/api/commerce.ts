import { apiRequest } from './client';
import type { CreateListingRequest, UpdateListingRequest } from './listingRequests';
import type { Budget, BudgetSummary, Expense, ExpenseCategory, Listing } from '../domain/types';

export type {
  CreateListingRequest,
  ListingEdits,
  UpdateListingRequest,
} from './listingRequests';
export { listingUpdateFrom } from './listingRequests';

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

  budgetSummary: () => apiRequest<BudgetSummary>('/api/budgets/summary'),
  currentBudget: () => apiRequest<Budget>('/api/budgets/current'),
  createBudget: (body: CreateBudgetRequest) => apiRequest<Budget>('/api/budgets', { method: 'POST', body }),
  deleteBudget: (id: string) => apiRequest<void>(`/api/budgets/${id}`, { method: 'DELETE' }),
  addExpense: (body: CreateExpenseRequest) => apiRequest<Expense>('/api/budgets/expenses', { method: 'POST', body }),
  expenses: () => apiRequest<Expense[]>('/api/budgets/expenses'),
  deleteExpense: (id: string) => apiRequest<void>(`/api/budgets/expenses/${id}`, { method: 'DELETE' }),
};
