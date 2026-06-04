import type { ToneKey } from '../components/theme';

/**
 * Maps inventory status to a display tone. Single source of truth —
 * used by inventory list, scout screen, and any future status display.
 */
export function statusTone(status: string): ToneKey {
  switch (status) {
    case 'Listed':
      return 'info';
    case 'Sold':
    case 'Shipped':
    case 'Delivered':
    case 'Packed':
      return 'success';
    case 'Researching':
    case 'NeedsCleaning':
    case 'NeedsTesting':
    case 'NeedsPhotos':
    case 'ReadyToList':
      return 'warning';
    case 'Purchased':
      return 'info';
    case 'Returned':
    case 'Disposed':
      return 'danger';
    case 'Draft':
    case 'Watchlist':
    default:
      return 'neutral';
  }
}
