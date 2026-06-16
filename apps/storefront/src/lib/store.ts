import { createApiClient } from "@overhaul/api-client";

export const STORE = {
  name: "Overhaul Trading Company",
  /** This store's organization id, sent as X-ScoutHaul-Store on public reads. */
  orgId: process.env.NEXT_PUBLIC_STORE_ORG_ID ?? "",
  apiBaseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5080",
};

export const liveCatalogEnabled = Boolean(STORE.orgId);

/** Typed public storefront client (anonymous, store resolved by header). */
export function storefrontApi() {
  return createApiClient({ baseUrl: STORE.apiBaseUrl, storeId: STORE.orgId });
}
