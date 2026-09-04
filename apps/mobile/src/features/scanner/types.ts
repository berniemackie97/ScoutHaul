import type { MarketplaceListing, MarketplaceLookupResult, ResaleComp, ResaleSource } from '../../domain/types';
import type { IdentifyResult, VisionErrorKind } from './identifyHelpers';

export type ScanMode = 'barcode' | 'photo-identify';

export type ScanState =
  | { phase: 'scanning' }
  | { phase: 'searching'; barcode: string; photoUri?: string | null; step?: string }
  | { phase: 'capturing' }
  | { phase: 'identifying'; photoUri: string; step?: string }
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
  // Gemini appraisal signals (identification vs valuation kept separate).
  idConfidence: 'high' | 'medium' | 'low' | null;
  valuationConfidence: 'high' | 'medium' | 'low' | null;
  priceRangeType: 'tight' | 'moderate' | 'wide' | null;
  keyValueFactors: string[];
  missingInformation: string[];
  recommendedNextSteps: string[];
  comps: ResaleComp[];
  sources: ResaleSource[];
  // Why identify failed (quota/limit/outage/timeout) — lets results show the real cause instead of a
  // misleading "no results". null = ran fine (identified or a genuine no-match).
  identifyError: VisionErrorKind | null;
  productConditionQuality: string | null;
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

export function makeDoneError(query: string, error: unknown): ScanDoneState {
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
    idConfidence: null,
    valuationConfidence: null,
    priceRangeType: null,
    keyValueFactors: [],
    missingInformation: [],
    recommendedNextSteps: [],
    comps: [],
    sources: [],
    identifyError: null,
    productConditionQuality: null,
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
  idConfidence?: 'high' | 'medium' | 'low' | null;
  valuationConfidence?: 'high' | 'medium' | 'low' | null;
  priceRangeType?: 'tight' | 'moderate' | 'wide' | null;
  keyValueFactors?: string[];
  missingInformation?: string[];
  recommendedNextSteps?: string[];
  rationale?: string | null;
  comps?: ResaleComp[];
  errorKind?: VisionErrorKind | null;
  conditionQuality?: string | null;
};

export function makeDoneFromResult(
  query: string,
  result: MarketplaceLookupResult,
  photoUri?: string | null,
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
  // The server estimate is now GROUNDED (real Google-Search comps), so it wins over the photo
  // vision override (training-knowledge) for everything valuation-related.
  const priceRationale = !hasMarket && hasEstimate ? (result.estimateRationale ?? v?.rationale ?? null) : null;

  const norm = <T extends string>(x: unknown, allowed: readonly T[]): T | null =>
    typeof x === 'string' && (allowed as readonly string[]).includes(x) ? (x as T) : null;
  const confSet = ['high', 'medium', 'low'] as const;
  const rangeSet = ['tight', 'moderate', 'wide'] as const;

  const valuationConfidence = !hasMarket
    ? (norm(result.estimateValuationConfidence, confSet) ?? v?.valuationConfidence ?? null)
    : null;
  const priceRangeType = !hasMarket
    ? (norm(result.estimatePriceRangeType, rangeSet) ?? v?.priceRangeType ?? null)
    : null;

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
    idConfidence: v?.idConfidence ?? null,
    valuationConfidence,
    priceRangeType,
    keyValueFactors: v?.keyValueFactors ?? [],
    missingInformation: v?.missingInformation ?? [],
    recommendedNextSteps: v?.recommendedNextSteps ?? [],
    // Comps: prefer grounded server comps (real listings) over the vision override; none when
    // live marketplace listings already populate the listings array.
    comps: hasMarket ? [] : (result.estimateComps ?? v?.comps ?? []),
    sources: result.estimateSources ?? [],
    identifyError: v?.errorKind ?? null,
    productConditionQuality: v?.conditionQuality ?? null,
  };
}

/**
 * A lookup that found nothing, without having asked.
 *
 * Used when no marketplace provider is configured, and when the lookup call itself fails: the screen
 * still has the vision result to show, and an empty lookup is the honest shape for "we did not get
 * marketplace prices" — not an error the shopper caused.
 */
export function emptyLookupResult(query: string): MarketplaceLookupResult {
  return {
    query, listings: [], totalResults: 0,
    averageSoldPrice: null, lowestPrice: null, highestPrice: null,
    productTitle: null, productBrand: null, productImageUrl: null, productCategory: null,
    productIdentityId: null, savedObservationCount: 0, providersUsed: [], providerWarnings: [],
  };
}

/**
 * Whether a lookup answered well enough to stop trying other phrasings.
 *
 * Listings or a product name mean it worked. No providers used means nobody was asked, so retrying a
 * different phrasing would ask nobody again — retrying then just delays the result for no reason.
 */
export function isConclusiveLookup(result: MarketplaceLookupResult): boolean {
  return result.totalResults > 0
    || Boolean(result.productTitle)
    || !result.providersUsed?.length;
}

/** Flattens a vision identification into the overrides the results screen layers over a lookup. */
export function toVisionOverrides(result: IdentifyResult): VisionOverrides {
  return {
    brand: result.brand,
    model: result.model,
    category: result.category,
    color: result.color,
    condition: result.condition,
    title: result.title,
    estimatedResaleLow: result.estimatedResaleLow,
    estimatedResaleHigh: result.estimatedResaleHigh,
    idConfidence: result.confidence,
    valuationConfidence: result.valuationConfidence,
    priceRangeType: result.priceRangeType,
    keyValueFactors: result.keyValueFactors,
    missingInformation: result.missingInformation,
    recommendedNextSteps: result.recommendedNextSteps,
    rationale: result.rationale,
    comps: result.comps,
    errorKind: result.errorKind,
    conditionQuality: result.conditionQuality,
  };
}
