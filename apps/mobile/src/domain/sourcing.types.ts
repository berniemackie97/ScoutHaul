export type SourcingRouteStatus = 'Draft' | 'Planned' | 'Active' | 'Paused' | 'Completed' | 'Cancelled';
export type SourcingRouteType = 'General' | 'GarageSales' | 'ThriftRun' | 'PickupRun' | 'AuctionPickup' | 'Liquidation' | 'Mixed';
export type SourcingStopSourceType =
  | 'GarageSale'
  | 'EstateSale'
  | 'ThriftStore'
  | 'Goodwill'
  | 'FacebookPickup'
  | 'Auction'
  | 'Liquidation'
  | 'FleaMarket'
  | 'Other';
export type SourcingStopPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type SourcingStopStatus = 'Planned' | 'Navigating' | 'Arrived' | 'InProgress' | 'Completed' | 'Skipped' | 'Cancelled' | 'AtRisk';

export type SourcingTrip = {
  id: string;
  assignedToUserId?: string | null;
  name: string;
  startedAt: string;
  endedAt?: string | null;
  routeDate?: string | null;
  plannedStartAt?: string | null;
  plannedEndAt?: string | null;
  routeType: SourcingRouteType;
  status: SourcingRouteStatus;
  cashBudget?: number | null;
  notes?: string | null;
  itemCount: number;
  stopCount: number;
  createdAt: string;
};

export type SourcingTripSummary = {
  id: string;
  name: string;
  itemCount: number;
  stopCount: number;
  completedStopCount: number;
  skippedStopCount: number;
  purchaseCount: number;
  totalPurchaseCost: number;
  totalAskingPrice: number;
  cashBudget?: number | null;
  remainingBudget: number;
  plannedMinutes: number;
  actualMinutes: number;
  scheduleVarianceMinutes: number;
  status: SourcingRouteStatus;
};

export type SourcingStopChecklistItem = {
  id: string;
  sourcingTripStopId: string;
  text: string;
  sortOrder: number;
  isRequired: boolean;
  isCompleted: boolean;
  completedAt?: string | null;
};

export type SourcingTripStop = {
  id: string;
  sourcingTripId: string;
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
  status: SourcingStopStatus;
  actualArrivalAt?: string | null;
  actualDepartureAt?: string | null;
  totalPurchaseCost: number;
  purchaseCount: number;
  checklistItems: SourcingStopChecklistItem[];
  createdAt: string;
};

export type SourcingRouteDetail = {
  trip: SourcingTrip;
  summary: SourcingTripSummary;
  stops: SourcingTripStop[];
};
