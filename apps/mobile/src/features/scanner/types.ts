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
  totalResults: number;
  error: string | null;
  productTitle: string | null;
  productBrand: string | null;
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
    totalResults: 0,
    error: error instanceof Error ? error.message : 'Search failed',
    productTitle: null,
    productBrand: null,
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
  category?: string | null;
  title?: string | null;
};

export function makeDoneFromResult(
  query: string,
  result: import('../../domain/types').MarketplaceLookupResult,
  photoUri?: string | null,
  webMatches: WebMatch[] = [],
  visionOverrides?: VisionOverrides,
): ScanDoneState {
  return {
    phase: 'done',
    barcode: query,
    photoUri,
    listings: result.listings,
    avgPrice: result.averageSoldPrice ?? null,
    lowPrice: result.lowestPrice ?? null,
    highPrice: result.highestPrice ?? null,
    totalResults: result.totalResults,
    error: null,
    productTitle: result.productTitle ?? visionOverrides?.title ?? null,
    productBrand: result.productBrand ?? visionOverrides?.brand ?? null,
    productImageUrl: result.productImageUrl ?? null,
    productCategory: result.productCategory ?? visionOverrides?.category ?? null,
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
