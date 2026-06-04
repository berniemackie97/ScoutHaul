import type { MarketplaceListing } from '../../domain/types';

export type ScanMode = 'barcode' | 'photo-identify';

export type WebMatch = {
  url: string;
  title: string | null;
  source: string;
};

export type ScanState =
  | { phase: 'scanning' }
  | { phase: 'searching'; barcode: string; photoUri?: string | null; step?: string }
  | { phase: 'capturing' }
  | { phase: 'identifying'; photoUri: string; ocrText: string | null; step?: string }
  | { phase: 'photo-review'; photoUri: string; ocrText: string; query: string; identifyResult?: import('./photoIdentify').IdentifyResult }
  | ScanDoneState;

export type ScanDoneState = {
  phase: 'done';
  barcode: string;
  photoUri?: string | null;
  listings: MarketplaceListing[];
  avgPrice: number | null;
  lowPrice: number | null;
  highPrice: number | null;
  priceSource: 'market' | 'estimate' | null;
  priceRationale: string | null;
  totalResults: number;
  error: string | null;
  productTitle: string | null;
  productBrand: string | null;
  productModel: string | null;
  productColor: string | null;
  productCondition: string | null;
  productImageUrl: string | null;
  productCategory: string | null;
  productIdentityId: string | null;
  savedObservationCount: number;
  providersUsed: string[];
  providerWarnings: string[];
  webMatches: WebMatch[];
};

export function resolveProductName(state: ScanDoneState): string | null {
  if (state.productTitle) return state.productTitle;
  if (state.listings.length === 0) return null;
  const raw = state.listings[0].title;
  return raw
    .replace(/\s*-\s*(New|Used|Open Box|Sealed|Free Shipping|Fast Ship|US Seller|Brand New).*/i, '')
    .replace(/\s*\(.*\)\s*$/, '')
    .trim() || raw;
}

export function resolveProductImage(state: ScanDoneState): string | null {
  return state.productImageUrl ?? state.photoUri ?? state.listings[0]?.imageUrl ?? null;
}

export function makeDoneError(query: string, error: unknown, webMatches: WebMatch[] = []): ScanDoneState {
  return {
    phase: 'done',
    barcode: query,
    listings: [],
    avgPrice: null,
    lowPrice: null,
    highPrice: null,
    priceSource: null,
    priceRationale: null,
    totalResults: 0,
    error: error instanceof Error ? error.message : 'Search failed',
    productTitle: null,
    productBrand: null,
    productModel: null,
    productColor: null,
    productCondition: null,
    productImageUrl: null,
    productCategory: null,
    productIdentityId: null,
    savedObservationCount: 0,
    providersUsed: [],
    providerWarnings: [],
    webMatches,
  };
}

export type VisionOverrides = {
  brand?: string | null;
  model?: string | null;
  category?: string | null;
  color?: string | null;
  condition?: string | null;
  title?: string | null;
  estimatedResaleLow?: number | null;
  estimatedResaleHigh?: number | null;
};

export function makeDoneFromResult(
  query: string,
  result: import('../../domain/types').MarketplaceLookupResult,
  photoUri?: string | null,
  webMatches: WebMatch[] = [],
  v?: VisionOverrides,
): ScanDoneState {
  // Real marketplace data wins. Otherwise fall back to an AI resale estimate so the user always
  // sees a value. Estimate source priority: server (barcode/keyword) → vision override (photo).
  const hasMarket = (result.averageSoldPrice ?? null) != null;
  const estLow = result.estimatedResaleLow ?? v?.estimatedResaleLow ?? null;
  const estHigh = result.estimatedResaleHigh ?? v?.estimatedResaleHigh ?? null;
  const hasEstimate = estLow != null || estHigh != null;
  const estMid = hasEstimate ? Math.round((((estLow ?? estHigh)! + (estHigh ?? estLow)!) / 2)) : null;

  const avgPrice = hasMarket ? result.averageSoldPrice! : estMid;
  const lowPrice = hasMarket ? (result.lowestPrice ?? null) : estLow;
  const highPrice = hasMarket ? (result.highestPrice ?? null) : estHigh;
  const priceSource: ScanDoneState['priceSource'] = hasMarket ? 'market' : hasEstimate ? 'estimate' : null;
  const priceRationale = !hasMarket && hasEstimate ? (result.estimateRationale ?? null) : null;

  return {
    phase: 'done',
    barcode: query,
    photoUri,
    listings: result.listings,
    avgPrice,
    lowPrice,
    highPrice,
    priceRationale,
    priceSource,
    totalResults: result.totalResults,
    error: null,
    productTitle: result.productTitle ?? v?.title ?? null,
    productBrand: result.productBrand ?? v?.brand ?? null,
    productModel: v?.model ?? null,
    productColor: v?.color ?? null,
    productCondition: v?.condition ?? null,
    productImageUrl: result.productImageUrl ?? null,
    productCategory: result.productCategory ?? v?.category ?? null,
    productIdentityId: result.productIdentityId ?? null,
    savedObservationCount: result.savedObservationCount ?? 0,
    providersUsed: result.providersUsed ?? [],
    providerWarnings: result.providerWarnings ?? [],
    webMatches,
  };
}

export function extractWebMatches(
  pages: { url: string; title: string | null }[],
): WebMatch[] {
  return pages
    .filter((p) => p.url && !p.url.includes('google.com/imgres'))
    .map((p) => {
      let source: string;
      try {
        const host = new URL(p.url).hostname.replace(/^www\./, '');
        source = host;
      } catch {
        source = 'Web';
      }
      return { url: p.url, title: p.title, source };
    })
    .slice(0, 8);
}
