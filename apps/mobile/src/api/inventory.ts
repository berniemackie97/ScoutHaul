import { apiRequest, uploadMultipart } from './client';
import type {
  DimensionUnit,
  InventoryAuthenticityStatus,
  InventoryCompletenessStatus,
  InventoryFunctionalStatus,
  InventoryItem,
  InventoryIssue,
  InventoryOwnershipType,
  InventoryPackagingStatus,
  InventoryPhoto,
  InventoryStatus,
  WeightUnit,
} from '../domain/types';

export type PhotoResponse = {
  inventoryItemId: string;
  photoUrls: string[];
  uploadedUrl?: string | null;
  photos: InventoryPhoto[];
};

export type InventoryReferenceOption = {
  value: string;
  label: string;
  group?: string | null;
  parentValue?: string | null;
  isLegacy?: boolean;
};

/** The optional fields an item profile switches on; title, category, condition, brand, money and shipping always apply. */
export type ItemField =
  | 'Model'
  | 'Color'
  | 'Size'
  | 'FunctionalStatus'
  | 'CompletenessStatus'
  | 'AuthenticityStatus'
  | 'PackagingStatus'
  | 'ManufacturerPartNumber'
  | 'SerialNumber'
  | 'ManufactureYear'
  | 'CountryOfOrigin';

export type ItemFieldSpec = { field: ItemField; label: string; readiness: 'Optional' | 'Recommended' | 'Required' };

/** A detail kept in the item's attributes: free text, one pick from `options`, or up to `maxValues` of them. */
export type ItemAttribute = { key: string; label: string; options?: string[] | null; maxValues: number };

/** Which fields a kind of item has and what they are called — the table the API enforces on save. */
export type ItemProfile = {
  key: string;
  label: string;
  categories: string[];
  fields: ItemFieldSpec[];
  attributes: ItemAttribute[];
  sizeSystems: string[];
  brandLabel: string;
};

export type InventoryReferenceData = {
  profiles: ItemProfile[];
  categories: InventoryReferenceOption[];
  brands: InventoryReferenceOption[];
  models: InventoryReferenceOption[];
  conditions: InventoryReferenceOption[];
  colors: InventoryReferenceOption[];
  sizeSystems: InventoryReferenceOption[];
  sizes: InventoryReferenceOption[];
  storageLocations: InventoryReferenceOption[];
  countries: InventoryReferenceOption[];
};

export type CreateInventoryRequest = {
  title: string;
  category?: string | null;
  brand?: string | null;
  model?: string | null;
  /** Main color first; the API takes two. Omit to leave the item's colors as they are. */
  colors?: string[] | null;
  condition?: string | null;
  sizeSystem?: string | null;
  sizeValue?: string | null;
  attributesJson?: string | null;
  measurementsJson?: string | null;
  storageLocation?: string | null;
  accessories?: string | null;
  missingParts?: string | null;
  testingNotes?: string | null;
  cleaningNotes?: string | null;
  listingNotes?: string | null;
  purchasePrice?: number | null;
  estimatedSalePrice?: number | null;
  sellerAskingPrice?: number | null;
  barcode?: string | null;
  notes?: string | null;
  description?: string | null;
  productIdentityId?: string | null;
  sourcingTripId?: string | null;
  sourcingTripStopId?: string | null;
  sourceLocationId?: string | null;
  ownershipType?: InventoryOwnershipType | null;
  functionalStatus?: InventoryFunctionalStatus | null;
  completenessStatus?: InventoryCompletenessStatus | null;
  authenticityStatus?: InventoryAuthenticityStatus | null;
  packagingStatus?: InventoryPackagingStatus | null;
  manufacturerPartNumber?: string | null;
  serialNumber?: string | null;
  countryOfOrigin?: string | null;
  manufactureYear?: number | null;
  length?: number | null;
  width?: number | null;
  height?: number | null;
  dimensionUnit?: DimensionUnit | null;
  weight?: number | null;
  weightUnit?: WeightUnit | null;
  status?: InventoryStatus | null;
};

export type CreateIssueRequest = {
  issueType: string;
  severity: string;
  note?: string | null;
  inventoryPhotoId?: string | null;
  sortOrder?: number | null;
  isResolved: boolean;
};

export const inventoryApi = {
  list: () => apiRequest<InventoryItem[]>('/api/inventory'),
  get: (id: string) => apiRequest<InventoryItem>(`/api/inventory/${id}`),
  referenceData: () => apiRequest<InventoryReferenceData>('/api/inventory/reference-data'),
  create: (body: CreateInventoryRequest) => apiRequest<InventoryItem>('/api/inventory', { method: 'POST', body }),
  update: (id: string, body: CreateInventoryRequest) => apiRequest<InventoryItem>(`/api/inventory/${id}`, { method: 'PUT', body }),
  archive: (id: string) => apiRequest<void>(`/api/inventory/${id}`, { method: 'DELETE' }),
  setExpectedPrice: (id: string, estimatedSalePrice: number) =>
    apiRequest<InventoryItem>(`/api/inventory/${id}/price`, { method: 'PATCH', body: { estimatedSalePrice } }),
  changeStatus: (id: string, status: InventoryStatus) =>
    apiRequest<InventoryItem>(`/api/inventory/${id}/status`, { method: 'PATCH', body: { status } }),

  uploadPhoto: (id: string, uri: string, isHero: boolean) => {
    const form = new FormData();
    form.append('file', { uri, name: `inventory-${Date.now()}.jpg`, type: 'image/jpeg' } as unknown as Blob);
    form.append('isHero', String(isHero));
    return uploadMultipart<PhotoResponse>(`/api/inventory/${id}/photos`, form);
  },
  deletePhoto: (id: string, url: string) =>
    apiRequest<PhotoResponse>(`/api/inventory/${id}/photos?url=${encodeURIComponent(url)}`, { method: 'DELETE' }),
  deletePhotoById: (id: string, photoId: string) =>
    apiRequest<PhotoResponse>(`/api/inventory/${id}/photos/${photoId}`, { method: 'DELETE' }),
  setHeroPhoto: (id: string, url: string) =>
    apiRequest<PhotoResponse>(`/api/inventory/${id}/photos/hero?url=${encodeURIComponent(url)}`, { method: 'PATCH' }),

  listIssues: (id: string) => apiRequest<InventoryIssue[]>(`/api/inventory/${id}/issues`),
  createIssue: (id: string, body: CreateIssueRequest) =>
    apiRequest<InventoryIssue>(`/api/inventory/${id}/issues`, { method: 'POST', body }),
  updateIssue: (id: string, issueId: string, body: CreateIssueRequest) =>
    apiRequest<InventoryIssue>(`/api/inventory/${id}/issues/${issueId}`, { method: 'PUT', body }),
  deleteIssue: (id: string, issueId: string) =>
    apiRequest<void>(`/api/inventory/${id}/issues/${issueId}`, { method: 'DELETE' }),
};
