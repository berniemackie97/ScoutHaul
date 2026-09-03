import {
  ApiError,
  apiRequest,
  isRepeatable,
  isTransient,
  parseErrorMessage,
  retryDelayMs,
  uploadMultipart,
} from './client';
import { authStore, refreshAuthSession } from '../services/auth';
import * as Network from 'expo-network';

jest.mock('../config', () => ({ appConfig: { apiBaseUrl: 'https://api.test' } }));
jest.mock('expo-network', () => ({ getNetworkStateAsync: jest.fn() }));
jest.mock('../services/workspaceSession', () => ({
  getActiveOrganizationId: jest.fn(async () => 'org-1'),
}));
jest.mock('../services/auth', () => ({
  authStore: { getToken: jest.fn(), isExpiringWithin: jest.fn(() => false) },
  refreshAuthSession: jest.fn(async () => false),
}));

const mockToken = authStore.getToken as jest.Mock;
const mockExpiring = authStore.isExpiringWithin as jest.Mock;
const mockRefresh = refreshAuthSession as jest.Mock;
const mockNetwork = Network.getNetworkStateAsync as jest.Mock;
const mockFetch = jest.fn();

const jsonResponse = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

/**
 * A fetch that only ever ends by being aborted — the stalled-server case. Mirrors the real thing by
 * rejecting straight away when handed a signal that has already fired.
 */
