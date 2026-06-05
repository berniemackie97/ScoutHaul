import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';
import { api } from '../../api';
import type { ProductIdentification, ResaleComp, VisionIdentifyResult } from '../../api/marketplace';

export type IdentifyResult = {
  title: string | null;
  brand: string | null;
  model: string | null;
  category: string | null;
  color: string | null;
  condition: string | null;
  barcode: string | null;
  estimatedResaleLow: number | null;
  estimatedResaleHigh: number | null;
  rawText: string;
  // Identification confidence — ALWAYS from Gemini, never fabricated. null = not identified.
  confidence: 'high' | 'medium' | 'low' | null;
  valuationConfidence: 'high' | 'medium' | 'low' | null;
  priceRangeType: 'tight' | 'moderate' | 'wide' | null;
  keyValueFactors: string[];
  missingInformation: string[];
  recommendedNextSteps: string[];
  rationale: string | null;
  comps: ResaleComp[];
  searchQuery: string;
  alternateQueries: string[];
  bestGuessLabel: string | null;
};

// ── Main ──
// Gemini multimodal is the SOLE identifier — it reads text/logos and reasons about the product.
// Google Cloud Vision contributes ONLY reverse-image web-match links (never identity or confidence).
// There is no second identification system: if Gemini can't run, we do not fabricate a guess.

export async function identifyFromPhoto(photoUri: string): Promise<IdentifyResult> {
  const vision = await callVisionApi(photoUri);
  if (!vision) return emptyResult();
  return buildFromVision(vision);
}

// ── Build result — Gemini is the single source of truth ──

function buildFromVision(vision: VisionIdentifyResult): IdentifyResult {
  const product: ProductIdentification | null = vision.product;
  const barcode = vision.detectedText ? extractBarcode(vision.detectedText) : null;

  // No Gemini identification (key unset or rate-limited after retry). Do NOT invent an identity
  // or a confidence — return a clean unidentified result; the user refines manually.
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
  try {
    const base64 = await downscaleToBase64(photoUri);
    if (!base64 || base64.length < 100) {
      console.warn('[Vision] Base64 too short, skipping:', base64?.length);
      return null;
    }
    console.log('[Vision] Sending image…', Math.round(base64.length / 1024), 'KB');
    const result = await api.visionIdentify(base64);
    console.log('[Vision] Result:', {
      product: result.product?.name ?? null,
      idConfidence: result.product?.confidence ?? 'n/a',
      valConfidence: result.product?.valuationConfidence ?? 'n/a',
      rangeType: result.product?.priceRangeType ?? 'n/a',
      resale: result.product ? `$${result.product.estimatedResaleLow}-${result.product.estimatedResaleHigh}` : null,
      query: result.searchQuery,
    });
    return result;
  } catch (error) {
    const msg = error instanceof Error ? error.message : String(error);
    if (msg.includes('Sign in')) {
      console.error('[Vision] Auth required — user not signed in');
    } else {
      console.error('[Vision] call failed:', msg);
    }
    return null;
  }
}

// ── Helpers ──

// Phone cameras shoot 12–48MP. A raw full-res JPEG base64-encodes to several MB, which is slow to
// upload (especially over adb-reverse) and can exceed the API's 12MB request cap → the identify
// silently fails for EVERY photo while tiny barcode calls succeed. Downscale to 1280px wide + JPEG
// q0.6 first: payload drops to ~150–350KB, uploads fast, and Gemini still resolves detail clearly.
// On failure we return null (fail clean → user retakes) rather than sending the raw full-res file,
// which is the very payload that breaks the upload.
async function downscaleToBase64(uri: string): Promise<string | null> {
  try {
    const ref = await ImageManipulator.manipulate(uri).resize({ width: 1280 }).renderAsync();
    const out = await ref.saveAsync({ compress: 0.6, format: SaveFormat.JPEG, base64: true });
    return out.base64 ?? null;
  } catch (error) {
    console.warn('[Vision] Image resize failed:', error);
    return null;
  }
}

const BARCODE_PATTERN = /\b(\d{8}|\d{12,14})\b/;

function extractBarcode(text: string): string | null {
  const clean = text.replace(/\s/g, '');
  const match = clean.match(BARCODE_PATTERN);
  if (!match) return null;
  const len = match[1].length;
  return (len === 8 || len === 12 || len === 13 || len === 14) ? match[1] : null;
}

function normConfidence(v: string | null | undefined): 'high' | 'medium' | 'low' | null {
  return v === 'high' || v === 'medium' || v === 'low' ? v : null;
}

function normRangeType(v: string | null | undefined): 'tight' | 'moderate' | 'wide' | null {
  return v === 'tight' || v === 'moderate' || v === 'wide' ? v : null;
}

function dedupeQueries(primary: string, candidates: string[]): string[] {
  const seen = new Set<string>([primary.toLowerCase().trim()]);
  const out: string[] = [];
  for (const c of candidates) {
    const trimmed = c?.trim();
    if (!trimmed || trimmed.length < 3) continue;
    const key = trimmed.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(trimmed);
  }
  return out.slice(0, 4);
}

function emptyResult(): IdentifyResult {
  return {
    title: null, brand: null, model: null, category: null, color: null, condition: null,
    barcode: null, estimatedResaleLow: null, estimatedResaleHigh: null,
    rawText: '', confidence: null, valuationConfidence: null, priceRangeType: null,
    keyValueFactors: [], missingInformation: [], recommendedNextSteps: [], rationale: null, comps: [],
    searchQuery: '', alternateQueries: [], bestGuessLabel: null,
  };
}
