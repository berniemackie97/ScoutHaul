export type InventoryStatus =
  | 'Draft'
  | 'Researching'
  | 'Watchlist'
  | 'Purchased'
  | 'NeedsCleaning'
  | 'NeedsTesting'
  | 'NeedsPhotos'
  | 'ReadyToList'
  | 'Listed'
  | 'Sold'
  | 'Packed'
  | 'Shipped'
  | 'Delivered'
  | 'Returned'
  | 'Donated'
  | 'Disposed';

export type MarketObservationType =
  | 'Sold'
  | 'ActiveListing'
  | 'CompletedUnsold'
  | 'ManualSold'
  | 'ManualActive'
  | 'InternalSale'
  | 'Estimate'
  | 'Unknown';

export type PricingBasisType =
  | 'LiveValidated'
  | 'CachedRecent'
  | 'CachedStale'
  | 'ManualEstimate'
  | 'InternalHistory'
  | 'Unavailable';

export type RecommendationDecisionType =
  | 'Buy'
  | 'Negotiate'
  | 'Pass'
  | 'ResearchRequired'
  | 'Unavailable';

export type ListingStatus = 'Draft' | 'Active' | 'Ended' | 'Sold' | 'Cancelled';
export type WorkTaskStatus = 'Open' | 'InProgress' | 'Blocked' | 'Completed' | 'Cancelled';

export type InventoryItem = {
  id: string;
  productIdentityId?: string | null;
  sourcingTripId?: string | null;
  sourceLocationId?: string | null;
  title: string;
  description?: string | null;
  condition?: string | null;
  status: InventoryStatus;
  sellerAskingPrice?: number | null;
  purchasePrice?: number | null;
  estimatedSalePrice?: number | null;
  barcode?: string | null;
  notes?: string | null;
  isArchived: boolean;
  createdAt: string;
  updatedAt: string;
};

export type ProductIdentity = {
  id: string;
  title: string;
  brand?: string | null;
  model?: string | null;
  category?: string | null;
  upc?: string | null;
  ean?: string | null;
  isbn?: string | null;
  sku?: string | null;
  searchQuery?: string | null;
  notes?: string | null;
  createdAt: string;
  updatedAt: string;
};

export type MarketObservation = {
  id: string;
  productIdentityId?: string | null;
  inventoryItemId?: string | null;
  observationType: MarketObservationType;
  source: string;
  sourceUrl?: string | null;
  title: string;
  price: number;
  shippingPrice?: number | null;
  totalPrice: number;
  condition?: string | null;
  observedAt: string;
  soldAt?: string | null;
  notes?: string | null;
};

export type MarketplaceSearchLink = {
  provider: string;
  label: string;
  url: string;
};

export type MarketplaceSearchLinks = {
  query: string;
  links: MarketplaceSearchLink[];
};

export type DealAnalysisResult = {
  recommendedMaxBuyPrice?: number | null;
  expectedNetProfit?: number | null;
  roiPercent?: number | null;
  marginPercent?: number | null;
  breakEvenPrice?: number | null;
  decision: RecommendationDecisionType;
  warnings: string[];
};

export type MarketAnalysisResponse = {
  analysisId: string;
  recommendationId: string;
  estimatedSalePrice?: number | null;
  pricingBasis: PricingBasisType;
  soldCompCount: number;
  activeListingCount: number;
  confidence: {
    score: number;
    reasons: string[];
    warnings: string[];
  };
  recommendation: DealAnalysisResult;
};

export type DashboardSummary = {
  currentInventoryCount: number;
  activeListingsCount: number;
  totalInventoryCost: number;
  estimatedInventoryValue: number;
  grossSales: number;
  netProfit: number;
  cashDeployed: number;
  unlistedInventoryCount: number;
  staleInventoryCount: number;
};

export type SourcingTrip = {
  id: string;
  name: string;
  startedAt: string;
  endedAt?: string | null;
  cashBudget?: number | null;
  notes?: string | null;
  itemCount: number;
  createdAt: string;
};

export type SourcingTripSummary = {
  id: string;
  name: string;
  itemCount: number;
  totalPurchaseCost: number;
  totalAskingPrice: number;
  cashBudget?: number | null;
  remainingBudget: number;
};

export type BudgetSummary = {
  budgetId?: string | null;
  startingAmount: number;
  currentAmount: number;
  totalExpenses: number;
  inventoryPurchases: number;
  remainingAmount: number;
};

export type Listing = {
  id: string;
  inventoryItemId: string;
  platform: string;
  listingUrl?: string | null;
  listPrice: number;
  status: ListingStatus;
  listedAt?: string | null;
  endedAt?: string | null;
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

export type MarketplaceListing = {
  title: string;
  price: number;
  shippingCost?: number | null;
  imageUrl?: string | null;
  itemUrl?: string | null;
  condition: string;
  source: string;
  isSold: boolean;
  soldDate?: string | null;
};

export type MarketplaceLookupResult = {
  query: string;
  listings: MarketplaceListing[];
  averageSoldPrice?: number | null;
  lowestPrice?: number | null;
  highestPrice?: number | null;
  totalResults: number;
};

export type LocalItemDraft = {
  id: string;
  title: string;
  barcode?: string | null;
  sellerAskingPrice?: number | null;
  condition?: string | null;
  notes?: string | null;
  sourceName?: string | null;
  expectedSalePrice?: number | null;
  needsResearch: boolean;
  syncStatus: 'local' | 'queued' | 'synced' | 'failed';
  createdAt: string;
  updatedAt: string;
};
