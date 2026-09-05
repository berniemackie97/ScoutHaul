import { api } from '../api';
import { ApiError } from '../api/apiError';
import { listingUpdateFrom, type CreateListingRequest } from '../api/listingRequests';
import { enqueueOperation } from '../db/localStore';
import type { Listing } from '../domain/types';
import { isWorthQueueing } from './offlineWrites';

/**
 * What happened to the generated copy the user asked to keep.
 *
 * Queued counts as kept: the words are on the phone and will reach the server. Only `rejected`
 * needs the user to do something.
 */
export type ListingCopySaveOutcome =
  | { status: 'saved'; created: boolean }
  | { status: 'queued'; because: 'offline' | 'unreachable' }
  | { status: 'rejected'; message: string };

export type ListingCopy = {
  inventoryItemId: string;
  title: string;
  description: string;
  /** Used only when there is no listing yet and one has to be created to hold the copy. */
  listPrice: number;
};

/** eBay is what every other listing path on this app defaults to; nothing here is eBay-specific. */
const DEFAULT_PLATFORM = 'eBay';

/**
 * Puts generated copy onto the item's listing, creating a draft listing if it has none.
 *
 * The generator wrote a title and a description and had nowhere to put them: the only button on the
 * screen was Regenerate, so the copy was retyped by hand or lost with the screen. It goes onto the
 * item's existing listing when there is one, because creating a second listing for an item that is
 * already listed is not what "save this" means.
 */
export async function saveListingCopy(copy: ListingCopy): Promise<ListingCopySaveOutcome> {
  try {
    const existing = await findListingForItem(copy.inventoryItemId);

    if (existing) {
      await api.updateListing(
        existing.id,
        listingUpdateFrom(existing, { title: copy.title, description: copy.description }),
      );
      return { status: 'saved', created: false };
    }

    if (!(copy.listPrice > 0)) {
      return {
        status: 'rejected',
        message: 'This item has no listing and no price to start one with. Set an asking price first.',
      };
    }

    await api.createListing(createRequest(copy));
    return { status: 'saved', created: true };
  } catch (error) {
    if (!isWorthQueueing(error)) {
      return {
        status: 'rejected',
        message: error instanceof Error ? error.message : 'Could not save this listing copy.',
      };
    }

    // Queued as a create. An update needs a listing id, and offline we cannot know whether the item
    // has one — the lookup is what just failed. A duplicate draft is recoverable; losing the words
    // is not.
    if (!(copy.listPrice > 0)) {
      return {
        status: 'rejected',
        message: 'This item has no listing and no price to start one with. Set an asking price first.',
      };
    }

    await enqueueOperation('CreateListing', '/api/listings', 'POST', createRequest(copy));
    return {
      status: 'queued',
      because: error instanceof ApiError && error.reason === 'offline' ? 'offline' : 'unreachable',
    };
  }
}

function createRequest(copy: ListingCopy): CreateListingRequest {
  return {
    inventoryItemId: copy.inventoryItemId,
    platform: DEFAULT_PLATFORM,
    listingUrl: null,
    listPrice: copy.listPrice,
    notes: null,
    title: copy.title,
    description: copy.description,
  };
}

/**
 * The listing this copy belongs on, if the item already has one.
 *
 * A listing that has already ended or sold is not somewhere new copy should land, so those are
 * skipped and a fresh draft is written instead.
 */
async function findListingForItem(inventoryItemId: string): Promise<Listing | undefined> {
  const listings = await api.listings();
  return listings.find(
    (listing) =>
      listing.inventoryItemId === inventoryItemId &&
      (listing.status === 'Draft' || listing.status === 'Active'),
  );
}

/** The sentence to show the user for an outcome. */
export function describeListingCopyOutcome(outcome: ListingCopySaveOutcome): string {
  switch (outcome.status) {
    case 'saved':
      return outcome.created ? 'Saved to a new draft listing.' : 'Saved to this item’s listing.';
    case 'queued':
      return outcome.because === 'offline'
        ? "Saved on this phone. It'll sync when you're back online."
        : "Saved on this phone. The server didn't answer, so it'll sync later.";
    case 'rejected':
      return outcome.message;
  }
}
