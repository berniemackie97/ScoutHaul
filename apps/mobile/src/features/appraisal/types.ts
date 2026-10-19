// Mirrors HaulScout.Application/Appraisal/AppraisalContracts.cs.

export type IdentificationCertainty = 'High' | 'Medium' | 'Low';
export type IdentificationSource = 'Catalog' | 'Barcode' | 'Photo' | 'Details';
export type CompKind = 'YourSale' | 'Sold' | 'Asking' | 'Guide';
export type PriceBasis = 'Sales' | 'Guide' | 'Asking' | 'Estimate' | 'None';
export type ConfidenceLevel = 'None' | 'Guess' | 'Thin' | 'Fair' | 'Strong';
export type CompSourceStatus = 'Found' | 'Empty' | 'Failed' | 'Off';

export type Estimate = {
  low: number;
  high: number;
  confidence: string;
  rationale: string | null;
  isWide: boolean;
};

export type Identification = {
  title: string;
  brand: string | null;
  model: string | null;
  category: string | null;
  color: string | null;
  condition: string | null;
  conditionGrade: string | null;
  barcode: string | null;
  identifiers: string[];
  certainty: IdentificationCertainty;
  source: IdentificationSource;
  productIdentityId: string | null;
  imageUrl: string | null;
  missingInformation: string[];
  estimate: Estimate | null;
};

export type AppraiseRequest = {
  title: string;
  brand: string | null;
  model: string | null;
  category: string | null;
  color: string | null;
  condition: string | null;
  barcode: string | null;
  identifiers: string[];
  certainty: IdentificationCertainty;
  productIdentityId: string | null;
  estimate: Estimate | null;
};

export type Price = {
  basis: PriceBasis;
  quickSale: number | null;
  list: number | null;
  high: number | null;
  confidence: number;
  level: ConfidenceLevel;
  reasons: string[];
};

export type Comp = {
  kind: CompKind;
  source: string;
  title: string;
  price: number;
  shipping: number | null;
  condition: string | null;
  url: string | null;
  date: string | null;
  verified: boolean;
  included: boolean;
  excludedBecause: string | null;
};

export type CompSourceReport = {
  name: string;
  status: CompSourceStatus;
  count: number;
  message: string | null;
};

export type CompSearchLink = {
  label: string;
  kind: CompKind;
  url: string;
};

export type WebSource = {
  url: string;
  title: string | null;
};

export type Appraisal = {
  searchText: string;
  price: Price;
  comps: Comp[];
  estimate: Estimate | null;
  sources: CompSourceReport[];
  searchLinks: CompSearchLink[];
  webSources: WebSource[];
  appraisedAt: string;
};
