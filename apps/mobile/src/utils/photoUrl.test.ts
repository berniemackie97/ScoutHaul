import { appConfig } from '../config';

jest.mock('../config', () => ({ appConfig: { apiBaseUrl: 'https://api.test' } }));
import { isPrivateInventoryPhotoUrl } from './photoUrl';

describe('inventory photo request identity', () => {
  const origin = appConfig.apiBaseUrl.replace(/\/$/, '');
  it('requires auth only for inventory image endpoints on this API', () => {
    const item = '11111111-1111-4111-8111-111111111111';
    const photo = '22222222-2222-4222-8222-222222222222';
    const path = `/api/inventory/${item}/photos/${photo}/thumbnail`;
    expect(isPrivateInventoryPhotoUrl(path)).toBe(true);
    expect(isPrivateInventoryPhotoUrl(`${origin}${path}`)).toBe(true);
    expect(isPrivateInventoryPhotoUrl(`/api/storefront/photos/${photo}`)).toBe(false);
  });

  it('never sends credentials to third-party hosts', () => {
    const item = '11111111-1111-4111-8111-111111111111';
    const photo = '22222222-2222-4222-8222-222222222222';
    const path = `/api/inventory/${item}/photos/${photo}/original`;
    expect(isPrivateInventoryPhotoUrl(`https://attacker.example${path}`)).toBe(false);
    expect(isPrivateInventoryPhotoUrl(`https://cdn.example.com/image.jpg`)).toBe(false);
    expect(isPrivateInventoryPhotoUrl('file:///local/photo.jpg')).toBe(false);
  });
});
