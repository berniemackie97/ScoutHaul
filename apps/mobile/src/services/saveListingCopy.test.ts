import { api } from '../api';
import { ApiError } from '../api/apiError';
import { enqueueOperation } from '../db/localStore';
import type { Listing } from '../domain/types';
import { describeListingCopyOutcome, saveListingCopy } from './saveListingCopy';

jest.mock('../api', () => ({
  api: { listings: jest.fn(), createListing: jest.fn(), updateListing: jest.fn() },
}));
jest.mock('../db/localStore', () => ({ enqueueOperation: jest.fn() }));

const mockListings = api.listings as jest.Mock;
const mockCreate = api.createListing as jest.Mock;
const mockUpdate = api.updateListing as jest.Mock;
const mockEnqueue = enqueueOperation as jest.Mock;

const copy = {
  inventoryItemId: 'item-1',
  title: "Vintage Brass Banker's Lamp",
  description: 'Working. Original green glass shade.',
  listPrice: 89,
};

const listing = (overrides: Partial<Listing> = {}): Listing => ({
  id: 'listing-1',
  inventoryItemId: 'item-1',
  inventoryItemTitle: 'Brass Lamp',
  inventoryItemStatus: 'ReadyToList',
  platform: 'eBay',
  listingUrl: 'https://ebay.example/1',
  listPrice: 75,
  status: 'Draft',
  notes: 'Ships in the original box.',
  ...overrides,
});

beforeEach(() => {
  jest.clearAllMocks();
  mockListings.mockResolvedValue([]);
});

describe('saveListingCopy', () => {
  it('writes the copy onto the listing the item already has', async () => {
    mockListings.mockResolvedValue([listing()]);

    await expect(saveListingCopy(copy)).resolves.toEqual({ status: 'saved', created: false });

    expect(mockCreate).not.toHaveBeenCalled();
    expect(mockUpdate).toHaveBeenCalledWith('listing-1', {
      platform: 'eBay',
      listingUrl: 'https://ebay.example/1',
      listPrice: 75,
      status: 'Draft',
      notes: 'Ships in the original box.',
      title: copy.title,
      description: copy.description,
    });
  });

  it('leaves everything else on the listing alone', async () => {
    // The endpoint replaces the whole listing, so a body missing a field clears it. Saving copy
    // must not end a live listing or wipe the note about the original box.
    mockListings.mockResolvedValue([listing({ status: 'Active', listedAt: '2026-09-01T00:00:00Z' })]);

    await saveListingCopy(copy);

    expect(mockUpdate).toHaveBeenCalledWith(
      'listing-1',
      expect.objectContaining({
        status: 'Active',
        notes: 'Ships in the original box.',
        listPrice: 75,
        listingUrl: 'https://ebay.example/1',
      }),
    );
  });

  it('starts a draft listing when the item has none', async () => {
    await expect(saveListingCopy(copy)).resolves.toEqual({ status: 'saved', created: true });

    expect(mockUpdate).not.toHaveBeenCalled();
    expect(mockCreate).toHaveBeenCalledWith(
      expect.objectContaining({
        inventoryItemId: 'item-1',
        listPrice: 89,
        title: copy.title,
        description: copy.description,
      }),
    );
  });

  it('ignores listings for other items', async () => {
    mockListings.mockResolvedValue([listing({ id: 'other', inventoryItemId: 'item-2' })]);

    await saveListingCopy(copy);

    expect(mockUpdate).not.toHaveBeenCalled();
    expect(mockCreate).toHaveBeenCalled();
  });

  it('does not reopen a listing that has already ended or sold', async () => {
    mockListings.mockResolvedValue([listing({ status: 'Sold' })]);

    await saveListingCopy(copy);

    expect(mockUpdate).not.toHaveBeenCalled();
    expect(mockCreate).toHaveBeenCalled();
  });

  it('says what is missing when there is nothing to price a new listing with', async () => {
    await expect(saveListingCopy({ ...copy, listPrice: 0 })).resolves.toMatchObject({
      status: 'rejected',
    });
    expect(mockCreate).not.toHaveBeenCalled();
  });

  it('keeps the copy on the phone when there is no connection', async () => {
    mockListings.mockRejectedValue(new ApiError({ reason: 'offline', message: 'offline' }));

    await expect(saveListingCopy(copy)).resolves.toEqual({ status: 'queued', because: 'offline' });
    expect(mockEnqueue).toHaveBeenCalledWith(
      'CreateListing',
      '/api/listings',
      'POST',
      expect.objectContaining({ title: copy.title, description: copy.description }),
    );
  });

  it('keeps the copy when the server does not answer', async () => {
    mockUpdate.mockRejectedValue(new ApiError({ reason: 'timeout', message: 'slow' }));
    mockListings.mockResolvedValue([listing()]);

    await expect(saveListingCopy(copy)).resolves.toEqual({
      status: 'queued',
      because: 'unreachable',
    });
    expect(mockEnqueue).toHaveBeenCalled();
  });

  it('does not queue something the server rejected', async () => {
    mockListings.mockResolvedValue([listing()]);
    mockUpdate.mockRejectedValue(
      new ApiError({ reason: 'http', status: 400, message: 'Title must be 300 characters or fewer.' }),
    );

    await expect(saveListingCopy(copy)).resolves.toEqual({
      status: 'rejected',
      message: 'Title must be 300 characters or fewer.',
    });
    expect(mockEnqueue).not.toHaveBeenCalled();
  });

  it('does not queue when the user is signed out', async () => {
    mockListings.mockRejectedValue(new ApiError({ reason: 'unauthenticated', message: 'Sign in.' }));

    await expect(saveListingCopy(copy)).resolves.toMatchObject({ status: 'rejected' });
    expect(mockEnqueue).not.toHaveBeenCalled();
  });
});

describe('describeListingCopyOutcome', () => {
  it('does not describe queued copy as a failure', () => {
    const offline = describeListingCopyOutcome({ status: 'queued', because: 'offline' });
    expect(offline).toMatch(/saved/i);
    expect(offline).not.toMatch(/fail|error|could not/i);
  });

  it('passes a rejection through verbatim so the user sees the real reason', () => {
    expect(
      describeListingCopyOutcome({ status: 'rejected', message: 'Set an asking price first.' }),
    ).toBe('Set an asking price first.');
  });
});
