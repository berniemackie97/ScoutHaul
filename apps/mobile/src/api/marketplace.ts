import { apiRequest } from './client';
import type {
  DashboardSummary,
  MarketAnalysisResponse,
  MarketObservation,
  MarketplaceLookupResult,
  MarketplaceSearchLinks,
  PricingBasisType,
  ProductIdentity,
} from '../domain/types';

export type CreateProductIdentityRequest = {
  title: string;
  brand?: string | null;
  model?: string | null;
  category?: string | null;
  upc?: string | null;
  ean?: string | null;
  isbn?: string | null;
  searchQuery?: string | null;
  notes?: string | null;
};

export type CreateCompRequest = {
  productIdentityId?: string | null;
  inventoryItemId?: string | null;
  observationType: string;
  source: string;
  sourceUrl?: string | null;
  title: string;
  price: number;
  shippingPrice?: number | null;
  condition?: string | null;
  observedAt: string;
  soldAt?: string | null;
  notes?: string | null;
};

export type AnalyzeRequest = {
  productIdentityId?: string | null;
  inventoryItemId?: string | null;
  expectedSalePrice?: number | null;
  sellerAskingPrice?: number | null;
  purchasePrice?: number | null;
  platform: string;
  platformFeePercent: number;
  paymentFeePercent: number;
  fixedPaymentFee: number;
  estimatedShippingCostPaidBySeller: number;
  suppliesCost: number;
  riskReserve: number;
  desiredMinimumProfit: number;
  targetMarginPercent?: number | null;
  pricingBasis?: PricingBasisType | null;
  condition?: string | null;
  riskFlags: string[];
};

export const marketplaceApi = {
  dashboard: () => apiRequest<DashboardSummary>('/api/dashboard/summary'),

  lookupBarcode: (barcode: string) =>
    apiRequest<MarketplaceLookupResult>(
      `/api/marketplace-lookup/barcode/${encodeURIComponent(barcode)}`
    ),
  lookupSearch: (query: string) =>
    apiRequest<MarketplaceLookupResult>(
      `/api/marketplace-lookup/search?q=${encodeURIComponent(query)}`
    ),
  marketplaceLinks: (query: string) =>
    apiRequest<MarketplaceSearchLinks>(`/api/marketplace-links?query=${encodeURIComponent(query)}`),

  searchProducts: (search: string) =>
    apiRequest<ProductIdentity[]>(`/api/product-identities?search=${encodeURIComponent(search)}`),
  getProductIdentity: (id: string) => apiRequest<ProductIdentity>(`/api/product-identities/${id}`),
  createProductIdentity: (body: CreateProductIdentityRequest) =>
    apiRequest<ProductIdentity>('/api/product-identities', { method: 'POST', body }),
  updateProductIdentity: (id: string, body: CreateProductIdentityRequest) =>
    apiRequest<ProductIdentity>(`/api/product-identities/${id}`, { method: 'PUT', body }),
  deleteProductIdentity: (id: string) =>
    apiRequest<void>(`/api/product-identities/${id}`, { method: 'DELETE' }),

  addComp: (body: CreateCompRequest) =>
    apiRequest<MarketObservation>('/api/market-observations', { method: 'POST', body }),
  comps: (params: string) => apiRequest<MarketObservation[]>(`/api/market-observations?${params}`),
  analyze: (body: AnalyzeRequest) =>
    apiRequest<MarketAnalysisResponse>('/api/market-analysis/calculate', { method: 'POST', body }),

  visionIdentify: (imageBase64?: string | null, signal?: AbortSignal, context?: string | null) =>
    apiRequest<VisionIdentifyResult>('/api/vision/identify', {
      method: 'POST',
      body: { imageBase64: imageBase64 || undefined, context: context || undefined },
      signal,
    }),

  visionIdentifyText: (context: string, signal?: AbortSignal) =>
    apiRequest<VisionIdentifyResult>('/api/vision/identify', {
      method: 'POST',
      body: { context },
      signal,
    }),

  providerStatus: () => apiRequest<MarketplaceProviderStatus>('/api/marketplace-lookup/status'),
};

export type MarketplaceProviderStatus = {
  configuredProviders: string[];
  hasProviders: boolean;
  visionConfigured: boolean;
};

export type VisionIdentifyResult = {
  product: ProductIdentification | null;
  searchQuery: string | null;
  detectedText: string | null;
};

export type ProductIdentification = {
  name: string | null;
  brand: string | null;
  model: string | null;
  category: string | null;
  color: string | null;
  condition: string | null;
  conditionQuality?: string | null; // new | like-new | good | fair | poor | for-parts
  estimatedResaleLow: number | null;
  estimatedResaleHigh: number | null;
  identifyingText: string | null;
  searchKeywords: string[];
  confidence: 'high' | 'medium' | 'low'; // identification (variant) confidence
  valuationConfidence?: 'high' | 'medium' | 'low' | null;
  priceRangeType?: 'tight' | 'moderate' | 'wide' | null;
  keyValueFactors?: string[];
  missingInformation?: string[];
  recommendedNextSteps?: string[];
  rationale?: string | null;
  comps?: ResaleComp[];
};

export type ResaleComp = {
  price: number;
  status: 'sold' | 'active';
  condition?: string | null;
  note?: string | null;
  sourceUrl?: string | null;
};
