import { apiRequest, type CallOptions } from './client';
import type {
  DashboardSummary,
  MarketObservation,
  MarketplaceSearchLinks,
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

export const marketplaceApi = {
  dashboard: () => apiRequest<DashboardSummary>('/api/dashboard/summary'),

  marketplaceLinks: (query: string) =>
    apiRequest<MarketplaceSearchLinks>(`/api/marketplace-links?query=${encodeURIComponent(query)}`),

  createProductIdentity: (body: CreateProductIdentityRequest) =>
    apiRequest<ProductIdentity>('/api/product-identities', { method: 'POST', body }),

  addComp: (body: CreateCompRequest) =>
    apiRequest<MarketObservation>('/api/market-observations', { method: 'POST', body }),
  comps: (params: string) => apiRequest<MarketObservation[]>(`/api/market-observations?${params}`),

  // Generate a ready-to-post marketplace listing (title, description, comp-backed price) from item details.
  generateListing: (body: GenerateListingRequest, options?: CallOptions) =>
    apiRequest<GeneratedListing>('/api/listing/generate', { method: 'POST', body, ...options }),

};

export type GenerateListingRequest = {
  title: string;
  brand?: string | null;
  model?: string | null;
  category?: string | null;
  condition?: string | null;
  color?: string | null;
  sizeSystem?: string | null;
  sizeValue?: string | null;
  attributesJson?: string | null;
  notes?: string | null;
};

export type GeneratedListing = {
  listingTitle: string;
  listingDescription: string;
  suggestedListPrice: number | null;
  estimatedResaleLow: number | null;
  estimatedResaleHigh: number | null;
  priceRationale: string | null;
  comps: ResaleComp[];
};

export type ResaleComp = {
  price: number;
  status: 'sold' | 'active';
  condition?: string | null;
  note?: string | null;
  sourceUrl?: string | null;
};
