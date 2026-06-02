import { appConfig } from '../config';
import { authStore } from '../services/auth';
import { getActiveOrganizationId } from '../services/workspaceSession';

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  idempotencyKey?: string;
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
    headers['X-ScoutHaul-Organization-Id'] = activeOrganizationId;
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
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(parseErrorMessage(text) || `Request failed with ${response.status}`);
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
