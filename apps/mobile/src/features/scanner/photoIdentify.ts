import { recognizeText } from '@infinitered/react-native-mlkit-text-recognition';
import { readAsStringAsync } from 'expo-file-system';
import { api } from '../../api';
import type { VisionIdentifyResult } from '../../api/marketplace';

export type IdentifyResult = {
  title: string | null;
  brand: string | null;
  model: string | null;
  category: string | null;
  barcode: string | null;
  rawText: string;
  confidence: 'high' | 'medium' | 'low';
  searchQuery: string;
  alternateQueries: string[];
  visionLabels: string[];
  webEntities: { description: string; score: number }[];
  matchingPages: { url: string; title: string | null }[];
  bestGuessLabel: string | null;
};

// ── Known brands ──

const KNOWN_BRANDS = [
  'Nike', 'Adidas', 'Puma', 'New Balance', 'Reebok', 'Converse', 'Vans',
  'Sony', 'Samsung', 'Apple', 'LG', 'Panasonic', 'Bose', 'JBL', 'Canon', 'Nikon',
  'Nintendo', 'PlayStation', 'Xbox', 'Sega',
  'Lego', 'Hasbro', 'Mattel', 'Fisher-Price', 'Hot Wheels', 'Nerf',
  'KitchenAid', 'Cuisinart', 'Instant Pot', 'Ninja', 'Keurig', 'Breville',
  'DeWalt', 'Milwaukee', 'Makita', 'Ryobi', 'Bosch', 'Black & Decker',
  'Coach', 'Michael Kors', 'Kate Spade', 'Dooney & Bourke', 'Fossil',
  'North Face', 'Patagonia', 'Columbia', 'Under Armour',
  'Ralph Lauren', 'Tommy Hilfiger', 'Calvin Klein', 'Levi\'s',
  'Pyrex', 'Corning', 'Fiesta', 'Le Creuset', 'Lodge',
  'Dyson', 'iRobot', 'Shark', 'Bissell',
  'Herman Miller', 'Steelcase',
  'Vitamix', 'Blendtec',
  'Garmin', 'Fitbit',
  'Funko', 'Hallmark',
  'Cricut', 'Brother',
];

const BRAND_PATTERN = new RegExp(`\\b(${KNOWN_BRANDS.map(escapeRegex).join('|')})\\b`, 'i');

const LABEL_CATEGORY_MAP: Record<string, string> = {
  'Laptop': 'Tech', 'Computer': 'Tech', 'Tablet': 'Tech', 'Phone': 'Tech',
  'Electronic device': 'Electronics', 'Audio equipment': 'Electronics', 'Camera': 'Electronics',
  'Clothing': 'Clothing', 'Shirt': 'Clothing', 'T-shirt': 'Clothing', 'Dress': 'Clothing',
  'Shoe': 'Shoes', 'Footwear': 'Shoes', 'Sneakers': 'Shoes', 'Boot': 'Shoes',
  'Toy': 'Toys', 'Action figure': 'Collectibles', 'Doll': 'Toys',
  'Furniture': 'Furniture', 'Chair': 'Furniture', 'Table': 'Furniture',
  'Cookware': 'Home', 'Kitchen utensil': 'Home', 'Tableware': 'Home',
  'Tool': 'Tools', 'Power tool': 'Tools', 'Drill': 'Tools',
  'Small appliance': 'Appliances', 'Blender': 'Appliances', 'Vacuum cleaner': 'Appliances',
  'Book': 'Books', 'Video game': 'Video Games',
};

