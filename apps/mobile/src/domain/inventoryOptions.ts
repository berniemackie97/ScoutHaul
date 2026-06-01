import type { InventoryStatus } from './types';

export const conditionOptions = [
  'New with tags',
  'New without tags',
  'Open box',
  'Near mint',
  'Light use',
  'Moderate use',
  'Heavy use',
  'Damaged',
  'For parts',
] as const;

export const categoryOptions = [
  'Clothing',
  'Shoes',
  'Electronics',
  'Video Games',
  'Toys',
  'Collectibles',
  'Home',
  'Books',
  'Media',
  'Tools',
  'Other',
] as const;

export const sizeSystemOptions = [
  'US letter',
  'US numeric',
  'Waist/Inseam',
  'Shoe US M',
  'Shoe US W',
  'Youth',
  'One size',
  'Other',
] as const;

export const damageSeverityOptions = [
  'Cosmetic',
  'Minor',
  'Moderate',
  'Major',
  'Parts only',
] as const;

export const damageTypeOptions = [
  'Stain',
  'Hole/tear',
  'Scuff/scratch',
  'Missing part',
  'Odor',
  'Does not power on',
  'Untested',
  'Other',
] as const;

export const workflowActionOptions: { status: InventoryStatus; label: string }[] = [
  { status: 'NeedsCleaning', label: 'Needs cleaning' },
  { status: 'NeedsTesting', label: 'Needs testing' },
  { status: 'NeedsPhotos', label: 'Needs photos' },
  { status: 'ReadyToList', label: 'Ready to list' },
  { status: 'Listed', label: 'Listed' },
];
