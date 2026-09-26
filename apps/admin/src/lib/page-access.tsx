import type { ReactNode } from "react";
import { NoAccess } from "@/components/feedback/no-access";
import { pageAt } from "@/components/layout/navigation";
import { allows } from "@/lib/permissions";
import { getSession } from "@/lib/session";

/**
 * What to show instead of a page this role cannot open, or null to show the page. A page awaits it before asking the
 * API for anything: its server component runs whatever its layout renders, so a check there would only hide the page
 * after its refused requests had already been made.
 */
export async function refusal(route: string): Promise<ReactNode | null> {
  const page = pageAt(route);
  if (!page?.requires) return null;

  const session = await getSession();
  const granted = session ? new Set(session.permissions ?? []) : null;
  return allows(granted, page.requires) ? null : <NoAccess page={page.name} requires={page.requires} granted={granted} />;
}
