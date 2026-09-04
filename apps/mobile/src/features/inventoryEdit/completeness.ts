import { parseOptionalNumber } from '../../utils/money';
import type { AttributeMap } from './attributes';

/**
 * How much of each section the operator has filled in.
 *
 * These drive the "3 of 4" counters on the collapsed sections, which is the only thing telling
 * someone what is still worth opening. They live here rather than as memos inside the screen so the
 * rules are readable in one place and can be checked without rendering a 1400-line form.
 *
 * Each counts what the form counts: a picker value is present when it is set at all, and a typed
 * value when it holds something other than whitespace.
 */

const typed = (value: string): boolean => value.trim().length > 0;
const picked = (value: string): boolean => Boolean(value);

export function essentialsFilled(fields: {
  title: string;
  condition: string;
  category: string;
  brand: string;
}): number {
  return [typed(fields.title), picked(fields.condition), picked(fields.category), typed(fields.brand)]
    .filter(Boolean).length;
}

export function pricingFilled(fields: { purchasePrice: string; estimatedSalePrice: string }): number {
  return [typed(fields.purchasePrice), typed(fields.estimatedSalePrice)].filter(Boolean).length;
}

export function detailsFilled(fields: {
  sizeSystem: string;
  sizeValue: string;
  attributes: AttributeMap;
}): number {
  const attributeCount = Object.values(fields.attributes).filter(typed).length;
  return [picked(fields.sizeSystem), typed(fields.sizeValue)].filter(Boolean).length + attributeCount;
}

export function physicalFilled(fields: {
  barcode: string;
  manufacturerPartNumber: string;
  serialNumber: string;
  countryOfOrigin: string;
  manufactureYear: string;
  length: string;
  width: string;
  height: string;
  weight: string;
}): number {
  // Any one dimension counts the whole measurement as started — nobody fills length alone on purpose.
  const anyDimension = typed(fields.length) || typed(fields.width) || typed(fields.height);
  return [
    typed(fields.barcode),
    typed(fields.manufacturerPartNumber),
    typed(fields.serialNumber),
    picked(fields.countryOfOrigin),
    typed(fields.manufactureYear),
    anyDimension,
    typed(fields.weight),
  ].filter(Boolean).length;
}

export function workFilled(fields: {
  ownershipType: string;
  storageLocation: string;
  accessories: string;
  testingNotes: string;
  cleaningNotes: string;
  listingNotes: string;
  missingParts: string;
}): number {
  return [
    picked(fields.ownershipType),
    typed(fields.storageLocation),
    typed(fields.accessories),
    typed(fields.testingNotes),
    typed(fields.cleaningNotes),
    typed(fields.listingNotes),
    typed(fields.missingParts),
  ].filter(Boolean).length;
}

export function damageFilled(fields: {
  damageType: string;
  damageSeverity: string;
  damageNote: string;
}): number {
  return [picked(fields.damageType), picked(fields.damageSeverity), typed(fields.damageNote)]
    .filter(Boolean).length;
}

export type ProjectedMargin = { profit: number; pct: string; positive: boolean };

/**
 * What the item stands to make, shown live as the two prices are typed.
 *
 * Null whenever it cannot be computed — either price missing, or a sale price of zero, which would
 * divide by zero and has no margin to speak of anyway.
 */
export function projectedMargin(purchasePrice: string, estimatedSalePrice: string): ProjectedMargin | null {
  const cost = parseOptionalNumber(purchasePrice);
  const sale = parseOptionalNumber(estimatedSalePrice);
  if (cost === null || sale === null || sale === 0) return null;

  const profit = sale - cost;
  return { profit, pct: ((profit / sale) * 100).toFixed(0), positive: profit > 0 };
}
