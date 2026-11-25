import { CommandCenter } from "@/features/console/command-center";
import { getApiClient } from "@/lib/api";
import { getSession, operatorFrom } from "@/lib/session";

export const dynamic = "force-dynamic";

/**
 * The home page. It fetches the operator console and hands it to {@link CommandCenter}.
 *
 * This page used to be four figures and two links, and several of those figures disagreed with the
 * pages they linked to. Everything now comes from one console endpoint whose every count is computed
 * by the same query as the list it opens — so a card that says 12 opens 12 rows.
 */
export default async function CommandCenterPage() {
  const api = await getApiClient();
  const [session, { data: overview, error }, { data: work }] = await Promise.all([
    getSession(),
    api.GET("/api/dashboard/console", {}),
    api.GET("/api/work", {}),
  ]);

  // Throw rather than render zeros: a home page that reads "nothing to do" because the request
  // failed is worse than one that says it failed. The segment's boundary keeps the shell alive.
  if (!overview) throw new Error(`The operator console could not be loaded (${JSON.stringify(error ?? "no response")}).`);

  return <CommandCenter overview={overview} work={work ?? null} operator={operatorFrom(session)} />;
}
