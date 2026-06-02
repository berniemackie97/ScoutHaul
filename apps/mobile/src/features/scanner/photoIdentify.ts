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

// ── Category signals ──

const CATEGORY_SIGNALS: Record<string, string[]> = {
  'Electronics': ['HDMI', 'USB', 'Bluetooth', 'WiFi', 'wireless', 'battery', 'charger', 'adapter'],
  'Clothing': ['cotton', 'polyester', 'nylon', 'fabric', 'wash', 'dry clean'],
  'Shoes': ['sole', 'insole', 'footbed', 'EUR'],
  'Toys': ['ages', 'choking hazard', 'small parts', 'assembly required'],
  'Appliances': ['watt', 'volt', 'cycles', 'capacity'],
  'Tools': ['torque', 'RPM', 'amp-hour', 'cordless'],
  'Video Games': ['ESRB', 'rated', 'players'],
  'Books': ['ISBN', 'edition', 'published', 'author', 'paperback', 'hardcover'],
};

const LABEL_CATEGORY_MAP: Record<string, string> = {
  'Laptop': 'Tech', 'Computer': 'Tech', 'Tablet': 'Tech', 'Phone': 'Tech',
  'Electronic device': 'Electronics', 'Audio equipment': 'Electronics', 'Camera': 'Electronics',
  'Clothing': 'Clothing', 'Shirt': 'Clothing', 'T-shirt': 'Clothing', 'Dress': 'Clothing', 'Jacket': 'Clothing',
  'Shoe': 'Shoes', 'Footwear': 'Shoes', 'Sneakers': 'Shoes', 'Boot': 'Shoes',
  'Toy': 'Toys', 'Action figure': 'Collectibles', 'Doll': 'Toys', 'Board game': 'Toys',
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

// ── Main identification function ──
// Strategy: Cloud Vision (visual AI) + OCR (text) in parallel.
// Cloud Vision gives: product name, brand, category from image recognition.
// OCR gives: model numbers, barcodes, text-based brand detection.
// Combine both for best results.

export async function identifyFromPhoto(photoUri: string): Promise<IdentifyResult> {
  const [visionResult, ocrResult] = await Promise.allSettled([
    callCloudVision(photoUri),
    callOcr(photoUri),
  ]);

  const vision = visionResult.status === 'fulfilled' ? visionResult.value : null;
  const rawText = ocrResult.status === 'fulfilled' ? ocrResult.value : '';

  // Extract from OCR
  const ocrBrand = rawText ? extractBrand(rawText) : null;
  const ocrModel = rawText ? extractModel(rawText) : null;
  const ocrCategory = rawText ? inferCategoryFromText(rawText) : null;
  const ocrBarcode = rawText ? extractBarcode(rawText) : null;

  // Extract from Cloud Vision
  const visionBrand = vision ? extractBrandFromVision(vision) : null;
  const visionCategory = vision ? inferCategoryFromVision(vision) : null;

  // Merge — Cloud Vision takes priority for title/brand, OCR for model/barcode
  const brand = visionBrand ?? ocrBrand;
  const model = ocrModel;
  const category = visionCategory ?? ocrCategory;
  const barcode = ocrBarcode;

  // Title: Cloud Vision bestGuessLabel > OCR title > web entity combo
  const title = vision?.bestGuessLabel
    ?? buildOcrTitle(rawText, brand, model)
    ?? (vision?.webEntities?.slice(0, 2).map((e) => e.description).join(' ') || null);

  // Search query: bestGuessLabel is the most powerful signal
  const searchQuery = vision?.searchQuery
    ?? buildQuery(brand, model, title, rawText);

  // Confidence: vision bestGuessLabel = high. Vision entities = medium. OCR only = depends.
  const hasVisionId = !!vision?.bestGuessLabel;
  const hasVisionEntities = (vision?.webEntities?.length ?? 0) > 0;
  const hasOcrFields = [brand, model, category].filter(Boolean).length;

  let confidence: 'high' | 'medium' | 'low' = 'low';
  if (hasVisionId) confidence = 'high';
  else if (hasVisionEntities || hasOcrFields >= 2) confidence = 'medium';
  else if (hasOcrFields >= 1) confidence = 'medium';

  return {
    title,
    brand,
    model,
    category,
    barcode,
    rawText,
    confidence,
    searchQuery: searchQuery ?? '',
    visionLabels: vision?.labels ?? [],
    webEntities: vision?.webEntities ?? [],
    bestGuessLabel: vision?.bestGuessLabel ?? null,
  };
}

// ── Cloud Vision call ──

async function callCloudVision(photoUri: string): Promise<VisionIdentifyResult | null> {
  try {
    const file = new File(photoUri);
    const buffer = await file.arrayBuffer();
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    const base64 = btoa(binary);
    return await api.visionIdentify(base64);
  } catch {
    // Vision API unavailable — fall back to OCR only
    return null;
  }
}

async function callOcr(photoUri: string): Promise<string> {
  try {
    const result = await recognizeText(photoUri);
    return result.text.trim();
  } catch {
    return '';
  }
}

// ── Brand extraction ──

function extractBrand(text: string): string | null {
  const match = text.match(BRAND_PATTERN);
  return match ? normalizeCase(match[1]) : null;
}

function extractBrandFromVision(vision: VisionIdentifyResult): string | null {
  // Check web entities for known brands (highest confidence)
  for (const entity of vision.webEntities) {
    const match = entity.description.match(BRAND_PATTERN);
    if (match) return normalizeCase(match[1]);
  }
  // Check bestGuessLabel
  if (vision.bestGuessLabel) {
    const match = vision.bestGuessLabel.match(BRAND_PATTERN);
    if (match) return normalizeCase(match[1]);
  }
  return null;
}

// ── Category inference ──

function inferCategoryFromVision(vision: VisionIdentifyResult): string | null {
  for (const label of vision.labels) {
    for (const [key, cat] of Object.entries(LABEL_CATEGORY_MAP)) {
      if (label.toLowerCase().includes(key.toLowerCase())) return cat;
    }
  }
  return null;
}

function inferCategoryFromText(text: string): string | null {
  const lower = text.toLowerCase();
  let best: string | null = null;
  let bestScore = 0;
  for (const [cat, signals] of Object.entries(CATEGORY_SIGNALS)) {
    const score = signals.filter((s) => lower.includes(s.toLowerCase())).length;
    if (score > bestScore) { bestScore = score; best = cat; }
  }
  return bestScore >= 2 ? best : null;
}

// ── Model / barcode extraction ──

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
  const match = text.replace(/\s/g, '').match(BARCODE_PATTERN);
  if (!match) return null;
  const len = match[1].length;
  return (len === 8 || len === 12 || len === 13 || len === 14) ? match[1] : null;
}

// ── Title / query building ──

function buildOcrTitle(text: string, brand: string | null, model: string | null): string | null {
  if (!text) return brand && model ? `${brand} ${model}` : brand;
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

function buildQuery(brand: string | null, model: string | null, title: string | null, rawText: string): string | null {
  if (brand && model) return `${brand} ${model}`;
  if (title) return title.slice(0, 100);
  if (rawText) {
    const lines = rawText.split('\n').map((l) => l.trim()).filter((l) => l.length > 3);
    const joined = lines.slice(0, 2).join(' ').slice(0, 100);
    return joined || null;
  }
  return null;
}

function normalizeCase(text: string): string {
  return KNOWN_BRANDS.find((b) => b.toLowerCase() === text.toLowerCase()) ?? text;
}

function escapeRegex(str: string): string {
  return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
