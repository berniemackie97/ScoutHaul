import type { ItemProfile } from '../api/inventory';
import { readinessBlockers, readinessRequirements } from './lifecycleReadiness';

const videoGames: ItemProfile = {
  key: 'video-games',
  label: 'Video games',
  categories: ['Video Games'],
  fields: [
    { field: 'FunctionalStatus', label: 'Working condition', readiness: 'Required' },
    { field: 'CompletenessStatus', label: 'Completeness', readiness: 'Required' },
    { field: 'PackagingStatus', label: 'Packaging', readiness: 'Recommended' },
  ],
  attributes: [],
  sizeSystems: [],
  brandLabel: 'Console maker or publisher',
};

describe('inventory lifecycle readiness', () => {
  it('blocks ready-to-list video games without test/completeness facts', () => {
    const blockers = readinessBlockers('ReadyToList', {
      profile: videoGames,
      title: 'Nintendo Wii Sports',
      category: 'Video Games',
      condition: 'Good',
      estimatedSalePrice: 25,
      functionalStatus: 'Untested',
      completenessStatus: 'Unknown',
      packagingStatus: 'NotApplicable',
      photoCount: 1,
    });

    expect(blockers.map((item) => item.id)).toEqual(['functionalStatus', 'completenessStatus']);
  });

  it('allows ready-to-list when required facts are present', () => {
    const blockers = readinessBlockers('ReadyToList', {
      profile: videoGames,
      title: 'Nintendo Wii Sports',
      category: 'Video Games',
      condition: 'Good',
      estimatedSalePrice: 25,
      functionalStatus: 'FullyFunctional',
      completenessStatus: 'Complete',
      packagingStatus: 'LooseNoPackaging',
      photoCount: 1,
    });

    expect(blockers).toEqual([]);
  });

  it('uses needs-photos as a warning stage when photos are missing', () => {
    const photos = readinessRequirements('NeedsPhotos', {
      title: 'Untitled item',
      photoCount: 0,
    }).find((item) => item.id === 'photos');

    expect(photos).toMatchObject({ met: false, severity: 'warning' });
  });

  it('asks nothing of a kind of item its profile does not ask', () => {
    const requirements = readinessRequirements('ReadyToList', {
      title: 'Wool coat',
      category: 'Clothing',
      condition: 'Good',
      estimatedSalePrice: 80,
      photoCount: 1,
    });

    expect(requirements.map((item) => item.id)).not.toContain('functionalStatus');
    expect(readinessBlockers('ReadyToList', { title: 'Wool coat', category: 'Clothing', condition: 'Good', estimatedSalePrice: 80, photoCount: 1 })).toEqual([]);
  });
});
