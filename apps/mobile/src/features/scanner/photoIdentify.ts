import { recognizeText } from '@infinitered/react-native-mlkit-text-recognition';

export type IdentifyResult = {
  title: string | null;
  brand: string | null;
  model: string | null;
  category: string | null;
  barcode: string | null;
  rawText: string;
  confidence: 'high' | 'medium' | 'low';
  searchQuery: string;
};

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
];

const BRAND_PATTERN = new RegExp(`\\b(${KNOWN_BRANDS.map(escapeRegex).join('|')})\\b`, 'i');

const CATEGORY_SIGNALS: Record<string, string[]> = {
  'Electronics': ['HDMI', 'USB', 'Bluetooth', 'WiFi', 'wireless', 'battery', 'charger', 'adapter', 'remote'],
  'Clothing': ['cotton', 'polyester', 'nylon', 'fabric', 'wash', 'dry clean', 'XS', 'XXL'],
  'Shoes': ['sole', 'insole', 'footbed', 'EUR', 'UK'],
  'Toys': ['ages', 'choking hazard', 'small parts', 'assembly required'],
  'Appliances': ['watt', 'volt', 'cycles', 'capacity', 'quart'],
  'Tools': ['torque', 'RPM', 'amp-hour', 'cordless'],
  'Video Games': ['ESRB', 'rated', 'players', 'online'],
  'Books': ['ISBN', 'edition', 'published', 'author', 'paperback', 'hardcover'],
  'Collectibles': ['limited edition', 'numbered', 'certificate', 'authentic'],
};

const MODEL_PATTERNS = [
  /\b([A-Z]{1,4}[-\s]?\d{2,6}[A-Z]?\b)/,
  /\b(Model[:\s#]+\S+)/i,
  /\b(Part[:\s#]+\S+)/i,
  /\b(Style[:\s#]+\S+)/i,
  /\b(Item[:\s#]+\S+)/i,
  /\b(Ref[:\s.#]+\S+)/i,
];

const BARCODE_PATTERN = /\b(\d{8}|\d{12,14})\b/;

export async function identifyFromPhoto(photoUri: string): Promise<IdentifyResult> {
  let rawText = '';
  try {
    const result = await recognizeText(photoUri);
    rawText = result.text.trim();
  } catch {
    return emptyResult('');
  }

  if (!rawText || rawText.length < 3) {
    return emptyResult(rawText);
  }

  const brand = extractBrand(rawText);
  const model = extractModel(rawText);
  const category = inferCategory(rawText);
  const barcode = extractBarcode(rawText);
  const title = buildTitle(rawText, brand, model);
  const searchQuery = buildQuery(brand, model, title, rawText);

  const fieldCount = [brand, model, category, title].filter(Boolean).length;
  const confidence = fieldCount >= 3 ? 'high' : fieldCount >= 1 ? 'medium' : 'low';

  return { title, brand, model, category, barcode, rawText, confidence, searchQuery };
}

function extractBrand(text: string): string | null {
  const match = text.match(BRAND_PATTERN);
  return match ? normalizeCase(match[1]) : null;
}

function extractModel(text: string): string | null {
  for (const pattern of MODEL_PATTERNS) {
    const match = text.match(pattern);
    if (match) {
      return match[1].replace(/^(Model|Part|Style|Item|Ref)[:\s#]+/i, '').trim();
    }
  }
  return null;
}

function inferCategory(text: string): string | null {
  const lower = text.toLowerCase();
  let bestCategory: string | null = null;
  let bestScore = 0;

  for (const [category, signals] of Object.entries(CATEGORY_SIGNALS)) {
    const score = signals.filter((signal) => lower.includes(signal.toLowerCase())).length;
    if (score > bestScore) {
      bestScore = score;
      bestCategory = category;
    }
  }

  return bestScore >= 2 ? bestCategory : null;
}

function extractBarcode(text: string): string | null {
  const match = text.replace(/\s/g, '').match(BARCODE_PATTERN);
  if (!match) return null;
  const candidate = match[1];
  if (candidate.length === 8 || candidate.length === 12 || candidate.length === 13 || candidate.length === 14) {
    return candidate;
  }
  return null;
}

function buildTitle(text: string, brand: string | null, model: string | null): string | null {
  const lines = text.split('\n').map((l) => l.trim()).filter((l) =>
    l.length > 4 &&
    l.length < 80 &&
    !l.match(/^https?:\/\//i) &&
    !l.match(/^\$?\d+\.?\d*$/) &&
    !l.match(/^(WARNING|CAUTION|DIRECTIONS|INGREDIENTS|NET WT|MADE IN|©)/i) &&
    !l.match(/^\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}$/) &&
    !l.match(/^\d{8,14}$/),
  );

  const scored = lines.map((line) => {
    let score = 0;
    if (brand && line.toLowerCase().includes(brand.toLowerCase())) score += 20;
    if (model && line.includes(model)) score += 15;
    if (line.length > 10 && line.length < 60) score += 10;
    if (/[a-z]/.test(line) && /[A-Z]/.test(line)) score += 5;
    if (/^\d/.test(line)) score -= 10;
    return { line, score };
  });

  scored.sort((a, b) => b.score - a.score);
  const best = scored[0];
  if (!best || best.score < 5) {
    if (brand && model) return `${brand} ${model}`;
    if (brand) return brand;
    return null;
  }
  return best.line;
}

function buildQuery(brand: string | null, model: string | null, title: string | null, rawText: string): string {
  if (brand && model) return `${brand} ${model}`;
  if (title) return title.slice(0, 100);
  const lines = rawText.split('\n').map((l) => l.trim()).filter((l) => l.length > 3);
  return lines.slice(0, 2).join(' ').slice(0, 100);
}

function normalizeCase(text: string): string {
  const brand = KNOWN_BRANDS.find((b) => b.toLowerCase() === text.toLowerCase());
  return brand ?? text;
}

function emptyResult(rawText: string): IdentifyResult {
  return { title: null, brand: null, model: null, category: null, barcode: null, rawText, confidence: 'low', searchQuery: '' };
}

function escapeRegex(str: string): string {
  return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
