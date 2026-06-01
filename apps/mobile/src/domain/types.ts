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

export type OrganizationRole = 'Owner' | 'Admin' | 'Member' | 'Viewer';
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

export type InventoryItem = {
  id: string;
  productIdentityId?: string | null;
  sourcingTripId?: string | null;
  sourcingTripStopId?: string | null;
  sourceLocationId?: string | null;
  title: string;
  description?: string | null;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
  color?: string | null;
  condition?: string | null;
  sizeSystem?: string | null;
  sizeValue?: string | null;
  measurementsJson?: string | null;
  storageLocation?: string | null;
  accessories?: string | null;
  missingParts?: string | null;
  testingNotes?: string | null;
  cleaningNotes?: string | null;
  listingNotes?: string | null;
  status: InventoryStatus;
  sellerAskingPrice?: number | null;
  purchasePrice?: number | null;
  estimatedSalePrice?: number | null;
  barcode?: string | null;
  notes?: string | null;
  photoUrls: string[];
  photos?: InventoryPhoto[];
  heroPhotoThumbnailUrl?: string | null;
  isArchived: boolean;
  createdAt: string;
  updatedAt: string;
};

export type InventoryPhoto = {
  id: string;
  inventoryItemId: string;
  isHero: boolean;
  sortOrder: number;
  originalUrl: string;
  thumbnailUrl: string;
  contentType: string;
  originalByteSize: number;
  thumbnailByteSize: number;
  width?: number | null;
  height?: number | null;
  createdAt: string;
};

export type InventoryIssue = {
  id: string;
  inventoryItemId: string;
  inventoryPhotoId?: string | null;
  issueType: string;
  severity: string;
  note?: string | null;
  sortOrder: number;
  isResolved: boolean;
  createdAt: string;
  updatedAt: string;
};

export type SessionBootstrap = {
  user: {
    id: string;
    email: string;
    displayName: string;
  };
  organizations: Organization[];
  selectedOrganizationId: string;
  remoteCounts: {
    inventoryItems: number;
    budgets: number;
    marketObservations: number;
    listings: number;
    tasks: number;
    pendingSyncOperations: number;
  };
  serverTime: string;
};

export type Organization = {
  id: string;
  name: string;
  createdAt: string;
};

export type OrganizationRoleDefinition = {
  id: string;
  name: string;
  description?: string | null;
  isSystem: boolean;
  isOwnerRole: boolean;
  permissions: string[];
};

export type OrganizationMember = {
  id: string;
  userId: string;
  email: string;
  displayName: string;
  role: OrganizationRole;
  roleId?: string | null;
  roleName?: string | null;
  inviteStatus: 'Pending' | 'Active' | string;
  joinedAt: string;
};

export type OrganizationTeam = {
  id: string;
  organizationId: string;
  name: string;
  description?: string | null;
  memberCount: number;
  createdAt: string;
};

export type OrganizationTeamMember = {
  id: string;
  teamId: string;
  userId: string;
  email: string;
  displayName: string;
  roleId?: string | null;
  roleName?: string | null;
  joinedAt: string;
};

export type OrganizationInviteCode = {
  id: string;
  organizationId: string;
  teamId?: string | null;
  teamName?: string | null;
  roleId?: string | null;
  roleName?: string | null;
  name: string;
  codePrefix: string;
  isActive: boolean;
  expiresAt?: string | null;
  maxUses?: number | null;
  useCount: number;
  createdAt: string;
};

export type OrganizationInviteCodeCreated = {
  invite: OrganizationInviteCode;
  code: string;
};

export type OrganizationJoinResult = {
  organization: Organization;
  member: OrganizationMember;
  teamId?: string | null;
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

export type Listing = {
  id: string;
  inventoryItemId: string;
  inventoryItemTitle: string;
  inventoryItemStatus: InventoryStatus;
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
  /** Product identity from the configured barcode provider chain. */
  productTitle?: string | null;
  productBrand?: string | null;
  productImageUrl?: string | null;
  productCategory?: string | null;
  productIdentityId?: string | null;
  savedObservationCount: number;
  providersUsed?: string[] | null;
  providerWarnings?: string[] | null;
};

export type BarcodeLookupResult = {
  barcode: string;
  title?: string | null;
  brand?: string | null;
  description?: string | null;
  category?: string | null;
  imageUrl?: string | null;
  upc?: string | null;
  ean?: string | null;
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
