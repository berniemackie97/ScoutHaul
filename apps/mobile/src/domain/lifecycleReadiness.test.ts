import { readinessBlockers, readinessRequirements } from './lifecycleReadiness';

describe('inventory lifecycle readiness', () => {
  it('blocks ready-to-list video games without test/completeness facts', () => {
    const blockers = readinessBlockers('ReadyToList', {
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
});
