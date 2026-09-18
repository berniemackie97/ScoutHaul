import Link from "next/link";
import { ArrowRight, History } from "lucide-react";
import { Badge, Card } from "@overhaul/ui";
import { ListEmpty, PageHeader } from "@/components/list/list-search";
import { Pager } from "@/components/list/pager";
import { countsSentence, coverage, runStatus, type Tone } from "@/features/imports/import-model";
import { getApiClient } from "@/lib/api";
import { ImportDropZone } from "./import-drop-zone";

export const dynamic = "force-dynamic";

const BADGE: Record<Tone, "success" | "warning" | "destructive" | "info" | "secondary"> = {
  success: "success",
  warning: "warning",
  danger: "destructive",
  info: "info",
  neutral: "secondary",
};

const WHEN = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });

/**
 * Where marketplace exports come in, what is left to match, and every import there has been.
 *
 * This page used to be four upload cards — pick the right one for each eBay export — with no trace
 * of an import beyond its effects. Now there is one drop zone that recognises every supported file
 * by its contents, and a history of every import with what it changed.
 */
export default async function ImportsPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const query = await searchParams;
  const page = Math.max(1, Number(query.page) || 1);

  const api = await getApiClient();
  const [{ data: queues }, { data: history }] = await Promise.all([
    api.GET("/api/imports/queues", {}),
    api.GET("/api/imports/runs", { params: { query: { page, pageSize: 20 } } }),
  ]);

  const runs = history?.items ?? [];

  return (
    <div className="space-y-8">
      <PageHeader
        eyebrow="Data"
        title="Imports"
        description="Bring in the marketplaces' own exports. Anything that matches exactly is recorded for you; only genuine questions reach the queues below. Re-importing a file never duplicates anything."
      />

      <ImportDropZone />

      {queues?.ordersToMatch != null || queues?.listingsToMatch != null ? (
        <section aria-labelledby="queues-heading" className="space-y-3">
          <h2 id="queues-heading" className="text-sm font-semibold">
            Needs your decision
          </h2>
          <div className="grid gap-4 sm:grid-cols-2">
            {queues?.ordersToMatch != null ? (
              <QueueCard
                href="/imports/orders"
                title="Orders to match"
                count={queues.ordersToMatch}
                hint="Imported sales waiting to be matched to inventory; matching records the sale with its exact fees."
              />
            ) : null}
            {queues?.listingsToMatch != null ? (
              <QueueCard
                href="/imports/listings"
                title="Listings to match"
                count={queues.listingsToMatch}
                hint="Imported listings waiting to be linked to an item, or used to create one."
              />
            ) : null}
          </div>
        </section>
      ) : null}

      <section aria-labelledby="history-heading" className="space-y-3">
        <h2 id="history-heading" className="flex items-center gap-2 text-sm font-semibold">
          <History className="text-muted-foreground h-4 w-4" aria-hidden /> Import history
        </h2>

        {runs.length === 0 ? (
          <ListEmpty>No imports yet. Files you import appear here with what each one changed.</ListEmpty>
        ) : (
          <>
            <Card className="overflow-x-auto p-0">
              <table className="w-full text-sm">
                <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
                  <tr>
                    <th className="px-4 py-3 font-medium">When</th>
                    <th className="px-4 py-3 font-medium">Report</th>
                    <th className="px-4 py-3 font-medium">Result</th>
                    <th className="px-4 py-3 font-medium">Changes</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {runs.map((run) => {
                    const status = runStatus(run.status);
                    const period = coverage(run.coversFrom, run.coversTo, run.isCompleteSnapshot ?? false);
                    return (
                      <tr key={run.id} className="hover:bg-muted/30">
                        <td className="text-muted-foreground whitespace-nowrap px-4 py-3 tabular-nums">
                          {run.startedAt ? WHEN.format(new Date(run.startedAt)) : "—"}
                        </td>
                        <td className="p-0">
                          <Link href={`/imports/runs/${run.id}`} className="block px-4 py-3">
                            <span className="font-medium">{run.formatLabel ?? "Unrecognised file"}</span>
                            <span className="text-muted-foreground block truncate text-xs">
                              {[run.fileName, period].filter(Boolean).join(" · ")}
                            </span>
                          </Link>
                        </td>
                        <td className="px-4 py-3">
                          <Badge variant={BADGE[status.tone]}>{status.label}</Badge>
                          {(run.issueCount ?? 0) > 0 ? (
                            <span className="text-muted-foreground ml-2 text-xs">
                              {run.issueCount} note{run.issueCount === 1 ? "" : "s"}
                            </span>
                          ) : null}
                        </td>
                        <td className="text-muted-foreground px-4 py-3 text-xs">{countsSentence(run)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </Card>
            <Pager page={history?.page ?? 1} pageCount={history?.pageCount ?? 1} total={history?.total ?? 0} noun="import" />
          </>
        )}
      </section>
    </div>
  );
}

function QueueCard({ href, title, count, hint }: { href: string; title: string; count: number; hint: string }) {
  return (
    <Link href={href} className="bg-card hover:border-foreground/20 group rounded-xl border p-5 transition-colors">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">{title}</span>
        <ArrowRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" aria-hidden />
      </div>
      <p className="mt-2 text-3xl font-semibold tabular-nums">{count}</p>
      <p className="text-muted-foreground mt-1 text-xs">{hint}</p>
    </Link>
  );
}
