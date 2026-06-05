import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';
import { api } from '../../api';
import type { ProductIdentification, VisionIdentifyResult } from '../../api/marketplace';
import {
  type IdentifyResult,
  dedupeQueries,
  emptyResult,
  extractBarcode,
  normConfidence,
  normRangeType,
} from './identifyHelpers';

export type { IdentifyResult };

// Gemini multimodal is the SOLE identifier: it reads the photo and reasons about the product,
// returning a specific name/brand/model/category/condition, a conservative used-resale valuation,
// and comp price points. There is no second identification system — if Gemini can't run we return a
// clean "not identified" result and let the user refine manually (we never fabricate an identity).

// Tuning knobs, all in one place.
const TARGET_WIDTH = 1536;          // downscale width — good detail, ~400–700KB on the wire
const JPEG_QUALITY = 0.6;           // recompress quality after resize
const MIN_BASE64_LENGTH = 100;      // shorter than this means the encode produced nothing usable
const REQUEST_TIMEOUT_MS = 30_000;  // hard ceiling so a stalled backend can't hang the UI

const log = (...args: unknown[]) => { if (__DEV__) console.log('[Vision]', ...args); };

// ── Main ──

export async function identifyFromPhoto(photoUri: string): Promise<IdentifyResult> {
  const vision = await callVisionApi(photoUri);
  if (!vision) return emptyResult();
  return buildFromVision(vision);
}

// ── Build result — Gemini is the single source of truth ──

function buildFromVision(vision: VisionIdentifyResult): IdentifyResult {
  const product: ProductIdentification | null = vision.product;
  const barcode = vision.detectedText ? extractBarcode(vision.detectedText) : null;

  // No Gemini identification (key unset or rate-limited after retries). Don't invent an identity or
  // a confidence — return a clean unidentified result; the user refines manually.
  if (!product) {
    return {
      ...emptyResult(),
      barcode,
      rawText: vision.detectedText ?? '',
      searchQuery: vision.searchQuery ?? '',
    };
  }

  const searchQuery = vision.searchQuery
    ?? (product.brand && product.model ? `${product.brand} ${product.model}` : null)
    ?? product.name
    ?? product.searchKeywords[0]
    ?? '';

  const alternateQueries = dedupeQueries(searchQuery, [
    ...(product.brand && product.model ? [`${product.brand} ${product.model}`] : []),
    ...product.searchKeywords,
    ...(product.name ? [product.name] : []),
  ]);

  return {
    title: product.name,
    brand: product.brand,
    model: product.model,
    category: product.category,
    color: product.color,
    condition: product.condition,
    barcode,
    estimatedResaleLow: product.estimatedResaleLow,
    estimatedResaleHigh: product.estimatedResaleHigh,
    rawText: vision.detectedText ?? '',
    confidence: normConfidence(product.confidence),
    valuationConfidence: normConfidence(product.valuationConfidence),
    priceRangeType: normRangeType(product.priceRangeType),
    keyValueFactors: product.keyValueFactors ?? [],
    missingInformation: product.missingInformation ?? [],
    recommendedNextSteps: product.recommendedNextSteps ?? [],
    rationale: product.rationale ?? null,
    comps: product.comps ?? [],
    searchQuery,
    alternateQueries,
    bestGuessLabel: product.name,
  };
}

// ── Vision API call ──

async function callVisionApi(photoUri: string): Promise<VisionIdentifyResult | null> {
  const base64 = await downscaleToBase64(photoUri);
  if (!base64 || base64.length < MIN_BASE64_LENGTH) {
    log('encode produced too few bytes, skipping:', base64?.length ?? 0);
    return null;
  }

  // Hard timeout: a stalled network/backend must surface as a clean failure, never a frozen overlay.
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    log('sending image…', Math.round(base64.length / 1024), 'KB');
    const result = await api.visionIdentify(base64, controller.signal);
    log('result:', result.product?.name ?? '(not identified)', result.product?.confidence ?? '');
    return result;
  } catch (error) {
    const msg = error instanceof Error ? error.message : String(error);
    if (msg.includes('Sign in')) console.error('[Vision] auth required — user not signed in');
    else if (controller.signal.aborted) console.error('[Vision] timed out after', REQUEST_TIMEOUT_MS, 'ms');
    else console.error('[Vision] call failed:', msg);
    return null;
  } finally {
    clearTimeout(timeout);
  }
}

// ── Image prep ──

// Phone cameras shoot 12–48MP; a raw full-res JPEG base64-encodes to 10–25MB — slow to upload and
// historically over the API's request cap → silent "not identified". Downscale to TARGET_WIDTH +
// re-encode keeps every upload small and uniform. On failure return null (fail clean → user retakes)
// rather than sending the raw full-res file that breaks the upload.
async function downscaleToBase64(uri: string): Promise<string | null> {
  try {
    const ref = await ImageManipulator.manipulate(uri).resize({ width: TARGET_WIDTH }).renderAsync();
    const out = await ref.saveAsync({ compress: JPEG_QUALITY, format: SaveFormat.JPEG, base64: true });
    return out.base64 ?? null;
  } catch (error) {
    console.warn('[Vision] image resize failed:', error);
    return null;
  }
}
