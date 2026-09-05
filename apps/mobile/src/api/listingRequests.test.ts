import type { Listing } from '../domain/types';
import { listingUpdateFrom } from './listingRequests';

const listing: Listing = {
  id: 'listing-1',
  inventoryItemId: 'item-1',
  inventoryItemTitle: 'Brass Lamp',
  inventoryItemStatus: 'Listed',
  platform: 'eBay',
  listingUrl: 'https://ebay.example/1',
  listPrice: 75,
  status: 'Active',
  listedAt: '2026-09-01T00:00:00Z',
  notes: 'Ships in the original box.',
  title: "Vintage Brass Banker's Lamp",
  description: 'Working. Original green glass shade.',
};

describe('listingUpdateFrom', () => {
  it('carries every field the endpoint would otherwise clear', () => {
    // PUT replaces the listing. A body missing a field is a body that erases it.
    expect(listingUpdateFrom(listing)).toEqual({
      platform: 'eBay',
      listingUrl: 'https://ebay.example/1',
      listPrice: 75,
      status: 'Active',
      notes: 'Ships in the original box.',
      title: "Vintage Brass Banker's Lamp",
      description: 'Working. Original green glass shade.',
    });
  });

  it('changes only what the edit names', () => {
    const body = listingUpdateFrom(listing, { status: 'Ended' });

    expect(body.status).toBe('Ended');
    expect(body.title).toBe("Vintage Brass Banker's Lamp");
    expect(body.description).toBe('Working. Original green glass shade.');
    expect(body.notes).toBe('Ships in the original box.');
  });

  it('sends absent copy as null rather than leaving it out', () => {
    // undefined disappears in JSON, and a field the server never sees is a field it cannot tell
    // apart from one deliberately cleared.
    const body = listingUpdateFrom({ ...listing, title: undefined, description: undefined, notes: undefined, listingUrl: undefined });

    expect(body).toMatchObject({ title: null, description: null, notes: null, listingUrl: null });
  });
});
