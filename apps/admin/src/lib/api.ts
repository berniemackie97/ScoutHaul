import { createApiClient, type HaulScoutClient } from "@overhaul/api-client";
import { createSupabaseServerClient } from "@/lib/supabase/server";

/**
 * Returns a typed HaulScout API client authenticated as the current operator.
 * Use from Server Components / Route Handlers / Server Actions. The Supabase
 * access token is attached on every request; the .NET API resolves the operator's
 * organization from the token, so no org header is required here.
 */
export async function getApiClient(): Promise<HaulScoutClient> {
  const supabase = await createSupabaseServerClient();

  return createApiClient({
    baseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5080",
    getAccessToken: async () => {
      const {
        data: { session },
      } = await supabase.auth.getSession();
      return session?.access_token ?? null;
    },
  });
}
