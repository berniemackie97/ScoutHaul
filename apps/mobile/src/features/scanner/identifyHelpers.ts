// Pure, IO-free helpers + the result shape for photo identification. No expo/native/api imports
// (only erased type imports), so this module is trivially unit-testable and side-effect-free.
import type { ResaleComp } from '../../api/marketplace';

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

const VALID_BARCODE_LENGTHS = new Set([8, 12, 13, 14]); // EAN-8, UPC-A, EAN-13, ITF-14

// Gemini occasionally surfaces a printed UPC/EAN in the item's visible text — capture it if so.
// Matches digit runs that may contain internal spaces (e.g. "0 3 6 0 0 0...") without gluing onto
// adjacent words, then keeps the first run whose digit count is a real barcode length.
export function extractBarcode(text: string): string | null {
  const runs = text.match(/\d[\d ]*\d/g) ?? [];
  for (const run of runs) {
    const digits = run.replace(/ /g, '');
    if (VALID_BARCODE_LENGTHS.has(digits.length)) return digits;
  }
  return null;
}

export function normConfidence(v: string | null | undefined): 'high' | 'medium' | 'low' | null {
  return v === 'high' || v === 'medium' || v === 'low' ? v : null;
}

export function normRangeType(v: string | null | undefined): 'tight' | 'moderate' | 'wide' | null {
  return v === 'tight' || v === 'moderate' || v === 'wide' ? v : null;
}

// Build the ordered fallback query list for auto-search, de-duped against the primary, min 3 chars.
export function dedupeQueries(primary: string, candidates: string[]): string[] {
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

// A clean "not identified" result — never fabricates an identity or confidence.
export function emptyResult(): IdentifyResult {
  return {
    title: null, brand: null, model: null, category: null, color: null, condition: null,
    barcode: null, estimatedResaleLow: null, estimatedResaleHigh: null,
    rawText: '', confidence: null, valuationConfidence: null, priceRangeType: null,
    keyValueFactors: [], missingInformation: [], recommendedNextSteps: [], rationale: null, comps: [],
    searchQuery: '', alternateQueries: [], bestGuessLabel: null,
  };
}
