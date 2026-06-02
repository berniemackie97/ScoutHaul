import { recognizeText } from '@infinitered/react-native-mlkit-text-recognition';
import { File } from 'expo-file-system';
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

  // Brand: logos > web entities > OCR text
  const logoBrand = vision.detectedLogos.length > 0 ? vision.detectedLogos[0] : null;
  const entityBrand = extractBrandFromEntities(vision.webEntities, vision.bestGuessLabel);
  const textBrand = rawText ? extractBrandFromText(rawText) : null;
  const brand = logoBrand ?? entityBrand ?? textBrand;

  // Model: from OCR text (Cloud Vision text detection)
  const model = rawText ? extractModel(rawText) : null;

  // Category: from labels
  const category = inferCategoryFromLabels(vision.labels);

  // Barcode: from OCR text
  const barcode = rawText ? extractBarcode(rawText) : null;

  // Title: bestGuessLabel is the strongest
  const title = vision.bestGuessLabel ?? buildTitleFallback(brand, model, vision.webEntities);

  // Search query
  const searchQuery = vision.searchQuery ?? title ?? '';

  // Confidence
  const hasVisionId = !!vision.bestGuessLabel;
  const hasEntities = vision.webEntities.length > 0;
  const confidence: IdentifyResult['confidence'] = hasVisionId ? 'high' : hasEntities ? 'medium' : 'low';

  return {
    title,
    brand,
    model,
    category,
    barcode,
    rawText,
    confidence,
    searchQuery,
    visionLabels: vision.labels,
    webEntities: vision.webEntities,
    matchingPages: vision.matchingPages,
    bestGuessLabel: vision.bestGuessLabel,
  };
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

  return {
    title,
    brand,
    model,
    category: null,
    barcode,
    rawText,
    confidence: fieldCount >= 2 ? 'medium' : fieldCount >= 1 ? 'medium' : 'low',
    searchQuery,
    visionLabels: [],
    webEntities: [],
    matchingPages: [],
    bestGuessLabel: null,
  };
}

// ── Cloud Vision API call ──

async function callCloudVision(photoUri: string): Promise<VisionIdentifyResult | null> {
  try {
    const file = new File(photoUri);
    const buffer = await file.arrayBuffer();
    const base64 = uint8ToBase64(new Uint8Array(buffer));
    return await api.visionIdentify(base64);
  } catch {
    return null;
  }
}

function uint8ToBase64(bytes: Uint8Array): string {
  const CHUNK = 8192;
  const chunks: string[] = [];
  for (let i = 0; i < bytes.length; i += CHUNK) {
    chunks.push(String.fromCharCode(...bytes.subarray(i, Math.min(i + CHUNK, bytes.length))));
  }
  return btoa(chunks.join(''));
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
    rawText: '', confidence: 'low', searchQuery: '',
    visionLabels: [], webEntities: [], matchingPages: [], bestGuessLabel: null,
  };
}

function escapeRegex(str: string): string {
  return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
