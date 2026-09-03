import * as Network from 'expo-network';
import { appConfig } from '../config';
import { authStore, refreshAuthSession } from '../services/auth';
import { getActiveOrganizationId } from '../services/workspaceSession';

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
 * This app is used in thrift stores, warehouses and estate sales, where the connection drops
 * constantly. Without a deadline React Native's fetch waits forever, so a stalled request left the
 * user staring at a spinner until they force-quit. Uploads get longer because a photo on a weak
 * connection legitimately takes a while.
 */
export const REQUEST_TIMEOUT_MS = 15_000;
export const UPLOAD_TIMEOUT_MS = 60_000;

const MAX_RETRIES = 2;
const RETRY_BASE_DELAY_MS = 300;

/** A token this close to expiry is refreshed before use rather than spent on a request that 401s. */
const REFRESH_LEEWAY_MS = 60_000;

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  idempotencyKey?: string;
  signal?: AbortSignal;
  /** Overrides the default deadline. */
  timeoutMs?: number;
};

/** The transport knobs a caller can pass through any endpoint wrapper. */
export type CallOptions = Pick<RequestOptions, 'signal' | 'timeoutMs'>;

// ── Retry policy ──────────────────────────────────────────────────────────────

/**
 * Whether repeating this request is safe.
 *
 * A GET changes nothing, so it can always be repeated. Anything else can only be repeated when it
 * carries an idempotency key, because a retried POST that actually reached the server would
 * otherwise record a second sale or a second purchase.
 */
export function isRepeatable(method: string, idempotencyKey?: string): boolean {
  return method === 'GET' || Boolean(idempotencyKey);
}

/** Whether the failure is the kind that a later attempt might get past. */
export function isTransient(reason: ApiFailureReason, status: number | null): boolean {
  if (reason === 'timeout' || reason === 'network') return true;
  if (reason !== 'http' || status === null) return false;
  // 408 request timeout, 429 too many requests, and anything the server calls its own fault.
  return status === 408 || status === 429 || status >= 500;
}

/** Exponential backoff. The jitter keeps a screen that fires several calls at once from lock-stepping. */
export function retryDelayMs(attempt: number, jitter = Math.random()): number {
  return RETRY_BASE_DELAY_MS * 3 ** attempt + Math.round(jitter * RETRY_BASE_DELAY_MS);
}

// ── Error shaping ─────────────────────────────────────────────────────────────

/** Pulls the most specific message the server offered out of an error body. */
export function parseErrorMessage(text: string): string {
  if (!text) return '';

  try {
    const parsed = JSON.parse(text) as
      | string
      | { title?: string; detail?: string; errors?: Record<string, string[]> };

    // `return BadRequest("Some message")` sends a bare JSON string, which parses cleanly — so
    // without this the message reached the user still wrapped in its quotes.
    if (typeof parsed === 'string') return parsed;

    const validationErrors = parsed.errors ? Object.values(parsed.errors).flat() : [];
    return validationErrors[0] ?? parsed.detail ?? parsed.title ?? text;
  } catch {
    return text.replace(/^"|"$/g, '');
  }
}

function parseErrorCode(text: string): string | null {
  try {
    return (JSON.parse(text) as { code?: string }).code ?? null;
  } catch {
    return null;
  }
}

/**
 * Turns a thrown fetch failure into a reason. The device's connectivity is only consulted here,
 * after something has already gone wrong — checking before every request would add a round trip to
 * the OS on the happy path, and the flag is not reliable enough to gate on anyway.
 */
async function classifyTransportFailure(error: unknown, timedOut: boolean): Promise<ApiError> {
  if (timedOut) {
    return new ApiError({
      reason: 'timeout',
      message: 'The server took too long to respond. Check your connection and try again.',
    });
  }

  if (!(await isOnline())) {
    return new ApiError({
      reason: 'offline',
      message: "You're offline. This will work again once you have a connection.",
    });
  }

  return new ApiError({
    reason: 'network',
    message: error instanceof Error ? error.message : 'The request could not be sent.',
  });
}

async function isOnline(): Promise<boolean> {
  try {
    const state = await Network.getNetworkStateAsync();
    return Boolean(state.isInternetReachable ?? state.isConnected ?? true);
  } catch {
    // If we cannot tell, assume we are online so the real transport error survives.
    return true;
  }
}

// ── Request plumbing ──────────────────────────────────────────────────────────

async function buildHeaders(includeContentType = true): Promise<Record<string, string>> {
  const headers: Record<string, string> = { Accept: 'application/json' };

  if (includeContentType) {
    headers['Content-Type'] = 'application/json';
  }

  const token = authStore.getToken();
  const activeOrganizationId = await getActiveOrganizationId();

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  if (activeOrganizationId) {
    headers['X-HaulScout-Organization-Id'] = activeOrganizationId;
  }

  return headers;
}

const unauthenticated = () =>
  new ApiError({ reason: 'unauthenticated', message: 'Sign in to access this feature.' });

