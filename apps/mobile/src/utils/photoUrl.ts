import { appConfig } from '../config';

export function resolvePhotoUrl(url?: string | null): string | null {
  if (!url) return null;
  if (/^https?:\/\//i.test(url) || url.startsWith('file:')) return url;
  if (url.startsWith('/')) return `${appConfig.apiBaseUrl}${url}`;
  return url;
}

/** Only inventory-media endpoints on our configured API receive operator credentials. */
export function isPrivateInventoryPhotoUrl(url: string): boolean {
  try {
    const target = new URL(resolvePhotoUrl(url) ?? url);
    const api = new URL(appConfig.apiBaseUrl);
    return target.origin === api.origin
      && /^\/api\/inventory\/[0-9a-f-]{36}\/photos\/[0-9a-f-]{36}\/(thumbnail|original)$/i.test(target.pathname);
  } catch {
    return false;
  }
}
