import type { InventoryItem, InventoryStatus } from './types';

export type InventoryReadinessFacts = {
  title?: string | null;
  description?: string | null;
  category?: string | null;
  condition?: string | null;
  functionalStatus?: string | null;
  completenessStatus?: string | null;
  authenticityStatus?: string | null;
  packagingStatus?: string | null;
  testingNotes?: string | null;
  estimatedSalePrice?: string | number | null;
  photoCount?: number | null;
  componentCount?: number | null;
  isBundle?: boolean | null;
};

export type ReadinessRequirement = {
  id: string;
  label: string;
  met: boolean;
  severity: 'blocker' | 'warning';
};

const testingCategories = new Set(['Video Games', 'Tech', 'Phones', 'Cameras', 'Audio', 'TV', 'Electronics', 'Appliances', 'Musical Instruments', 'Tools', 'Fitness']);
const completenessCategories = new Set(['Video Games', 'Tech', 'Phones', 'Cameras', 'Audio', 'TV', 'Electronics', 'Appliances', 'Toys', 'Collectibles', 'Media', 'Musical Instruments', 'Tools', 'Baby']);
const packagingCategories = new Set(['Video Games', 'Collectibles', 'Toys', 'Tech', 'Phones', 'Cameras', 'Audio', 'TV', 'Electronics', 'Media', 'Books', 'Watches', 'Jewelry', 'Shoes', 'Handbags']);
const authenticityCategories = new Set(['Collectibles', 'Jewelry', 'Watches', 'Handbags', 'Shoes', 'Clothing', 'Sporting Goods']);

function present(value: unknown) {
  return typeof value === 'string' ? value.trim().length > 0 : value !== null && value !== undefined;
}

function positiveMoney(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === '') return false;
  const parsed = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(parsed) && parsed > 0;
}

function known(value: string | null | undefined, unknownValues: string[]) {
  return Boolean(value && !unknownValues.includes(value));
}

export function readinessRequirements(targetStatus: InventoryStatus, facts: InventoryReadinessFacts): ReadinessRequirement[] {
  const requirements: ReadinessRequirement[] = [];
  const readyIntent = targetStatus === 'ReadyToList' || targetStatus === 'Listed';
  const photoRequired = readyIntent || targetStatus === 'NeedsPhotos';

  requirements.push({ id: 'title', label: 'Title recorded', met: present(facts.title), severity: 'blocker' });

  if (readyIntent) {
    requirements.push({ id: 'category', label: 'Category selected', met: present(facts.category), severity: 'blocker' });
    requirements.push({
      id: facts.isBundle ? 'description' : 'condition',
      label: facts.isBundle ? 'Bundle contents and condition spread described' : 'Cosmetic condition selected',
      met: facts.isBundle ? present(facts.description) : present(facts.condition),
      severity: 'blocker',
    });
    requirements.push({ id: 'estimatedSalePrice', label: 'Expected sale price recorded', met: positiveMoney(facts.estimatedSalePrice), severity: 'blocker' });
  }

  if (facts.isBundle || targetStatus === 'ReadyToList') {
    requirements.push({
      id: 'components',
      label: facts.isBundle ? 'At least two bundle components selected' : 'Not a bundle-only requirement',
      met: facts.isBundle ? (facts.componentCount ?? 0) >= 2 : true,
      severity: 'blocker',
    });
  }

  if (facts.category && testingCategories.has(facts.category)) {
    requirements.push({
      id: 'functionalStatus',
      label: 'Functional/test status recorded',
      met: known(facts.functionalStatus, ['Unknown', 'Untested']),
      severity: readyIntent ? 'blocker' : 'warning',
    });
    requirements.push({
      id: 'testingNotes',
      label: 'Testing notes/disclosure recorded',
      met: present(facts.testingNotes) || known(facts.functionalStatus, ['Unknown', 'Untested', 'FullyFunctional']),
      severity: 'warning',
    });
  }

  if (facts.category && completenessCategories.has(facts.category)) {
    requirements.push({
      id: 'completenessStatus',
      label: 'Completeness recorded',
      met: known(facts.completenessStatus, ['Unknown']),
      severity: readyIntent ? 'blocker' : 'warning',
    });
  }

  if (facts.category && packagingCategories.has(facts.category)) {
    requirements.push({
      id: 'packagingStatus',
      label: 'Packaging / CIB state recorded',
      met: known(facts.packagingStatus, ['NotApplicable']),
      severity: 'warning',
    });
  }

  if (facts.category && authenticityCategories.has(facts.category)) {
    requirements.push({
      id: 'authenticityStatus',
      label: 'Authenticity assessed or disclosed',
      met: known(facts.authenticityStatus, ['NotAssessed']),
      severity: 'warning',
    });
  }

  if (photoRequired && facts.photoCount !== null && facts.photoCount !== undefined) {
    requirements.push({
      id: 'photos',
      label: 'Photos attached',
      met: facts.photoCount > 0,
      severity: readyIntent ? 'blocker' : 'warning',
    });
  }

  return requirements.filter((requirement) => requirement.id !== 'components' || facts.isBundle);
}

export function readinessBlockers(targetStatus: InventoryStatus, facts: InventoryReadinessFacts) {
  return readinessRequirements(targetStatus, facts).filter((requirement) => requirement.severity === 'blocker' && !requirement.met);
}

export function factsFromInventoryItem(item: InventoryItem): InventoryReadinessFacts {
  return {
    title: item.title,
    description: item.description,
    category: item.category,
    condition: item.condition,
    functionalStatus: item.functionalStatus,
    completenessStatus: item.completenessStatus,
    authenticityStatus: item.authenticityStatus,
    packagingStatus: item.packagingStatus,
    testingNotes: item.testingNotes,
    estimatedSalePrice: item.estimatedSalePrice,
    photoCount: item.photos?.length ?? item.photoUrls?.length ?? 0,
    componentCount: item.bundleComponentCount,
    isBundle: item.isBundle,
  };
}
