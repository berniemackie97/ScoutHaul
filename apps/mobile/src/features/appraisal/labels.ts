import type { ToneKey } from '../../components/theme';
import type {
  Comp,
  CompKind,
  ConfidenceLevel,
  IdentificationCertainty,
  IdentificationSource,
  Price,
} from './types';

export const certaintyLabel: Record<IdentificationCertainty, string> = {
  High: 'Sure',
  Medium: 'Likely',
  Low: 'Best guess',
};

export const certaintyTone: Record<IdentificationCertainty, ToneKey> = {
  High: 'success',
  Medium: 'info',
  Low: 'warning',
};

export const sourceLabel: Record<IdentificationSource, string> = {
  Catalog: 'From your records',
  Barcode: 'From the barcode',
  Photo: 'From the photo',
  Details: 'From your description',
};

export const levelTone: Record<ConfidenceLevel, ToneKey> = {
  Strong: 'success',
  Fair: 'info',
  Thin: 'warning',
  Guess: 'danger',
  None: 'neutral',
};

export const compKindLabel: Record<CompKind, string> = {
  YourSale: 'Your sale',
  Sold: 'Sold',
  Asking: 'Listed',
  Guide: 'Guide',
};

export function basisLabel(price: Price, comps: Comp[]): string {
  const count = (kinds: CompKind[]) => comps.filter((comp) => comp.included && kinds.includes(comp.kind)).length;
  switch (price.basis) {
    case 'Sales':
      return plural(count(['YourSale', 'Sold']), 'matching sale');
    case 'Guide':
      return 'Price guide';
    case 'Asking':
      return `${plural(count(['Asking']), 'current listing')}, no sales`;
    case 'Estimate':
      return 'Model estimate, no comps';
    case 'None':
      return 'No price yet';
  }
}

// The editor's condition list; a vision grade maps onto it so the field is never left holding free text.
const conditionByGrade: Record<string, string> = {
  new: 'New',
  'like-new': 'Excellent',
  good: 'Good',
  fair: 'Fair',
  poor: 'Poor',
  'for-parts': 'For parts',
};

export function conditionFromGrade(grade: string | null): string | null {
  return grade ? conditionByGrade[grade.toLowerCase()] ?? null : null;
}

export function compDateLabel(comp: Comp, now: Date = new Date()): string | null {
  if (!comp.date) return null;
  const date = new Date(comp.date);
  if (Number.isNaN(date.getTime())) return null;

  const days = Math.floor((now.getTime() - date.getTime()) / 86_400_000);
  const when =
    days < 1 ? 'today'
    : days < 2 ? 'yesterday'
    : days < 60 ? `${days} days ago`
    : date.toLocaleDateString('en-US', { month: 'short', year: 'numeric' });
  return comp.kind === 'Asking' ? `Listed ${when}` : `Sold ${when}`;
}

/** A typed sale date as an ISO instant: blank means today; a malformed or future date is null. */
export function parseSoldDay(value: string, now: Date = new Date()): string | null {
  const trimmed = value.trim();
  if (!trimmed) return now.toISOString();
  if (!/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) return null;
  const date = new Date(`${trimmed}T12:00:00Z`);
  return Number.isNaN(date.getTime()) || date.getTime() > now.getTime() ? null : date.toISOString();
}

export function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}