const MODEL_PATTERNS = [
  /\b([A-Z]{1,4}[-\s]?\d{2,6}[A-Z]{0,2})\b/,
  /\bModel[:\s#]+(\S+)/i,
  /\bPart[:\s#]+(\S+)/i,
  /\bStyle[:\s#]+(\S+)/i,
];

const BARCODE_PATTERN = /\b(\d{8}|\d{12,14})\b/;

// ── Main ──
// Cloud Vision is primary — gives product ID, text, logos, labels in one call.
// On-device OCR is fallback only when Vision API is unavailable.

export async function identifyFromPhoto(photoUri: string): Promise<IdentifyResult> {
  // Try Cloud Vision first (one call gets everything)
  const vision = await callCloudVision(photoUri);

  if (vision) {
    return buildFromVision(vision);
  }

  // Fallback: on-device OCR only (offline, free)
  return buildFromOcrOnly(photoUri);
}

// ── Build result from Cloud Vision response ──

function buildFromVision(vision: VisionIdentifyResult): IdentifyResult {
  const rawText = vision.detectedText ?? '';

  // Brand: logos > web entities > matching page titles > OCR text
  const logoBrand = vision.detectedLogos.length > 0 ? vision.detectedLogos[0] : null;
  const entityBrand = extractBrandFromEntities(vision.webEntities, vision.bestGuessLabel);
  const pageBrand = extractBrandFromPageTitles(vision.matchingPages);
  const textBrand = rawText ? extractBrandFromText(rawText) : null;
  const brand = logoBrand ?? entityBrand ?? pageBrand ?? textBrand;

  // Model: from OCR text (Cloud Vision text detection)
  const model = rawText ? extractModel(rawText) : null;

  // Category: from labels
  const category = inferCategoryFromLabels(vision.labels);

  // Barcode: from OCR text
  const barcode = rawText ? extractBarcode(rawText) : null;

  // Title: bestGuessLabel > matching page product name > entity fallback
  const pageProductName = extractProductFromPageTitles(vision.matchingPages);
  const title = vision.bestGuessLabel ?? pageProductName ?? buildTitleFallback(brand, model, vision.webEntities);

  // Primary search query — bestGuessLabel is strongest signal
  const searchQuery = vision.searchQuery ?? title ?? '';

  // Alternate queries — ranked strategies for retry when primary returns no results
  const alternateQueries = buildAlternateQueries(
    searchQuery, brand, model, category,
    vision.webEntities, vision.matchingPages, vision.bestGuessLabel,
  );

  // Confidence — factor in matching pages (Google already found this product somewhere)
  const hasVisionId = !!vision.bestGuessLabel;
  const hasEntities = vision.webEntities.length > 0;
  const hasPages = vision.matchingPages.length > 0;
  const confidence: IdentifyResult['confidence'] =
    hasVisionId ? 'high'
    : (hasEntities && hasPages) ? 'high'
    : hasEntities ? 'medium'
    : hasPages ? 'medium'
    : 'low';

  return {
    title,
    brand,
    model,
    category,
    barcode,
    rawText,
    confidence,
    searchQuery,
    alternateQueries,
    visionLabels: vision.labels,
    webEntities: vision.webEntities,
    matchingPages: vision.matchingPages,
    bestGuessLabel: vision.bestGuessLabel,
  };
}

// ── Alternate query generation ──
// When primary query returns no marketplace results, try these in order.

function buildAlternateQueries(
  primary: string,
  brand: string | null,
  model: string | null,
  category: string | null,
  entities: { description: string; score: number }[],
  pages: { url: string; title: string | null }[],
  bestGuess: string | null,
): string[] {
  const seen = new Set<string>();
  const alts: string[] = [];
  const norm = (s: string) => s.toLowerCase().trim();

  const add = (q: string | null | undefined) => {
    if (!q) return;
    const trimmed = q.trim();
    if (trimmed.length < 3) return;
    const key = norm(trimmed);
    if (seen.has(key) || key === norm(primary)) return;
    seen.add(key);
    alts.push(trimmed);
  };

  // Strategy 1: brand + model (very specific)
  if (brand && model) add(`${brand} ${model}`);

  // Strategy 2: product name from matching page titles
  const pageProduct = extractProductFromPageTitles(pages);
  add(pageProduct);

  // Strategy 3: bestGuessLabel alone (if primary was built differently)
  add(bestGuess);

  // Strategy 4: top web entities by score (these are Google's product guesses)
  const topEntities = entities
    .filter((e) => e.description.length > 3 && e.score > 0.3)
    .sort((a, b) => b.score - a.score);
  if (topEntities.length >= 2) {
    add(`${topEntities[0].description} ${topEntities[1].description}`);
  }
  if (topEntities.length >= 1) {
    add(topEntities[0].description);
  }

  // Strategy 5: brand + category (broadest useful search)
  if (brand && category) add(`${brand} ${category}`);

  // Strategy 6: brand alone
  if (brand) add(brand);

  return alts.slice(0, 4);
}

function extractProductFromPageTitles(
  pages: { url: string; title: string | null }[],
): string | null {
  // Matching page titles from marketplace sites often contain the actual product name.
  // E.g. "Sony WH-1000XM5 Wireless Headphones - eBay" → "Sony WH-1000XM5 Wireless Headphones"
  const SITE_SUFFIXES = /\s*[-–|·]\s*(eBay|Amazon|Walmart|Target|Best Buy|Mercari|Poshmark|Etsy|Wayfair|Home Depot|Lowe's|AliExpress|Wish|StockX|GOAT|Facebook Marketplace|Craigslist|Offerup|Depop|Grailed|Nordstrom|Macy's|Newegg|B&H|Costco|Sam's Club|Google Shopping|Shopping|Search Results?|Price Comparison).*$/i;
  const JUNK = /\s*[-–|]\s*(New|Used|Pre-?Owned|Refurbished|Like New|Good|Fair|Poor|Open Box|Sealed|Free Shipping|Fast Ship).*$/i;
  const TOO_GENERIC = /^(shop|buy|sell|search|find|compare|results|home|page|\d+)/i;

  for (const page of pages) {
    if (!page.title) continue;
    let cleaned = page.title
      .replace(SITE_SUFFIXES, '')
      .replace(JUNK, '')
      .replace(/\s*\((\d+|[^)]{0,15})\)\s*$/, '')
      .replace(/\s*\[\d+\]\s*$/, '')
      .replace(/\s{2,}/g, ' ')
      .trim();

    if (cleaned.length >= 8 && cleaned.length <= 120 && !TOO_GENERIC.test(cleaned)) {
      return cleaned;
    }
  }
  return null;
}

function extractBrandFromPageTitles(
  pages: { url: string; title: string | null }[],
): string | null {
  for (const page of pages) {
    if (!page.title) continue;
    const match = page.title.match(BRAND_PATTERN);
    if (match) return normalizeCase(match[1]);
  }
  return null;
}

// ── Fallback: on-device OCR only ──

async function buildFromOcrOnly(photoUri: string): Promise<IdentifyResult> {
  let rawText = '';
  try {
    const result = await recognizeText(photoUri);
    rawText = result.text.trim();
  } catch {
    return emptyResult();
  }

  if (!rawText || rawText.length < 3) return emptyResult();

  const brand = extractBrandFromText(rawText);
  const model = extractModel(rawText);
  const barcode = extractBarcode(rawText);
  const title = buildOcrTitle(rawText, brand, model);
  const searchQuery = brand && model ? `${brand} ${model}` : title ?? '';
  const fieldCount = [brand, model, title].filter(Boolean).length;
  const alternateQueries: string[] = [];
  if (brand && model && searchQuery !== `${brand} ${model}`) alternateQueries.push(`${brand} ${model}`);
  if (title && title !== searchQuery) alternateQueries.push(title);
  if (brand && brand !== searchQuery) alternateQueries.push(brand);

  return {
    title,
    brand,
    model,
    category: null,
    barcode,
    rawText,
    confidence: fieldCount >= 2 ? 'medium' : fieldCount >= 1 ? 'medium' : 'low',
    searchQuery,
    alternateQueries,
    visionLabels: [],
    webEntities: [],
    matchingPages: [],
    bestGuessLabel: null,
  };
}

// ── Cloud Vision API call ──

async function callCloudVision(photoUri: string): Promise<VisionIdentifyResult | null> {
  try {
    const base64 = await readAsStringAsync(photoUri, { encoding: 'base64' });
    if (!base64 || base64.length < 100) {
      console.warn('[Vision] Base64 too short, skipping:', base64?.length);
      return null;
    }
    const result = await api.visionIdentify(base64);
    console.log('[Vision] Response:', result.bestGuessLabel, result.webEntities.length, 'entities', result.labels.length, 'labels');
    return result;
  } catch (error) {
    console.warn('[Vision] Cloud Vision call failed:', error instanceof Error ? error.message : error);
    return null;
  }
}

// ── Extractors ──

function extractBrandFromEntities(
  entities: { description: string }[],
  bestGuess: string | null,
): string | null {
  for (const entity of entities) {
    const match = entity.description.match(BRAND_PATTERN);
    if (match) return normalizeCase(match[1]);
  }
  if (bestGuess) {
    const match = bestGuess.match(BRAND_PATTERN);
    if (match) return normalizeCase(match[1]);
  }
  return null;
}

function extractBrandFromText(text: string): string | null {
  const match = text.match(BRAND_PATTERN);
  return match ? normalizeCase(match[1]) : null;
}

function extractModel(text: string): string | null {
  for (const pattern of MODEL_PATTERNS) {
    const match = text.match(pattern);
    if (match) {
      const raw = match[1].replace(/^(Model|Part|Style)[:\s#]+/i, '').trim();
      if (raw.length >= 3) return raw;
    }
  }
  return null;
}

function extractBarcode(text: string): string | null {
  const clean = text.replace(/\s/g, '');
  const match = clean.match(BARCODE_PATTERN);
  if (!match) return null;
  const len = match[1].length;
  return (len === 8 || len === 12 || len === 13 || len === 14) ? match[1] : null;
}

function inferCategoryFromLabels(labels: string[]): string | null {
  for (const label of labels) {
    for (const [key, cat] of Object.entries(LABEL_CATEGORY_MAP)) {
      if (label.toLowerCase().includes(key.toLowerCase())) return cat;
    }
  }
  return null;
}

function buildTitleFallback(
  brand: string | null,
  model: string | null,
  entities: { description: string }[],
): string | null {
  if (brand && model) return `${brand} ${model}`;
  if (entities.length >= 2) return entities.slice(0, 2).map((e) => e.description).join(' ');
  if (brand) return brand;
  if (entities.length > 0) return entities[0].description;
  return null;
}

function buildOcrTitle(text: string, brand: string | null, model: string | null): string | null {
  const lines = text.split('\n').map((l) => l.trim()).filter((l) =>
    l.length > 4 && l.length < 80 &&
    !l.match(/^https?:\/\//i) &&
    !l.match(/^\$?\d+\.?\d*$/) &&
    !l.match(/^(WARNING|CAUTION|DIRECTIONS|INGREDIENTS|NET WT|MADE IN|©)/i),
  );
  const scored = lines.map((line) => {
    let score = 0;
    if (brand && line.toLowerCase().includes(brand.toLowerCase())) score += 20;
    if (model && line.includes(model)) score += 15;
    if (line.length > 10 && line.length < 60) score += 10;
    if (/[a-z]/.test(line) && /[A-Z]/.test(line)) score += 5;
    return { line, score };
  });
  scored.sort((a, b) => b.score - a.score);
  if (scored[0]?.score >= 5) return scored[0].line;
  if (brand && model) return `${brand} ${model}`;
  return brand;
}

function normalizeCase(text: string): string {
  return KNOWN_BRANDS.find((b) => b.toLowerCase() === text.toLowerCase()) ?? text;
}

function emptyResult(): IdentifyResult {
  return {
    title: null, brand: null, model: null, category: null, barcode: null,
    rawText: '', confidence: 'low', searchQuery: '', alternateQueries: [],
    visionLabels: [], webEntities: [], matchingPages: [], bestGuessLabel: null,
  };
}

function escapeRegex(str: string): string {
  return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
