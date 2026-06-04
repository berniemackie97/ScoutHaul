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
  productTitle?: string | null;
  productBrand?: string | null;
  productImageUrl?: string | null;
  productCategory?: string | null;
  productIdentityId?: string | null;
  savedObservationCount: number;
  providersUsed?: string[] | null;
  providerWarnings?: string[] | null;
  // AI resale estimate — present when no live comps were available.
  estimatedResaleLow?: number | null;
  estimatedResaleHigh?: number | null;
  estimateSource?: string | null;
  estimateRationale?: string | null;
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
