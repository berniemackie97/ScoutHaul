"use client";

import { useState, useTransition } from "react";
import { AlertTriangle, CheckCircle2, RotateCcw } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, Notice } from "@overhaul/ui";
import { When } from "@/components/when";
import { safely } from "@/lib/safe-action";
import { loadIssueSnapshot, resolveIssue, resolveIssueGroup, retryIssue, type IssueActionResult } from "./issue-actions";

type IssueGroup = components["schemas"]["SyncIssueGroupDto"];
type Issue = components["schemas"]["SyncIssueDto"];

const groupKey = (group: IssueGroup) => `${group.platform}:${group.code}`;

/**
 * Open sync issues, grouped by marketplace and kind. Each opens to what happened, where, how often it was tried, what
 * was kept and what fixes it — and closes by a person or by a later scan that proves it gone.
 */
export function SyncIssues({ groups }: { groups: IssueGroup[] }) {
  const [selected, setSelected] = useState<{ group: string; issue: string } | null>(() =>
    groups[0]?.issues?.[0] ? { group: groupKey(groups[0]), issue: groups[0].issues[0].id! } : null,
  );
  const [result, setResult] = useState<IssueActionResult | null>(null);
  const [pending, startTransition] = useTransition();
  const group = groups.find((entry) => groupKey(entry) === selected?.group);
  const issue = group?.issues?.find((entry) => entry.id === selected?.issue);

  if (groups.length === 0) {
    return (
      <Notice tone="success" icon={<CheckCircle2 className="h-4 w-4" />} title="No open sync issues">
        Anything a scan cannot read is listed here, with what was kept and how to fix it.
      </Notice>
    );
  }

  const run = (action: () => Promise<IssueActionResult>) =>
    startTransition(async () => {
      setResult(await safely(action));
    });

  return (
    <section aria-labelledby="sync-issues" className="space-y-3">
      <div className="flex flex-wrap items-baseline gap-2">
        <h2 id="sync-issues" className="text-lg font-semibold">Open issues</h2>
        <span className="text-muted-foreground text-sm">
          {groups.reduce((total, entry) => total + (entry.count ?? 0), 0)} across {groups.length} {groups.length === 1 ? "kind" : "kinds"} · one fix clears a group
        </span>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
        <Card className="divide-y overflow-hidden p-0">
          {groups.map((entry) => (
            <div key={groupKey(entry)}>
              <div className="bg-muted/40 flex flex-wrap items-center gap-2 px-4 py-2.5">
                <Badge variant={entry.severity === "Error" ? "destructive" : "warning"}>{entry.severity === "Error" ? "Blocked" : "Partial"}</Badge>
                <span className="font-medium">{entry.title}</span>
                <span className="text-muted-foreground text-sm">· {entry.platform} · ×{entry.count}</span>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="ml-auto"
                  disabled={pending}
                  onClick={() => run(() => resolveIssueGroup(entry.platform ?? "", entry.code ?? "", false))}
                >
                  Resolve all
                </Button>
              </div>
              <ul>
                {(entry.issues ?? []).map((item) => {
                  const active = item.id === selected?.issue;
                  return (
                    <li key={item.id}>
                      <button
                        type="button"
                        aria-current={active}
                        onClick={() => setSelected({ group: groupKey(entry), issue: item.id! })}
                        className={`w-full px-4 py-3 text-left text-sm transition-colors ${active ? "bg-copper/10" : "hover:bg-muted/50"}`}
                      >
                        <span className="block truncate font-medium">{item.externalReference ?? item.message}</span>
                        <span className="text-muted-foreground block truncate text-xs">
                          {[item.accountName, item.step, `${item.attempts} ${item.attempts === 1 ? "try" : "tries"}`].filter(Boolean).join(" · ")} · <When value={item.createdAt} />
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            </div>
          ))}
        </Card>

        {group && issue ? <IssueDetail key={issue.id} group={group} issue={issue} pending={pending} run={run} /> : null}
      </div>

      {result && <Notice tone={result.ok ? "success" : "destructive"}>{result.message}</Notice>}
    </section>
  );
}

function IssueDetail({
  group,
  issue,
  pending,
  run,
}: {
  group: IssueGroup;
  issue: Issue;
  pending: boolean;
  run: (action: () => Promise<IssueActionResult>) => void;
}) {
  const [note, setNote] = useState("");
  const [kept, setKept] = useState<{ detail: string | null; snapshot: string | null } | null>(null);
  const [loading, startLoading] = useTransition();

  return (
    <Card className="space-y-4 p-5">
      <div>
        <p className="text-muted-foreground font-mono text-xs">{group.code} · {group.platform}</p>
        <h3 className="mt-1 text-lg font-semibold">{group.title}</h3>
        <p className="mt-1 text-sm">{issue.message}</p>
      </div>

      <dl className="grid grid-cols-[110px_1fr] gap-x-3 gap-y-1.5 text-sm">
        <dt className="text-muted-foreground">Record</dt>
        <dd>{issue.externalReference ?? "—"}</dd>
        <dt className="text-muted-foreground">Where</dt>
        <dd>{issue.step ?? "—"}</dd>
        <dt className="text-muted-foreground">Account</dt>
        <dd>{issue.accountName ?? "File import"}</dd>
        <dt className="text-muted-foreground">Tries</dt>
        <dd className="tabular-nums">{issue.attempts}</dd>
        <dt className="text-muted-foreground">Seen</dt>
        <dd>
          <When value={issue.createdAt} />
        </dd>
      </dl>

      {issue.fix && (
        <Notice tone="info" icon={<AlertTriangle className="h-4 w-4" />} title="Fix">
          {issue.fix}
        </Notice>
      )}
      {issue.note && <p className="text-muted-foreground text-xs">{issue.note}</p>}

      {(issue.detail || issue.hasSnapshot) && (
        <details className="rounded-md border px-3 py-2 text-sm" onToggle={(event) => {
          if ((event.target as HTMLDetailsElement).open && !kept) startLoading(async () => setKept(await loadIssueSnapshot(issue.id!).catch(() => ({ detail: "Could not load the detail. Try again.", snapshot: null }))));
        }}>
          <summary className="cursor-pointer font-medium">Technical detail{issue.hasSnapshot ? " and what was read" : ""}</summary>
          {loading && <p className="text-muted-foreground mt-2 text-xs">Loading…</p>}
          {kept?.detail && <pre className="bg-muted/50 mt-2 max-h-48 overflow-auto whitespace-pre-wrap rounded p-2 font-mono text-xs">{kept.detail}</pre>}
          {kept?.snapshot && <pre className="bg-muted/50 mt-2 max-h-64 overflow-auto whitespace-pre-wrap rounded p-2 font-mono text-xs">{kept.snapshot}</pre>}
        </details>
      )}

      <label className="block text-sm">
        <span className="text-muted-foreground mb-1 block text-xs">Note (optional)</span>
        <input
          value={note}
          onChange={(event) => setNote(event.target.value)}
          className="border-input bg-background focus-visible:ring-ring h-9 w-full rounded-md border px-3 text-sm focus-visible:outline-none focus-visible:ring-2"
          placeholder="What you did about it"
        />
      </label>

      <div className="flex flex-wrap gap-2">
        {issue.accountName && (
          <Button type="button" size="sm" disabled={pending} onClick={() => run(() => retryIssue(issue.id!))}>
            <RotateCcw className="mr-2 h-4 w-4" aria-hidden /> Retry
          </Button>
        )}
        <Button type="button" size="sm" variant={issue.accountName ? "outline" : "default"} disabled={pending} onClick={() => run(() => resolveIssue(issue.id!, false, note))}>
          Mark resolved
        </Button>
        <Button type="button" size="sm" variant="ghost" disabled={pending} onClick={() => run(() => resolveIssue(issue.id!, true, note))}>
          Dismiss
        </Button>
      </div>
    </Card>
  );
}
