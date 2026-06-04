import { recognizeText } from '@infinitered/react-native-mlkit-text-recognition';
import { readAsStringAsync } from 'expo-file-system';
import { api } from '../../api';
import type { ProductIdentification, VisionIdentifyResult } from '../../api/marketplace';

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
  confidence: 'high' | 'medium' | 'low';
  searchQuery: string;
  alternateQueries: string[];
  visionLabels: string[];
  webEntities: { description: string; score: number }[];
  matchingPages: { url: string; title: string | null }[];
  bestGuessLabel: string | null;
};

// ── Main ──
// Gemini multimodal is the primary identifier — it reasons about the product and returns
// structured data (name, brand, model, value). Cloud Vision (if configured) adds web matches.
// On-device OCR is a last-resort offline fallback only.

export async function identifyFromPhoto(photoUri: string): Promise<IdentifyResult> {
  const vision = await callVisionApi(photoUri);

  if (vision) {
    return buildFromVision(vision);
  }

  // Offline / API unreachable — on-device OCR so the user still gets a starting query.
  return buildFromOcrOnly(photoUri);
}

// ── Build result from Gemini + Cloud Vision response ──

function buildFromVision(vision: VisionIdentifyResult): IdentifyResult {
  const product: ProductIdentification | null = vision.product;
  const matchingPages = vision.matchingPages ?? [];
  const barcode = vision.detectedText ? extractBarcode(vision.detectedText) : null;

  if (product) {
    // Gemini identified the product — this is the strong path.
    const searchQuery = vision.searchQuery
      ?? (product.brand && product.model ? `${product.brand} ${product.model}` : null)
      ?? product.name
      ?? product.searchKeywords[0]
      ?? '';

    // Alternate queries: Gemini's keyword phrases, minus the primary, deduped.
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
      confidence: product.confidence,
      searchQuery,
      alternateQueries,
      visionLabels: vision.labels ?? [],
      webEntities: vision.webEntities ?? [],
      matchingPages,
      bestGuessLabel: product.name ?? vision.bestGuessLabel,
    };
  }

  // No Gemini product (key not set, or rate-limited) — fall back to Cloud Vision web matches.
  const searchQuery = vision.searchQuery ?? vision.bestGuessLabel ?? '';
  const pageTitle = extractProductFromPageTitles(matchingPages);
  const alternateQueries = dedupeQueries(searchQuery, [
    ...(pageTitle ? [pageTitle] : []),
    ...(vision.bestGuessLabel ? [vision.bestGuessLabel] : []),
    ...vision.webEntities.slice(0, 2).map((e) => e.description),
  ]);

  const hasSignal = !!vision.bestGuessLabel || matchingPages.length > 0 || vision.webEntities.length > 0;

  return {
    title: vision.bestGuessLabel ?? pageTitle,
    brand: null,
    model: null,
    category: null,
    color: null,
    condition: null,
    barcode,
    estimatedResaleLow: null,
    estimatedResaleHigh: null,
    rawText: vision.detectedText ?? '',
    confidence: vision.bestGuessLabel ? 'medium' : hasSignal ? 'low' : 'low',
    searchQuery,
    alternateQueries,
    visionLabels: vision.labels ?? [],
    webEntities: vision.webEntities ?? [],
    matchingPages,
    bestGuessLabel: vision.bestGuessLabel,
  };
}

// ── Fallback: on-device OCR only (offline) ──

async function buildFromOcrOnly(photoUri: string): Promise<IdentifyResult> {
  let rawText = '';
  try {
    const result = await recognizeText(photoUri);
    rawText = result.text.trim();
  } catch {
    return emptyResult();
  }

  if (!rawText || rawText.length < 3) return emptyResult();

  // Use the most prominent text lines as a best-effort query.
  const query = topTextLines(rawText, 3);
  const barcode = extractBarcode(rawText);

  return {
    ...emptyResult(),
    barcode,
    rawText,
    confidence: query ? 'low' : 'low',
    searchQuery: query ?? '',
    alternateQueries: [],
    bestGuessLabel: query,
    title: query,
  };
}

// ── Vision API call ──

async function callVisionApi(photoUri: string): Promise<VisionIdentifyResult | null> {
  try {
    const base64 = await readAsStringAsync(photoUri, { encoding: 'base64' });
    if (!base64 || base64.length < 100) {
      console.warn('[Vision] Base64 too short, skipping:', base64?.length);
      return null;
    }
    console.log('[Vision] Sending image…', Math.round(base64.length / 1024), 'KB');
    const result = await api.visionIdentify(base64);
    console.log('[Vision] Result:', {
      product: result.product?.name ?? null,
      confidence: result.product?.confidence ?? 'n/a',
      brand: result.product?.brand ?? null,
      resale: result.product ? `$${result.product.estimatedResaleLow}-${result.product.estimatedResaleHigh}` : null,
      webPages: result.matchingPages?.length ?? 0,
      query: result.searchQuery,
    });
    return result;
  } catch (error) {
    const msg = error instanceof Error ? error.message : String(error);
    if (msg.includes('Sign in')) {
      console.error('[Vision] Auth required — user not signed in');
    } else {
      console.error('[Vision] call failed, falling back to OCR:', msg);
    }
    return null;
  }
}

// ── Helpers ──

const BARCODE_PATTERN = /\b(\d{8}|\d{12,14})\b/;

function extractBarcode(text: string): string | null {
  const clean = text.replace(/\s/g, '');
  const match = clean.match(BARCODE_PATTERN);
  if (!match) return null;
  const len = match[1].length;
  return (len === 8 || len === 12 || len === 13 || len === 14) ? match[1] : null;
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

// Pull a product name out of a marketplace page title (e.g. "Sony XM4 - eBay" → "Sony XM4").
function extractProductFromPageTitles(pages: { url: string; title: string | null }[]): string | null {
  const SITE_SUFFIX = /\s*[-–|·]\s*(eBay|Amazon|Walmart|Target|Best Buy|Mercari|Poshmark|Etsy|Wayfair|Google Shopping|Shopping|Search Results?).*$/i;
  const TOO_GENERIC = /^(shop|buy|sell|search|find|compare|results|home|page|\d+)/i;
  for (const page of pages) {
    if (!page.title) continue;
    const cleaned = page.title.replace(SITE_SUFFIX, '').replace(/\s{2,}/g, ' ').trim();
    if (cleaned.length >= 8 && cleaned.length <= 120 && !TOO_GENERIC.test(cleaned)) return cleaned;
  }
  return null;
}

function topTextLines(text: string, count: number): string | null {
  const lines = text
    .split('\n')
    .map((l) => l.trim())
    .filter((l) =>
      l.length > 3 && l.length < 60 &&
      !/^https?:\/\//i.test(l) &&
      !/^\$?\d+\.?\d*$/.test(l) &&
      !/^(WARNING|CAUTION|DIRECTIONS|INGREDIENTS|NET WT|MADE IN|©)/i.test(l),
    );
  return lines.length > 0 ? lines.slice(0, count).join(' ').slice(0, 120) : null;
}

function emptyResult(): IdentifyResult {
  return {
    title: null, brand: null, model: null, category: null, color: null, condition: null,
    barcode: null, estimatedResaleLow: null, estimatedResaleHigh: null,
    rawText: '', confidence: 'low', searchQuery: '', alternateQueries: [],
    visionLabels: [], webEntities: [], matchingPages: [], bestGuessLabel: null,
  };
}
