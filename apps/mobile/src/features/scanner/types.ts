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

export function buildSearchQuery(raw: string): string {
  const lines = raw
    .split('\n')
    .map((l) => l.trim())
    .filter((l) =>
      l.length > 2 &&
      !l.match(/^https?:\/\//i) &&
      !l.match(/^\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}$/) &&
      !l.match(/^\$?\d+\.?\d*$/) &&
      !l.match(/^[©®™]/) &&
      !l.match(/^(WARNING|CAUTION|DIRECTIONS|INGREDIENTS|NET WT)/i),
    );

  const scored = lines.map((line) => {
    let score = line.length;
    if (line.length > 8 && line.length < 80) score += 20;
    if (/[a-z]/.test(line) && /[A-Z]/.test(line)) score += 10;
    if (/^\d+$/.test(line)) score -= 50;
    if (/^[A-Z\s]{1,6}$/.test(line)) score -= 20;
    if (/model|series|edition/i.test(line)) score += 15;
    if (/\d{3,}/.test(line) && /[a-zA-Z]/.test(line)) score += 10;
    return { line, score };
  });

  scored.sort((a, b) => b.score - a.score);
  return scored.slice(0, 3).map((s) => s.line).join(' ').slice(0, 120).trim();
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

export function makeDoneFromResult(
  query: string,
  result: import('../../domain/types').MarketplaceLookupResult,
  photoUri?: string | null,
  webMatches: WebMatch[] = [],
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
    productTitle: result.productTitle ?? null,
    productBrand: result.productBrand ?? null,
    productImageUrl: result.productImageUrl ?? null,
    productCategory: result.productCategory ?? null,
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
