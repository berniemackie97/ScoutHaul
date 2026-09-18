import { cache } from "react";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

export type SessionBootstrap = components["schemas"]["SessionBootstrapDto"];

/**
 * Who is signed in and which organization they are working in — fetched once per request.
 *
 * The layout needs it for the sidebar and the home page needs it for its header. Wrapped in React's
 * cache so both read the same answer from one API call rather than each asking separately.
 */
export const getSession = cache(async (): Promise<SessionBootstrap | null> => {
  const api = await getApiClient();
  const { data } = await api.GET("/api/session/bootstrap", {});
  return data ?? null;
});

/** The operator as the shell shows them: a name, an address, and the organization in use. */
export type Operator = { name: string; email: string | null; organization: string | null; initials: string };

export function operatorFrom(session: SessionBootstrap | null): Operator | null {
  const user = session?.user;
  if (!user) return null;

  const organization =
    session?.organizations?.find((entry) => entry.id === session.selectedOrganizationId) ??
    session?.organizations?.[0];
  const name = user.displayName?.trim() || user.email?.trim() || "Signed in";

  return { name, email: user.email ?? null, organization: organization?.name ?? null, initials: initialsOf(name) };
}

/** "Dana Brooks" → "DB"; "dana@example.com" → "D". Never empty. */
export function initialsOf(name: string): string {
  const words = name.replace(/@.*$/, "").split(/[\s._-]+/).filter(Boolean);
  const initials = words.slice(0, 2).map((word) => word[0]!.toUpperCase()).join("");
  return initials || "?";
}
