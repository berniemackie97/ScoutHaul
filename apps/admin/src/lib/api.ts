import { createApiClient, type HaulScoutClient } from "@overhaul/api-client";
import { createSupabaseServerClient } from "@/lib/supabase/server";

const apiBaseUrl = () =>
  (process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5080").replace(/\/$/, "");

export async function getApiAccessToken(): Promise<string | null> {
  const supabase = await createSupabaseServerClient();
  const {
    data: { session },
  } = await supabase.auth.getSession();
  return session?.access_token ?? null;
}

export function getApiAssetUrl(path: string | null | undefined): string | null {
  if (!path) return null;
  if (/^https?:\/\//i.test(path)) return path;
  return `${apiBaseUrl()}${path.startsWith("/") ? path : `/${path}`}`;
}

export function getApiBaseUrl(): string {
  return apiBaseUrl();
}

/**
 * Returns a typed HaulScout API client authenticated as the current operator.
 * Use from Server Components / Route Handlers / Server Actions. The Supabase
 * access token is attached on every request; the .NET API resolves the operator's
 * organization from the token, so no org header is required here.
 */
export async function getApiClient(): Promise<HaulScoutClient> {
  return createApiClient({
    baseUrl: apiBaseUrl(),
    getAccessToken: getApiAccessToken,
  });
}
