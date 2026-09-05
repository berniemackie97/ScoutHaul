import type { Listing, ListingStatus } from '../domain/types';

/**
 * The listing write contracts and the one way to build them.
 *
 * Kept apart from `commerce.ts` because that module pulls in the HTTP client, and through it auth,
 * Supabase and AsyncStorage — none of which a request body needs. Anything that only wants to shape
 * a body imports this instead.
 */

export type CreateListingRequest = {
  inventoryItemId: string;
  platform: string;
  listingUrl?: string | null;
  listPrice: number;
  notes?: string | null;
  /** The copy a buyer reads. Optional here: a draft can be created bare and written later. */
  title?: string | null;
  description?: string | null;
};

/**
 * The whole listing, because PUT /api/listings/{id} replaces it.
 *
 * Every field is required so a caller cannot leave one out and silently clear it on the server.
 * Build this with {@link listingUpdateFrom} rather than by hand — hand-assembling it is how a
 * "mark this ended" action ends up erasing the title someone wrote.
 */
export type UpdateListingRequest = {
  platform: string;
  listingUrl: string | null;
  listPrice: number;
  status: ListingStatus;
  notes: string | null;
  title: string | null;
  description: string | null;
};

/** The parts of a listing an edit is allowed to change. */
export type ListingEdits = Partial<UpdateListingRequest>;

/**
 * Builds a full update body from the listing as it stands, with `edits` applied on top.
 *
 * Callers change one thing at a time — a status, a price, the generated copy — but the endpoint
 * wants the whole record. This is the one place that difference is handled.
 */
export function listingUpdateFrom(listing: Listing, edits: ListingEdits = {}): UpdateListingRequest {
  return {
    platform: listing.platform,
    listingUrl: listing.listingUrl ?? null,
    listPrice: listing.listPrice,
    status: listing.status,
    notes: listing.notes ?? null,
    title: listing.title ?? null,
    description: listing.description ?? null,
    ...edits,
  };
}
