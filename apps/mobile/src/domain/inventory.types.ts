export type InventoryStatus =
  | 'Draft'
  | 'Researching'
  | 'Watchlist'
  | 'Purchased'
  | 'NeedsCleaning'
  | 'NeedsTesting'
  | 'NeedsRepair'
  | 'NeedsPhotos'
  | 'ReadyToList'
  | 'Bundled'
  | 'Listed'
  | 'Sold'
  | 'Packed'
  | 'Shipped'
  | 'Delivered'
  | 'Returned'
  | 'Donated'
  | 'Disposed';

export type InventoryOwnershipType = 'Owned' | 'Consigned' | 'CustomerRepair' | 'VendorManaged';
export type InventoryFunctionalStatus =
  | 'Unknown'
  | 'Untested'
  | 'FullyFunctional'
  | 'FunctionalWithIssues'
  | 'Intermittent'
  | 'NonFunctional'
  | 'PartsOnly';
export type InventoryCompletenessStatus = 'Unknown' | 'Complete' | 'MissingAccessories' | 'MissingParts' | 'Incomplete';
export type InventoryAuthenticityStatus =
  | 'NotAssessed'
  | 'Authentic'
  | 'LikelyAuthentic'
  | 'NeedsVerification'
  | 'Replica'
  | 'Counterfeit';
export type InventoryPackagingStatus =
  | 'NotApplicable'
  | 'FactorySealed'
  | 'OriginalExcellent'
  | 'OriginalWorn'
  | 'OriginalDamaged'
  | 'ReplacementPackaging'
  | 'LooseNoPackaging';
export type DimensionUnit = 'Inches' | 'Centimeters';
export type WeightUnit = 'Ounces' | 'Pounds' | 'Grams' | 'Kilograms';

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
  isBundle?: boolean;
  bundleComponentCount?: number;
  acquisitionLotId?: string | null;
  acquisitionLotName?: string | null;
  acquisitionAllocatedCost?: number | null;
  lifecycleGroup?: string | null;
  isSold?: boolean;
  ownershipType?: InventoryOwnershipType;
  functionalStatus?: InventoryFunctionalStatus;
  completenessStatus?: InventoryCompletenessStatus;
  authenticityStatus?: InventoryAuthenticityStatus;
  packagingStatus?: InventoryPackagingStatus;
  manufacturerPartNumber?: string | null;
  serialNumber?: string | null;
  countryOfOrigin?: string | null;
  manufactureYear?: number | null;
  length?: number | null;
  width?: number | null;
  height?: number | null;
  dimensionUnit?: DimensionUnit;
  weight?: number | null;
  weightUnit?: WeightUnit;
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