/**
 * Makes sure there is a token worth sending. Supabase refreshes on its own timer, but a phone that
 * has been asleep wakes with an expired token and the first request after that would 401 for no
 * reason the user can act on.
 */
async function ensureFreshToken(): Promise<void> {
  if (!authStore.getToken()) throw unauthenticated();
  if (!authStore.isExpiringWithin(REFRESH_LEEWAY_MS)) return;

  await refreshAuthSession();
  if (!authStore.getToken()) throw unauthenticated();
}

/** Runs `send` under a deadline, honouring a caller's own abort signal at the same time. */
async function withDeadline(
  timeoutMs: number,
  callerSignal: AbortSignal | undefined,
  send: (signal: AbortSignal) => Promise<Response>,
): Promise<Response> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  const abortFromCaller = () => controller.abort();

  // The caller may already have given up while we were fetching the token and headers; adding the
  // listener at that point would never fire, leaving the request to run out its full deadline.
  if (callerSignal?.aborted) controller.abort();
  else callerSignal?.addEventListener('abort', abortFromCaller);

  try {
    return await send(controller.signal);
  } catch (error) {
    // A caller who aborted deliberately gets their own cancellation back, not a timeout.
    if (controller.signal.aborted && !callerSignal?.aborted) {
      throw new ApiError({
        reason: 'timeout',
        message: 'The server took too long to respond. Check your connection and try again.',
      });
    }
    throw error;
  } finally {
    clearTimeout(timer);
    callerSignal?.removeEventListener('abort', abortFromCaller);
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  const text = await response.text();
  return new ApiError({
    reason: 'http',
    status: response.status,
    code: parseErrorCode(text),
    message: parseErrorMessage(text) || `Request failed with ${response.status}`,
  });
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * One attempt against the API, with the deadline applied and the response turned into either the
 * parsed body or an ApiError. Retrying and refreshing are the caller's job.
 */
async function attempt<T>(
  path: string,
  init: RequestInit,
  timeoutMs: number,
  callerSignal: AbortSignal | undefined,
): Promise<T> {
  let response: Response;
  try {
    response = await withDeadline(timeoutMs, callerSignal, (signal) =>
      fetch(`${appConfig.apiBaseUrl}${path}`, { ...init, signal }),
    );
  } catch (error) {
    if (error instanceof ApiError) throw error;
    if (callerSignal?.aborted) throw error; // a deliberate cancellation, not a failure
    throw await classifyTransportFailure(error, false);
  }

  if (!response.ok) throw await toApiError(response);
  if (response.status === 204) return undefined as T;

  return (await response.json()) as T;
}

async function send<T>(
  path: string,
  init: RequestInit,
  options: { timeoutMs: number; signal?: AbortSignal; repeatable: boolean },
  rebuildHeaders: () => Promise<Record<string, string>>,
): Promise<T> {
  let refreshed = false;

  for (let attemptNumber = 0; ; attemptNumber += 1) {
    try {
      return await attempt<T>(path, init, options.timeoutMs, options.signal);
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;

      // A 401 usually means the token aged out mid-session. Refresh once and try again; if that
      // still fails, the session is genuinely gone and the user has to sign in.
      if (error.status === 401 && !refreshed) {
        refreshed = true;
        if (await refreshAuthSession()) {
          init = { ...init, headers: await rebuildHeaders() };
          continue;
        }
        throw unauthenticated();
      }

      const canRetry =
        options.repeatable &&
        attemptNumber < MAX_RETRIES &&
        isTransient(error.reason, error.status) &&
        !options.signal?.aborted;

      if (!canRetry) throw error;

      await sleep(retryDelayMs(attemptNumber));
    }
  }
}

// ── Public API ────────────────────────────────────────────────────────────────

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  await ensureFreshToken();

  const method = options.method ?? 'GET';
  const rebuildHeaders = async () => ({
    ...(await buildHeaders()),
    ...(options.idempotencyKey ? { 'X-Idempotency-Key': options.idempotencyKey } : {}),
  });

  return send<T>(
    path,
    {
      method,
      headers: await rebuildHeaders(),
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    },
    {
      timeoutMs: options.timeoutMs ?? REQUEST_TIMEOUT_MS,
      signal: options.signal,
      repeatable: isRepeatable(method, options.idempotencyKey),
    },
    rebuildHeaders,
  );
}

export async function uploadMultipart<T>(
  path: string,
  formData: FormData,
  idempotencyKey?: string,
  options: { signal?: AbortSignal; timeoutMs?: number } = {},
): Promise<T> {
  await ensureFreshToken();

  const rebuildHeaders = async () => ({
    ...(await buildHeaders(false)),
    ...(idempotencyKey ? { 'X-Idempotency-Key': idempotencyKey } : {}),
  });

  return send<T>(
    path,
    { method: 'POST', headers: await rebuildHeaders(), body: formData },
    {
      timeoutMs: options.timeoutMs ?? UPLOAD_TIMEOUT_MS,
      signal: options.signal,
      // A repeated upload without a key would post the photo twice.
      repeatable: isRepeatable('POST', idempotencyKey),
    },
    rebuildHeaders,
  );
}
