import { ApiError } from '../api/apiError';
import { isWorthQueueing } from '../services/offlineWrites';

/**
 * How many times a queued write is retried before the queue gives up on it.
 *
 * Not a magic number so much as a bound: without one, a row the server will never accept is retried
 * on every reconnect for the life of the install, and the count of "pending" work never comes down.
 */
export const MAX_ATTEMPTS = 5;

const BASE_BACKOFF_MS = 30_000;
const MAX_BACKOFF_MS = 6 * 60 * 60 * 1000;

/** What to do with a queued write after an attempt failed. */
export type FailureVerdict =
  /** Nothing was wrong with the connection — stop and leave the whole queue for later. */
  | { action: 'pause' }
  /** Worth another go, after a wait. */
  | { action: 'retry'; attempts: number; nextAttemptAt: Date }
  /** It will fail the same way every time, or it already has often enough. */
  | { action: 'give-up'; attempts: number; reason: string };

/**
 * Waits longer after each attempt, capped.
 *
 * The queue drains on every reconnect, so without a wait a failing row is retried the moment the
 * signal flickers — which on a phone moving through a building is constantly.
 */
export function backoffMs(attempts: number): number {
  return Math.min(BASE_BACKOFF_MS * 2 ** Math.max(0, attempts - 1), MAX_BACKOFF_MS);
}

/**
 * Decides a queued write's fate from the error it hit and how many times it has already been tried.
 *
 * The distinction that matters: a write that could not reach the server is worth keeping, while one
 * the server rejected — bad data, no permission, gone — will be rejected identically for ever, and
 * retrying it only hides the problem behind a number that never goes down.
 */
export function classifyFailure(
  error: unknown,
  previousAttempts: number,
  now: Date = new Date(),
): FailureVerdict {
  // Being offline is not this row's fault; the queue exists for exactly this.
  if (error instanceof ApiError && error.reason === 'offline') {
    return { action: 'pause' };
  }

  const attempts = previousAttempts + 1;
  const reason = error instanceof Error ? error.message : 'Unknown sync error';

  if (!isWorthQueueing(error)) {
    return { action: 'give-up', attempts, reason };
  }

  if (attempts >= MAX_ATTEMPTS) {
    return { action: 'give-up', attempts, reason: `${reason} (gave up after ${attempts} attempts)` };
  }

  return {
    action: 'retry',
    attempts,
    nextAttemptAt: new Date(now.getTime() + backoffMs(attempts)),
  };
}
