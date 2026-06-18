import { createApiClient } from "@overhaul/api-client";

export const STORE = {
  name: "Overhaul Trading Company",
  /**
   * This store's public domain. Sent as X-ScoutHaul-Store; the API resolves the
   * organization that owns this domain — no hardcoded org id anywhere.
   */
  domain: process.env.NEXT_PUBLIC_STORE_DOMAIN ?? "overhaultrading.com",
  apiBaseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? "https://scouthaul-production.up.railway.app",
};

export const liveCatalogEnabled = Boolean(STORE.domain);

/** Typed public storefront client (anonymous; store resolved by domain header). */
export function storefrontApi() {
  return createApiClient({ baseUrl: STORE.apiBaseUrl, storeId: STORE.domain });
}
