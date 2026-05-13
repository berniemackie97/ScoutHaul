import { appConfig } from '../config';

export function resolvePhotoUrl(url?: string | null): string | null {
  if (!url) return null;
  if (/^https?:\/\//i.test(url) || url.startsWith('file:')) return url;
  if (url.startsWith('/')) return `${appConfig.apiBaseUrl}${url}`;
  return url;
}
