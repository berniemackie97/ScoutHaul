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
  /** An item in stock: the appraisal is kept in its history. */
  inventoryItemId: string | null;
};

export type AppraisalSnapshot = {
  id: string;
  appraisedAt: string;
  list: number | null;
  basis: PriceBasis;
  confidence: number;
  level: ConfidenceLevel;
  saleComps: number;
  askingComps: number;
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

// Mirrors HaulScout.Application/Appraisal/BuyCheck.cs.

export type BuyDecision = 'Buy' | 'Negotiate' | 'Pass' | 'ResearchRequired' | 'Unavailable';

export type BuyCheckRequest = {
  expectedSalePrice: number;
  askingPrice: number | null;
  channel: string | null;
  shippingCost: number | null;
  suppliesCost: number | null;
  minimumProfit: number;
  targetMarginPercent: number | null;
  confidence: number | null;
  basis: PriceBasis | null;
};

export type BuyChannel = {
  channel: string;
  label: string;
  fees: number;
  feesFrom: string;
  shipping: number;
  shippingFrom: string;
  supplies: number;
  netProceeds: number;
  maxBuyPrice: number;
};

export type BuyCheck = {
  expectedSalePrice: number;
  askingPrice: number | null;
  channel: BuyChannel;
  channels: BuyChannel[];
  maxBuyPrice: number;
  profitAtAsking: number | null;
  marginAtAsking: number | null;
  decision: BuyDecision;
  reasons: string[];
};
