import { ApiError, REQUEST_IN_FLIGHT } from '../api/apiError';

/**
 * Whether a failed write is worth keeping for later.
 *
 * Anything that could not reach the server, or that the server blamed on itself, will plausibly
 * succeed on a later attempt. A rejection — bad data, no permission, not signed in — will fail
 * identically every time, so queuing it only builds a backlog that can never drain while hiding the
 * real problem from the user.
 */
export function isWorthQueueing(error: unknown): boolean {
  if (!(error instanceof ApiError)) return false;

  switch (error.reason) {
    case 'offline':
    case 'network':
    case 'timeout':
      return true;
    case 'unauthenticated':
      return false;
    case 'http':
      // The server already has this exact write in flight — ours is the retry that overtook it.
      // Keep it queued and ask again rather than giving up on work that is about to succeed.
      if (error.code === REQUEST_IN_FLIGHT) return true;
      return error.status === 408 || error.status === 429 || (error.status ?? 0) >= 500;
  }
}
