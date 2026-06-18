import { appConfig } from '../config';
import { authStore } from '../services/auth';
import { getActiveOrganizationId } from '../services/workspaceSession';

// Thrown on non-2xx responses. Carries the HTTP status + the server's machine `code` (e.g.
// "quota_exhausted") so callers can show a specific message/CTA. Extends Error → existing
// `catch (e) { e.message }` callers keep working unchanged.
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string | null,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  idempotencyKey?: string;
  signal?: AbortSignal;
};

async function buildHeaders(includeContentType = true): Promise<Record<string, string>> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
  };

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

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  if (!authStore.getToken()) {
    throw new Error('Sign in to access this feature.');
  }

  const response = await fetch(`${appConfig.apiBaseUrl}${path}`, {
    method: options.method ?? 'GET',
    headers: {
      ...(await buildHeaders()),
      ...(options.idempotencyKey ? { 'X-Idempotency-Key': options.idempotencyKey } : {}),
    },
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options.signal,
  });

  if (!response.ok) {
    const text = await response.text();
    let code: string | null = null;
    try { code = (JSON.parse(text) as { code?: string }).code ?? null; } catch { /* body not JSON */ }
    throw new ApiError(response.status, code, parseErrorMessage(text) || `Request failed with ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

function parseErrorMessage(text: string): string {
  if (!text) {
    return '';
  }

  try {
    const parsed = JSON.parse(text) as { title?: string; detail?: string; errors?: Record<string, string[]> };
    const validationErrors = parsed.errors ? Object.values(parsed.errors).flat() : [];
    return validationErrors[0] ?? parsed.detail ?? parsed.title ?? text;
  } catch {
    return text.replace(/^"|"$/g, '');
  }
}

export async function uploadMultipart<T>(path: string, formData: FormData, idempotencyKey?: string): Promise<T> {
  const response = await fetch(`${appConfig.apiBaseUrl}${path}`, {
    method: 'POST',
    headers: {
      ...(await buildHeaders(false)),
      ...(idempotencyKey ? { 'X-Idempotency-Key': idempotencyKey } : {}),
    },
    body: formData,
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(parseErrorMessage(text) || `Request failed with ${response.status}`);
  }

  return (await response.json()) as T;
}