const stalledFetch = () => (_url: string, init: RequestInit) =>
  new Promise<Response>((_resolve, reject) => {
    const abort = () => reject(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    if (init.signal?.aborted) return abort();
    init.signal?.addEventListener('abort', abort);
  });

/** Asserts the call fails with an ApiError and hands it back, narrowed. */
async function failureOf(call: Promise<unknown>): Promise<ApiError> {
  try {
    await call;
  } catch (error) {
    if (error instanceof ApiError) return error;
    throw error;
  }
  throw new Error('Expected the call to fail, but it resolved.');
}

beforeEach(() => {
  jest.clearAllMocks();
  mockToken.mockReturnValue('token-1');
  mockExpiring.mockReturnValue(false);
  mockRefresh.mockResolvedValue(false);
  mockNetwork.mockResolvedValue({ isInternetReachable: true, isConnected: true });
  global.fetch = mockFetch as unknown as typeof fetch;
});

describe('retry policy', () => {
  it('treats a GET as repeatable and a bare write as not', () => {
    expect(isRepeatable('GET')).toBe(true);
    expect(isRepeatable('POST')).toBe(false);
    expect(isRepeatable('DELETE')).toBe(false);
  });

  it('makes a write repeatable once it carries an idempotency key', () => {
    expect(isRepeatable('POST', 'key-1')).toBe(true);
  });

  it('counts transport failures and the server blaming itself as transient', () => {
    expect(isTransient('timeout', null)).toBe(true);
    expect(isTransient('network', null)).toBe(true);
    expect(isTransient('http', 500)).toBe(true);
    expect(isTransient('http', 503)).toBe(true);
    expect(isTransient('http', 408)).toBe(true);
    expect(isTransient('http', 429)).toBe(true);
  });

  it('does not count the caller being wrong as transient', () => {
    expect(isTransient('http', 400)).toBe(false);
    expect(isTransient('http', 404)).toBe(false);
    expect(isTransient('http', 409)).toBe(false);
    expect(isTransient('unauthenticated', null)).toBe(false);
    expect(isTransient('offline', null)).toBe(false);
  });

  it('backs off further on each attempt', () => {
    expect(retryDelayMs(0, 0)).toBeLessThan(retryDelayMs(1, 0));
    expect(retryDelayMs(1, 0)).toBeLessThan(retryDelayMs(2, 0));
  });
});

describe('parseErrorMessage', () => {
  it('prefers the first validation error', () => {
    const body = JSON.stringify({
      title: 'Bad request',
      detail: 'nope',
      errors: { soldPrice: ['Sold price must be positive.'] },
    });
    expect(parseErrorMessage(body)).toBe('Sold price must be positive.');
  });

  it('falls back to detail, then title', () => {
    expect(parseErrorMessage(JSON.stringify({ title: 'T', detail: 'D' }))).toBe('D');
    expect(parseErrorMessage(JSON.stringify({ title: 'T' }))).toBe('T');
  });

  it('unwraps a bare quoted string', () => {
    expect(parseErrorMessage('"plain text"')).toBe('plain text');
  });
});

describe('apiRequest', () => {
  it('sends the token and the active organization', async () => {
    mockFetch.mockResolvedValue(jsonResponse(200, { ok: true }));
    await apiRequest('/api/inventory');

    const [url, init] = mockFetch.mock.calls[0];
    expect(url).toBe('https://api.test/api/inventory');
    expect(init.headers.Authorization).toBe('Bearer token-1');
    expect(init.headers['X-HaulScout-Organization-Id']).toBe('org-1');
  });

  it('refuses without a token, and says so in a way callers can branch on', async () => {
    mockToken.mockReturnValue(null);
    await expect(apiRequest('/api/inventory')).rejects.toMatchObject({
      reason: 'unauthenticated',
    });
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('refreshes a token that is about to expire before spending it', async () => {
    mockExpiring.mockReturnValue(true);
    mockRefresh.mockResolvedValue(true);
    mockFetch.mockResolvedValue(jsonResponse(200, {}));

    await apiRequest('/api/inventory');
    expect(mockRefresh).toHaveBeenCalledTimes(1);
  });

  it('returns undefined for 204 rather than trying to parse a body', async () => {
    mockFetch.mockResolvedValue(new Response(null, { status: 204 }));
    await expect(apiRequest('/api/inventory/1')).resolves.toBeUndefined();
  });

  it('carries the server machine code through', async () => {
    mockFetch.mockImplementation(async () =>
      jsonResponse(429, { code: 'quota_exhausted', detail: 'No credits' }),
    );

    const error = await failureOf(apiRequest('/api/vision/identify'));
    expect(error.reason).toBe('http');
    expect(error.status).toBe(429);
    expect(error.code).toBe('quota_exhausted');
    expect(error.message).toBe('No credits');
  });

  it('reports being offline distinctly from any other transport failure', async () => {
    mockFetch.mockRejectedValue(new TypeError('Network request failed'));
    mockNetwork.mockResolvedValue({ isInternetReachable: false, isConnected: false });

    await expect(apiRequest('/api/inventory')).rejects.toMatchObject({ reason: 'offline' });
  });

  it('reports a transport failure while online as a network error', async () => {
    mockFetch.mockRejectedValue(new TypeError('Network request failed'));

    await expect(apiRequest('/api/inventory')).rejects.toMatchObject({ reason: 'network' });
  });

  it('gives up on a request that never answers', async () => {
    // Never answers on its own; only the client's deadline can end it.
    mockFetch.mockImplementation(stalledFetch());

    await expect(apiRequest('/api/inventory', { timeoutMs: 20 })).rejects.toMatchObject({
      reason: 'timeout',
    });
  });

  it('retries a GET that the server failed to serve', async () => {
    mockFetch
      .mockResolvedValueOnce(jsonResponse(503, { detail: 'unavailable' }))
      .mockResolvedValueOnce(jsonResponse(200, { items: [] }));

    await expect(apiRequest('/api/inventory')).resolves.toEqual({ items: [] });
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it('never repeats a write without an idempotency key', async () => {
    mockFetch.mockImplementation(async () => jsonResponse(503, { detail: 'unavailable' }));

    await expect(apiRequest('/api/sales', { method: 'POST', body: {} })).rejects.toMatchObject({
      status: 503,
    });
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it('repeats a write that carries an idempotency key', async () => {
    mockFetch
      .mockResolvedValueOnce(jsonResponse(503, { detail: 'unavailable' }))
      .mockResolvedValueOnce(jsonResponse(200, { id: 'sale-1' }));

    await expect(
      apiRequest('/api/sales', { method: 'POST', body: {}, idempotencyKey: 'key-1' }),
    ).resolves.toEqual({ id: 'sale-1' });
    expect(mockFetch).toHaveBeenCalledTimes(2);
    expect(mockFetch.mock.calls[0][1].headers['X-Idempotency-Key']).toBe('key-1');
  });

  it('refreshes once on a 401 and replays with the new token', async () => {
    mockFetch
      .mockResolvedValueOnce(jsonResponse(401, { detail: 'expired' }))
      .mockResolvedValueOnce(jsonResponse(200, { ok: true }));
    mockRefresh.mockImplementation(async () => {
      mockToken.mockReturnValue('token-2');
      return true;
    });

    await expect(apiRequest('/api/inventory')).resolves.toEqual({ ok: true });
    expect(mockRefresh).toHaveBeenCalledTimes(1);
    expect(mockFetch.mock.calls[1][1].headers.Authorization).toBe('Bearer token-2');
  });

  it('asks the user to sign in when the refresh cannot save the session', async () => {
    mockFetch.mockImplementation(async () => jsonResponse(401, { detail: 'expired' }));
    mockRefresh.mockResolvedValue(false);

    await expect(apiRequest('/api/inventory')).rejects.toMatchObject({
      reason: 'unauthenticated',
    });
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it('lets a caller cancel without it looking like a timeout', async () => {
    const controller = new AbortController();
    mockFetch.mockImplementation(stalledFetch());

    const pending = apiRequest('/api/inventory', { signal: controller.signal });
    controller.abort();

    await expect(pending).rejects.not.toBeInstanceOf(ApiError);
  });
});

describe('uploadMultipart', () => {
  it('fails the same typed way as every other call', async () => {
    mockFetch.mockResolvedValue(jsonResponse(413, { code: 'too_large', detail: 'Photo too large' }));

    const error = await failureOf(uploadMultipart('/api/inventory/1/photos', new FormData()));
    expect(error.code).toBe('too_large');
    expect(error.message).toBe('Photo too large');
  });

  it('does not set a JSON content type, so the boundary survives', async () => {
    mockFetch.mockResolvedValue(jsonResponse(200, {}));
    await uploadMultipart('/api/inventory/1/photos', new FormData());

    expect(mockFetch.mock.calls[0][1].headers['Content-Type']).toBeUndefined();
  });

  it('never repeats an upload without an idempotency key', async () => {
    mockFetch.mockImplementation(async () => jsonResponse(500, { detail: 'boom' }));

    await expect(uploadMultipart('/api/inventory/1/photos', new FormData())).rejects.toMatchObject({
      status: 500,
    });
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });
});
