import type { ItemProfile } from '../../api/inventory';
import { fieldSpec, GENERAL_PROFILE, isListAttribute, profileFor } from './itemProfiles';

const clothing: ItemProfile = {
  key: 'apparel',
  label: 'Clothing',
  categories: ['Clothing'],
  fields: [{ field: 'Color', label: 'Color', readiness: 'Optional' }],
  attributes: [
    { key: 'material', label: 'Material', options: ['Cotton', 'Silk'], maxValues: 3 },
    { key: 'department', label: 'Department', options: ['Women', 'Men'], maxValues: 1 },
  ],
  sizeSystems: ['US letter'],
  brandLabel: 'Brand',
};

describe('item profiles', () => {
  it('finds the profile for a category regardless of case, and the general one otherwise', () => {
    expect(profileFor([clothing], 'clothing')).toBe(clothing);
    expect(profileFor([clothing], 'Video Games')).toBe(GENERAL_PROFILE);
    expect(profileFor([clothing], '')).toBe(GENERAL_PROFILE);
    expect(profileFor(undefined, 'Clothing')).toBe(GENERAL_PROFILE);
  });

  it('names the fields a profile has and tells a list detail from a single pick', () => {
    expect(fieldSpec(clothing, 'Color')?.label).toBe('Color');
    expect(fieldSpec(clothing, 'SerialNumber')).toBeUndefined();
    expect(isListAttribute(clothing.attributes[0])).toBe(true);
    expect(isListAttribute(clothing.attributes[1])).toBe(false);
  });
});
