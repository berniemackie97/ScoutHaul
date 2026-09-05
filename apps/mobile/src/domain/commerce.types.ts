export type ListingStatus = 'Draft' | 'Active' | 'Ended' | 'Sold' | 'Cancelled';
export type WorkTaskStatus = 'Open' | 'InProgress' | 'Blocked' | 'Completed' | 'Cancelled';
export type ExpenseCategory =
  | 'InventoryPurchase'
  | 'Shipping'
  | 'Supplies'
  | 'PlatformFee'
  | 'PaymentFee'
  | 'Refund'
  | 'Fuel'
  | 'Storage'
  | 'Other';

export type Listing = {
  id: string;
  inventoryItemId: string;
  inventoryItemTitle: string;
  inventoryItemStatus: import('./inventory.types').InventoryStatus;
  platform: string;
  listingUrl?: string | null;
  listPrice: number;
  status: ListingStatus;
  listedAt?: string | null;
  endedAt?: string | null;
  notes?: string | null;
  /** What the buyer reads on the marketplace. Null until the copy has been written. */
  title?: string | null;
  description?: string | null;
};

export type ResaleTask = {
  id: string;
  inventoryItemId?: string | null;
  assignedToUserId?: string | null;
  title: string;
  description?: string | null;
  status: WorkTaskStatus;
  dueAt?: string | null;
  createdAt: string;
};

export type BudgetSummary = {
  budgetId?: string | null;
  startingAmount: number;
  currentAmount: number;
  totalExpenses: number;
  inventoryPurchases: number;
  remainingAmount: number;
};

export type Budget = {
  id: string;
  name: string;
  startingAmount: number;
  currentAmount: number;
  startsAt: string;
  endsAt?: string | null;
  isActive: boolean;
};

export type Expense = {
  id: string;
  inventoryItemId?: string | null;
  sourcingTripId?: string | null;
  sourcingTripStopId?: string | null;
  category: ExpenseCategory;
  description: string;
  amount: number;
  occurredAt: string;
};
