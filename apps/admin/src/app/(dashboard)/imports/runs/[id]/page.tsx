import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, Download } from "lucide-react";
import { Badge, Card } from "@overhaul/ui";
import { StatCard, StatRow } from "@/components/list/stat-card";
import { coverage, runStatus, type Tone } from "@/features/imports/import-model";
import { getApiClient } from "@/lib/api";
import { RerunButton } from "./rerun-button";

export const dynamic = "force-dynamic";

const BADGE: Record<Tone, "success" | "warning" | "destructive" | "info" | "secondary"> = {
  success: "success",
  warning: "warning",
  danger: "destructive",
  info: "info",
  neutral: "secondary",
};

const STAMP = new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" });

/** One import: what it was, what it changed, which rows it could not use, and its exact file. */
export default async function ImportRunPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const api = await getApiClient();
  const { data } = await api.GET("/api/imports/runs/{id}", { params: { path: { id } } });
  if (!data?.run) notFound();

  const run = data.run;
  const issues = data.issues ?? [];
  const status = runStatus(run.status);
  const period = coverage(run.coversFrom, run.coversTo, run.isCompleteSnapshot ?? false);

  return (
    <div className="space-y-6">
      <Link href="/imports" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm">
        <ArrowLeft className="h-4 w-4" aria-hidden /> Imports
      </Link>

      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
        <div>
          <p className="text-muted-foreground text-sm">{run.platform}</p>
          <h1 className="mt-1 flex flex-wrap items-center gap-3 text-2xl font-semibold tracking-tight">
            {run.formatLabel ?? "Unrecognised file"}
            <Badge variant={BADGE[status.tone]}>{status.label}</Badge>
          </h1>
          {run.summary ? <p className="text-muted-foreground mt-1 max-w-3xl text-sm">{run.summary}</p> : null}
        </div>
        <div className="flex shrink-0 flex-wrap items-start gap-2">
          {run.fileName ? (
            <a
              href={`/api/imports/runs/${run.id}/file`}
              className="hover:bg-muted inline-flex h-10 items-center gap-2 rounded-lg border px-4 text-sm font-medium"
            >
              <Download className="h-4 w-4" aria-hidden /> Download file
            </a>
          ) : null}
          {run.canRerun && run.id ? <RerunButton runId={run.id} /> : null}
        </div>
      </header>

      <StatRow columns={4}>
        <StatCard label="New" value={String(run.created ?? 0)} />
        <StatCard label="Updated" value={String(run.updated ?? 0)} />
        <StatCard label="Unchanged" value={String(run.unchanged ?? 0)} />
        <StatCard label="Rejected rows" value={String(run.rejected ?? 0)} tone={(run.rejected ?? 0) > 0 ? "warning" : "default"} />
      </StatRow>

      <Card className="p-5">
        <dl className="grid gap-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
          <Fact label="File" value={run.fileName ?? "—"} />
          <Fact label="Covers" value={period ?? "—"} />
          <Fact label="Started" value={run.startedAt ? STAMP.format(new Date(run.startedAt)) : "—"} />
          <Fact label="Finished" value={run.finishedAt ? STAMP.format(new Date(run.finishedAt)) : "Still running"} />
        </dl>
        {run.rerunOfId ? (
          <p className="text-muted-foreground mt-4 text-xs">
            A re-run of{" "}
            <Link href={`/imports/runs/${run.rerunOfId}`} className="text-brand-600 hover:underline">
              an earlier import
            </Link>{" "}
            of the same file.
          </p>
        ) : null}
      </Card>

      <section aria-labelledby="issues-heading" className="space-y-3">
        <h2 id="issues-heading" className="text-sm font-semibold">
          Notes and skipped rows
        </h2>
        {issues.length === 0 ? (
          <p className="text-muted-foreground text-sm">Every row was used. Nothing to note.</p>
        ) : (
          <Card className="overflow-x-auto p-0">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
                <tr>
                  <th className="px-4 py-3 font-medium">Row</th>
                  <th className="px-4 py-3 font-medium">Kind</th>
                  <th className="px-4 py-3 font-medium">Record</th>
                  <th className="px-4 py-3 font-medium">What happened</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {issues.map((issue, index) => (
                  <tr key={index}>
                    <td className="text-muted-foreground px-4 py-3 tabular-nums">{issue.rowNumber ?? "—"}</td>
                    <td className="px-4 py-3">
                      <Badge variant={issue.severity === "Error" ? "destructive" : "warning"}>
                        {issue.severity === "Error" ? "Skipped" : "Note"}
                      </Badge>
                    </td>
                    <td className="text-muted-foreground max-w-[16rem] truncate px-4 py-3">{issue.externalReference ?? "—"}</td>
                    <td className="px-4 py-3">{issue.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        )}
      </section>
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-muted-foreground text-xs uppercase tracking-wide">{label}</dt>
      <dd className="mt-1 truncate font-medium">{value}</dd>
    </div>
  );
}
