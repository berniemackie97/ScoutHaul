import createClient, { type Client } from "openapi-fetch";
import type { paths } from "./schema";

/** Fully-typed ScoutHaul API client (generated from the .NET OpenAPI document). */
export type ScoutHaulClient = Client<paths>;

export interface CreateApiClientOptions {
  /** Base URL of the ScoutHaul API, e.g. https://api.example.com or http://localhost:5080 */
  baseUrl: string;
  /**
   * Returns the bearer token (Supabase access token) for the current request/session.
   * Called on every request so token refresh is transparent. Return null/undefined for
   * anonymous calls (e.g. public storefront reads).
   */
  getAccessToken?: () => string | null | undefined | Promise<string | null | undefined>;
  /** Optional organization id, sent as the X-ScoutHaul-Organization-Id header (operator calls). */
  organizationId?: string;
  /** Optional store organization id, sent as the X-ScoutHaul-Store header (public storefront reads). */
  storeId?: string;
  /** Extra fetch implementation (e.g. Next.js server fetch with caching options). */
  fetch?: typeof fetch;
}

/**
 * Creates a typed client for the ScoutHaul API. Attaches the Supabase bearer token and
 * optional organization header to every request.
 */
export function createApiClient(options: CreateApiClientOptions): ScoutHaulClient {
  const client = createClient<paths>({
    baseUrl: options.baseUrl,
    ...(options.fetch ? { fetch: options.fetch } : {}),
  });

  client.use({
    async onRequest({ request }) {
      const token = await options.getAccessToken?.();
      if (token) {
        request.headers.set("Authorization", `Bearer ${token}`);
      }
      if (options.organizationId) {
        request.headers.set("X-ScoutHaul-Organization-Id", options.organizationId);
      }
      if (options.storeId) {
        request.headers.set("X-ScoutHaul-Store", options.storeId);
      }
      return request;
    },
  });

  return client;
}

export type { paths, components, operations } from "./schema";
