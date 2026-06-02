export type ListingStatus = 'Draft' | 'Active' | 'Ended' | 'Sold' | 'Cancelled';
export type SaleStatus = 'Recorded' | 'Packed' | 'Shipped' | 'Delivered' | 'Returned';
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
};

export type Sale = {
  id: string;
  inventoryItemId: string;
  inventoryItemTitle: string;
  listingId?: string | null;
  platform: string;
  soldPrice: number;
  platformFees: number;
  paymentFees: number;
  shippingCost: number;
  suppliesCost: number;
  otherCosts: number;
  netProfit: number;
  status: SaleStatus;
  soldAt: string;
  notes?: string | null;
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
