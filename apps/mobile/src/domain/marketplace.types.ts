export type MarketObservationType =
  | 'Sold'
  | 'ActiveListing'
  | 'CompletedUnsold'
  | 'ManualSold'
  | 'ManualActive'
  | 'InternalSale'
  | 'Estimate'
  | 'Unknown';

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
