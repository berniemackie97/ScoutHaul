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
  attributesJson?: string | null;
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
