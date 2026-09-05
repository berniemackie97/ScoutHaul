/**
 * Why a request failed, as something the caller can branch on.
 *
 * Screens used to tell these apart by reading the message text — `msg.includes('Sign in')` for auth,
 * `/network/i` for a dead connection — which breaks the moment a message is reworded and cannot see
 * the difference between "no signal" and "the server took too long".
 */
export type ApiFailureReason =
  /** No usable token, and refreshing did not get one. */
  | 'unauthenticated'
  /** The device says it has no connectivity. */
  | 'offline'
  /** We gave up waiting for a reply. */
  | 'timeout'
  /** The request never reached the server for some other transport reason. */
  | 'network'
  /** The server answered, with a non-2xx status. */
  | 'http';

/** Thrown by every call in this module. Extends Error, so `catch (e) { e.message }` still works. */
export class ApiError extends Error {
  readonly reason: ApiFailureReason;
  /** The HTTP status, or null when the request never got an answer. */
  readonly status: number | null;
  /** The server's machine code (e.g. "quota_exhausted"), when it sent one. */
  readonly code: string | null;

  constructor(init: {
    reason: ApiFailureReason;
    message: string;
    status?: number | null;
    code?: string | null;
  }) {
    super(init.message);
    this.name = 'ApiError';
    this.reason = init.reason;
    this.status = init.status ?? null;
    this.code = init.code ?? null;
  }
}

/**
 * The server's code for "a request carrying this key is already running".
 *
 * A 409 normally means the state will not allow this and never will. This one is the exception: our
 * retry overtook the original, and asking again shortly is the right move.
 */
export const REQUEST_IN_FLIGHT = 'request_in_flight';
