import type { ToneKey } from '../components/theme';
import type { ListingStatus, WorkTaskStatus } from '../domain/commerce.types';
import type { InventoryStatus } from '../domain/inventory.types';
import type { OrderStatus } from '../domain/order.types';

/**
 * Every status-to-colour decision in the app.
 *
 * This file already claimed to be the single source of truth and was not: five screens carried their
 * own copies, two of them byte-identical, and one had drifted — the listing queue painted
 * `ReadyToList` green while the inventory list painted it amber, so the same item changed colour
 * depending on which screen you were looking at.
 *
 * The tones mean: success = done, warning = needs you, info = in flight, danger = went wrong,
 * neutral = nothing to say yet.
 */

/** Where an item is in its own lifecycle. */
export function inventoryStatusTone(status: InventoryStatus | string): ToneKey {
  switch (status) {
    case 'Listed':
    case 'Purchased':
    case 'Bundled':
      return 'info';
    case 'Sold':
    case 'Shipped':
    case 'Delivered':
    case 'Packed':
      return 'success';
    // Still owes work before it can be sold. ReadyToList belongs here: the prep is finished, but the
    // item is not listed yet and it is the user's move.
    case 'Researching':
    case 'NeedsCleaning':
    case 'NeedsTesting':
    case 'NeedsRepair':
    case 'NeedsPhotos':
    case 'ReadyToList':
      return 'warning';
    case 'Returned':
    case 'Disposed':
      return 'danger';
    case 'Draft':
    case 'Watchlist':
    default:
      return 'neutral';
  }
}

/**
 * A different question from {@link inventoryStatusTone}: not "where is this item", but "can I list it
 * right now, or is something blocking me".
 *
 * The listing queue is a worklist of items waiting to be listed, so on that screen the useful split is
 * ready versus blocked — which is why it had grown its own copy. Keeping it as its own named function
 * says that out loud instead of leaving two contradictory answers to the same question.
 */
export function listingReadinessTone(status: InventoryStatus | string): ToneKey {
  if (status === 'ReadyToList') return 'success';
  return inventoryStatusTone(status) === 'warning' ? 'warning' : 'neutral';
}

/** How far an order has got, from placed to delivered. */
export function orderStatusTone(status: OrderStatus): ToneKey {
  switch (status) {
    case 'Delivered':
      return 'success';
    case 'Cancelled':
    case 'Refunded':
      return 'danger';
    case 'Pending':
      return 'warning';
    case 'Paid':
    case 'Fulfilling':
    case 'Shipped':
      return 'info';
    default:
      return 'neutral';
  }
}

/** The state of a listing on a sales channel. */
export function listingStatusTone(status: ListingStatus): ToneKey {
  switch (status) {
    case 'Active':
      return 'success';
    case 'Sold':
      return 'info';
    case 'Draft':
      return 'warning';
    case 'Cancelled':
      return 'danger';
    default:
      return 'neutral';
  }
}

/** The state of a piece of work on the task list. */
export function taskStatusTone(status: WorkTaskStatus): ToneKey {
  switch (status) {
    case 'Completed':
      return 'success';
    case 'Blocked':
      return 'danger';
    case 'InProgress':
      return 'info';
    default:
      return 'neutral';
  }
}
