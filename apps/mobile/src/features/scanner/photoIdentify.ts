import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';
import { api } from '../../api';
import { ApiError } from '../../api/client';
import type { ProductIdentification, VisionIdentifyResult } from '../../api/marketplace';
import {
  type IdentifyResult,
  type VisionErrorKind,
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
const TARGET_WIDTH = 1536; // downscale width — good detail, ~400–700KB on the wire
const JPEG_QUALITY = 0.6; // recompress quality after resize
const MIN_BASE64_LENGTH = 100; // shorter than this means the encode produced nothing usable
const REQUEST_TIMEOUT_MS = 30_000; // hard ceiling so a stalled backend can't hang the UI

const log = (...args: unknown[]) => {
  if (__DEV__) console.log('[Vision]', ...args);
};

// ── Main ──

// `context` = optional known facts (brand/condition/size/notes the user already entered) fed to the
// model to sharpen identification + valuation.
export async function identifyFromPhoto(
  photoUri: string,
  context?: string | null
): Promise<IdentifyResult> {
  const base64 = await downscaleToBase64(photoUri);
  if (!base64 || base64.length < MIN_BASE64_LENGTH) {
    log('encode produced too few bytes, skipping:', base64?.length ?? 0);
    return { ...emptyResult(), errorKind: 'error' };
  }

  const { vision, errorKind } = await callVisionApi(base64, context);
  if (!vision) return { ...emptyResult(), errorKind };
  return buildFromVision(vision);
}

// Text-only path: same Gemini identifier/appraiser, no camera. `details` should include whatever
// the user knows — title, brand, model, condition, size, edition, defects, packaging, etc.
export async function identifyFromDetails(details: string): Promise<IdentifyResult> {
  const facts = details.trim();
  if (!facts) return { ...emptyResult(), errorKind: null };

  const { vision, errorKind } = await callVisionApi(null, facts);
  if (!vision) return { ...emptyResult(), errorKind };
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

  const searchQuery =
    vision.searchQuery ??
    (product.brand && product.model ? `${product.brand} ${product.model}` : null) ??
    product.name ??
    product.searchKeywords[0] ??
    '';

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
    conditionQuality: product.conditionQuality ?? null,
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
    errorKind: null,
  };
}

// ── Vision API call ──

type VisionCall = { vision: VisionIdentifyResult | null; errorKind: VisionErrorKind | null };

async function callVisionApi(
  imageBase64: string | null,
  context?: string | null
): Promise<VisionCall> {
  // Hard timeout: a stalled network/backend must surface as a clean failure, never a frozen overlay.
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    log(
      imageBase64 ? 'sending image…' : 'sending typed details…',
      imageBase64 ? Math.round(imageBase64.length / 1024) : 0,
      'KB'
    );
    const result = imageBase64
      ? await api.visionIdentify(imageBase64, controller.signal, context)
      : await api.visionIdentifyText(context ?? '', controller.signal);
    log('result:', result.product?.name ?? '(not identified)', result.product?.confidence ?? '');
    return { vision: result, errorKind: null };
  } catch (error) {
    const errorKind = classifyVisionError(error, controller.signal.aborted);
    console.error(
      '[Vision] call failed:',
      errorKind,
      error instanceof Error ? error.message : String(error)
    );
    return { vision: null, errorKind };
  } finally {
    clearTimeout(timeout);
  }
}

// Map a thrown error to a stable kind the UI turns into a specific message + CTA.
function classifyVisionError(error: unknown, aborted: boolean): VisionErrorKind {
  if (aborted) return 'timeout';
  if (error instanceof ApiError) {
    if (error.status === 429) return error.code === 'quota_exhausted' ? 'quota' : 'rate_limited';
    if (error.status === 503 || error.status === 502) return 'unavailable';
    return 'error';
  }
  const msg = error instanceof Error ? error.message : String(error);
  if (msg.includes('Sign in')) return 'auth';
  // RN fetch network failure surfaces as a TypeError ("Network request failed").
  if (error instanceof TypeError || /network/i.test(msg)) return 'offline';
  return 'error';
}

// ── Image prep ──

// Phone cameras shoot 12–48MP; a raw full-res JPEG base64-encodes to 10–25MB — slow to upload and
// historically over the API's request cap → silent "not identified". Downscale to TARGET_WIDTH +
// re-encode keeps every upload small and uniform. On failure return null (fail clean → user retakes)
// rather than sending the raw full-res file that breaks the upload.
async function downscaleToBase64(uri: string): Promise<string | null> {
  try {
    const ref = await ImageManipulator.manipulate(uri)
      .resize({ width: TARGET_WIDTH })
      .renderAsync();
    const out = await ref.saveAsync({
      compress: JPEG_QUALITY,
      format: SaveFormat.JPEG,
      base64: true,
    });
    return out.base64 ?? null;
  } catch (error) {
    console.warn('[Vision] image resize failed:', error);
    return null;
  }
}
