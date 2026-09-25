"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { CircleAlert, Images, Loader2, ScanLine, X } from "lucide-react";
import { Badge, Button, Card, Input } from "@overhaul/ui";
import type { Tone } from "@/features/imports/import-model";
import {
  ago,
  bindableUsernames,
  cadenceOptions,
  dueIn,
  healthBadge,
  isOpen,
  loopName,
  loopSummary,
  openRequestFor,
  photoSyncSummary,
  requestStatus,
  type LoopKind,
  type ScanScope,
  type SyncAccount,
  type SyncOverview,
  type SyncPlatformSettings,
  type SyncScanRequest,
} from "@/features/sync/sync-model";
import { useSyncExtension } from "@/features/sync/use-sync-extension";
import { bindAccount, cancelScan, requestScan, setPhotoSync, setSchedule, unbindAccount, type SyncActionResult } from "./actions";

const BADGE: Record<Tone, "success" | "warning" | "destructive" | "info" | "secondary"> = {
  success: "success",
  warning: "warning",
  danger: "destructive",
  info: "info",
  neutral: "secondary",
};

const TEXT: Record<Tone, string> = {
  success: "text-emerald-700",
  warning: "text-amber-700",
  danger: "text-red-700",
  info: "text-sky-700",
  neutral: "text-muted-foreground",
};

const selectClass =
  "h-9 rounded-md border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring disabled:cursor-not-allowed disabled:opacity-50";

const DAY = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric" });

export function SyncHub({ overview }: { overview: SyncOverview }) {
  const router = useRouter();
  const { status: extension, wake } = useSyncExtension();
  const [pending, startTransition] = useTransition();
  const [message, setMessage] = useState<SyncActionResult | null>(null);
  const now = useNow();

  const accounts = overview.accounts ?? [];
  const requests = overview.requests ?? [];
  const agents = (overview.agents ?? []).filter((agent) => !agent.revoked);
  const anyOnline = agents.some((agent) => agent.online);
  const busy = requests.some(isOpen) || accounts.some((account) => (account.loops ?? []).some((loop) => loop.runningOn));

  // The page keeps itself current while someone is looking at it — every few seconds while a scan is
  // queued or running, every half minute otherwise, and on coming back to the tab — so a browser's
  // sign-in, a Bind button or a finished scan shows up without anyone reloading.
  const lastRefresh = useRef(0);
  useEffect(() => {
    const refresh = () => {
      if (document.visibilityState !== "visible" || Date.now() - lastRefresh.current < 2000) return;
      lastRefresh.current = Date.now();
      router.refresh();
    };
    const timer = setInterval(refresh, busy ? 5000 : 30_000);
    document.addEventListener("visibilitychange", refresh);
    window.addEventListener("focus", refresh);
    return () => {
      clearInterval(timer);
      document.removeEventListener("visibilitychange", refresh);
      window.removeEventListener("focus", refresh);
    };
  }, [busy, router]);

  // A confirmation clears itself; a problem stays until it is read and dismissed.
  useEffect(() => {
    if (!message?.ok) return;
    const timer = setTimeout(() => setMessage(null), 6000);
    return () => clearTimeout(timer);
  }, [message]);

  const run = (action: () => Promise<SyncActionResult>, wakeAfter = false) =>
    startTransition(async () => {
      const outcome = await action();
      setMessage(outcome);
      if (outcome.ok && wakeAfter) wake();
      router.refresh();
    });

  const scan = (scope: ScanScope, accountId: string | null, from: string | null = null) => run(() => requestScan(scope, accountId, from), true);
  const syncable = accounts.some((account) => account.health !== "Unsupported" && account.boundUsername);

  return (
    <div className="space-y-8">
      {extension.installed ? (
        <p className="text-muted-foreground text-sm">
          {extension.paired
            ? <>This browser syncs as <strong className="text-foreground">{extension.agentName}</strong>.</>
            : <>HaulScout Sync is installed in this browser but not connected. <Link className="text-brand-600 underline" href="/sync/browsers?connect=1">Connect it</Link>.</>}
        </p>
      ) : null}

      <div aria-live="polite">
        {message ? (
          <div className={`flex items-start justify-between gap-3 rounded-lg border px-4 py-3 text-sm ${message.ok ? "border-emerald-200 bg-emerald-50 text-emerald-800" : "border-amber-200 bg-amber-50 text-amber-900"}`}>
            <p>{message.message}</p>
            <button type="button" className="shrink-0 opacity-70 hover:opacity-100" aria-label="Dismiss" onClick={() => setMessage(null)}>
              <X className="h-4 w-4" aria-hidden />
            </button>
          </div>
        ) : null}
      </div>

      <section aria-labelledby="accounts-heading" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="accounts-heading" className="text-sm font-semibold">Accounts</h2>
          {syncable ? (
            <div className="flex gap-2">
              <Button variant="outline" size="sm" disabled={pending} onClick={() => scan("Sales", null)}>Scan all sales</Button>
              <Button variant="outline" size="sm" disabled={pending} onClick={() => scan("Everything", null)}>Scan everything</Button>
            </div>
          ) : null}
        </div>
        {accounts.length === 0 ? (
          <Card className="text-muted-foreground p-6 text-sm">
            No marketplace accounts yet. Add your eBay, Depop, Poshmark or Vinted accounts under <Link className="text-brand-600 underline" href="/channels">Channels</Link>.
          </Card>
        ) : (
          <div className="grid gap-4 lg:grid-cols-2">
            {accounts.map((account) => (
              <AccountCard
                key={account.channelAccountId}
                account={account}
                settings={overview.platforms?.find((platform) => platform.platform === account.platform)}
                requests={requests}
                now={now}
                pending={pending}
                onScan={scan}
                onRun={run}
              />
            ))}
          </div>
        )}
      </section>

      {requests.length > 0 ? <Requests requests={requests} accounts={accounts} anyOnline={anyOnline} now={now} pending={pending} onRun={run} /> : null}
    </div>
  );
}

function AccountCard(props: {
  account: SyncAccount;
  settings: SyncPlatformSettings | undefined;
  requests: SyncScanRequest[];
  now: Date;
  pending: boolean;
  onScan: (scope: ScanScope, accountId: string | null, from?: string | null) => void;
  onRun: (action: () => Promise<SyncActionResult>, wakeAfter?: boolean) => void;
}) {
  const { account, settings, requests, now, pending, onScan, onRun } = props;
  const id = account.channelAccountId!;
  const badge = healthBadge(account.health);
  const loops = account.loops ?? [];
  const current = {
    sales: loops.find((loop) => loop.loop === "Sales")?.everyMinutes ?? null,
    listings: loops.find((loop) => loop.loop === "Listings")?.everyMinutes ?? null,
  };
  const historyDays = settings?.historyDays ?? 90;
  const earliest = new Date(now.getTime() - historyDays * 86_400_000).toISOString().slice(0, 10);
  const [from, setFrom] = useState("");
  const bindable = bindableUsernames(account);

  return (
    <Card className="flex flex-col gap-4 p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="font-semibold">{account.displayName}</p>
          <p className="text-muted-foreground text-xs">{account.platform}</p>
        </div>
        <Badge variant={BADGE[badge.tone]}>{badge.label}</Badge>
      </div>
      <p className="text-muted-foreground text-sm">{account.healthMessage}</p>

      {account.supported ? (
        <>
          <div className="text-sm">
            {account.boundUsername ? (
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span>
                  Syncs <strong>{account.boundUsername}</strong>
                </span>
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={pending}
                  onClick={() => {
                    if (window.confirm(`Unbind ${account.displayName}? It stops syncing until it is bound again, and its waiting scans are cancelled.`)) {
                      onRun(() => unbindAccount(id));
                    }
                  }}
                >
                  Unbind
                </Button>
              </div>
            ) : bindable.length > 0 ? (
              <div className="space-y-2">
                <p className="text-muted-foreground text-xs">A paired browser is signed in to {account.platform} as:</p>
                <div className="flex flex-wrap gap-2">
                  {bindable.map((username) => (
                    <Button
                      key={username}
                      size="sm"
                      disabled={pending}
                      onClick={() => {
                        if (window.confirm(`Sync ${account.displayName} from the ${account.platform} account ${username}? Browsers signed in as anyone else will be refused.`)) {
                          onRun(() => bindAccount(id, username));
                        }
                      }}
                    >
                      Bind to {username}
                    </Button>
                  ))}
                </div>
              </div>
            ) : null}
          </div>

          <div className="divide-y rounded-lg border">
            {loops.map((loop) => {
              const summary = loopSummary(loop, now);
              const next = loop.everyMinutes ? dueIn(loop.nextDueAt, now) : null;
              return (
                <div key={loop.loop} className="grid gap-2 p-3 sm:grid-cols-[6rem_1fr_auto] sm:items-center">
                  <span className="text-sm font-medium">{loopName(loop.loop)}</span>
                  <div className="min-w-0 text-xs">
                    <p className={TEXT[summary.tone]}>
                      {summary.text}
                      {loop.lastRunId ? (
                        <>
                          {" · "}
                          <Link className="underline" href={`/imports/runs/${loop.lastRunId}`}>details</Link>
                        </>
                      ) : null}
                    </p>
                    {next ? <p className="text-muted-foreground">Next {next}</p> : null}
                  </div>
                  <select
                    aria-label={`${loopName(loop.loop)} schedule for ${account.displayName}`}
                    className={selectClass}
                    value={loop.everyMinutes ?? ""}
                    disabled={pending || !account.boundUsername}
                    onChange={(event) => {
                      const minutes = event.target.value ? Number(event.target.value) : null;
                      onRun(() => setSchedule(id, loop.loop as LoopKind, minutes, current));
                    }}
                  >
                    {cadenceOptions(loop.loop as LoopKind, settings).map((option) => (
                      <option key={option.label} value={option.value ?? ""}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </div>
              );
            })}
          </div>

          <PhotoSync account={account} pending={pending} onRun={onRun} />

          {account.boundUsername ? (
            <div className="flex flex-wrap items-end gap-2">
              <ScanButton loop="Sales" open={openRequestFor(requests, id, "Sales")} pending={pending} primary onScan={() => onScan("Sales", id)} />
              <ScanButton loop="Listings" open={openRequestFor(requests, id, "Listings")} pending={pending} onScan={() => onScan("Listings", id)} />
              {account.supportsHistory ? (
                <form
                  className="flex items-end gap-2"
                  onSubmit={(event) => {
                    event.preventDefault();
                    if (from) onScan("History", id, new Date(`${from}T00:00:00`).toISOString());
                  }}
                >
                  <label className="text-muted-foreground flex flex-col text-xs">
                    Sales history from
                    <Input
                      type="date"
                      className="h-9 w-40"
                      min={earliest}
                      max={now.toISOString().slice(0, 10)}
                      value={from}
                      onChange={(event) => setFrom(event.target.value)}
                    />
                  </label>
                  <Button variant="outline" size="sm" type="submit" disabled={pending || !from}>Read</Button>
                </form>
              ) : null}
            </div>
          ) : null}
        </>
      ) : null}
    </Card>
  );
}

/**
 * Whether an item's photos follow this account's listings. Switching it on replaces those items' photos with the
 * listing's, so the choice is made plainly and confirmed before anything is overwritten.
 */
function PhotoSync(props: {
  account: SyncAccount;
  pending: boolean;
  onRun: (action: () => Promise<SyncActionResult>, wakeAfter?: boolean) => void;
}) {
  const { account, pending, onRun } = props;
  const id = account.channelAccountId!;
  const on = account.photoSyncEnabled === true;
  const summary = photoSyncSummary(account);

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3">
      <div className="flex items-start gap-2">
        <Images className="text-muted-foreground mt-0.5 h-4 w-4 shrink-0" aria-hidden />
        <div className="text-sm">
          <p className="font-medium">Photos</p>
          <p className={`text-xs ${TEXT[summary.tone]}`}>{summary.text}</p>
          {account.platform === "eBay" && !on ? (
            <p className="text-muted-foreground mt-1 text-xs">
              An eBay scan brings back one picture per listing. For an item listed on Vinted too, follow Vinted&rsquo;s photos instead.
            </p>
          ) : null}
        </div>
      </div>
      <Button
        variant={on ? "outline" : "default"}
        size="sm"
        disabled={pending || !account.supported}
        onClick={() => {
          if (on) {
            onRun(() => setPhotoSync(id, false));
            return;
          }

          if (
            window.confirm(
              `Keep ${account.displayName}'s item photos in step with its listings?\n\n`
                + "Each linked item's photos are replaced by the photos its listing shows, in the listing's order. "
                + "Photos uploaded in HaulScout for those items are replaced too. Items already matching are left alone.\n\n"
                + "For an item listed in more than one place, the listing showing the most photos wins.",
            )
          ) {
            onRun(() => setPhotoSync(id, true));
          }
        }}
      >
        {on ? "Stop following listings" : "Follow listing photos"}
      </Button>
    </div>
  );
}

/** A scan button that shows its scan under way — queued, or running — instead of offering it twice. */
function ScanButton(props: { loop: LoopKind; open: SyncScanRequest | undefined; pending: boolean; primary?: boolean; onScan: () => void }) {
  const { loop, open, pending, primary, onScan } = props;
  const noun = loop === "Sales" ? "sales" : "listings";
  return (
    <Button size="sm" variant={primary ? "default" : "outline"} disabled={pending || open !== undefined} onClick={onScan}>
      {open?.status === "Running" ? (
        <>
          <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> Scanning {noun}…
        </>
      ) : open ? (
        <>{loopName(loop)} scan queued</>
      ) : (
        <>
          {primary ? <ScanLine className="mr-2 h-4 w-4" aria-hidden /> : null} Scan {noun}
        </>
      )}
    </Button>
  );
}

function Requests(props: {
  requests: SyncScanRequest[];
  accounts: SyncAccount[];
  anyOnline: boolean;
  now: Date;
  pending: boolean;
  onRun: (action: () => Promise<SyncActionResult>) => void;
}) {
  const { requests, accounts, anyOnline, now, pending, onRun } = props;
  return (
    <section aria-labelledby="scans-heading" className="space-y-3">
      <h2 id="scans-heading" className="text-sm font-semibold">Scans you asked for</h2>
      <Card className="overflow-x-auto p-0">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
            <tr>
              <th className="px-4 py-3 font-medium">Account</th>
              <th className="px-4 py-3 font-medium">Scan</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium">Asked</th>
              <th className="px-4 py-3" />
            </tr>
          </thead>
          <tbody className="divide-y">
            {requests.map((request) => {
              const status = requestStatus(request, accounts.find((account) => account.channelAccountId === request.channelAccountId), anyOnline);
              const note = status.note ?? request.message;
              return (
                <tr key={request.id}>
                  <td className="px-4 py-3 font-medium">{request.accountName}</td>
                  <td className="text-muted-foreground px-4 py-3">
                    {request.from ? `Sales since ${DAY.format(new Date(request.from))}` : loopName(request.loop)}
                  </td>
                  <td className="px-4 py-3">
                    <span className="inline-flex items-center gap-2">
                      {request.status === "Running" ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden /> : null}
                      {status.tone === "warning" ? <CircleAlert className="h-3.5 w-3.5 text-amber-600" aria-hidden /> : null}
                      <Badge variant={BADGE[status.tone]}>{status.label}</Badge>
                    </span>
                    {note ? <p className="text-muted-foreground mt-1 text-xs">{note}</p> : null}
                  </td>
                  <td className="text-muted-foreground whitespace-nowrap px-4 py-3 text-xs">{ago(request.requestedAt, now)}</td>
                  <td className="px-4 py-3 text-right">
                    {request.status === "Queued" ? (
                      <Button variant="ghost" size="sm" disabled={pending} onClick={() => onRun(() => cancelScan(request.id!))}>
                        Cancel
                      </Button>
                    ) : request.runId ? (
                      <Link className="text-brand-600 text-xs underline" href={`/imports/runs/${request.runId}`}>Details</Link>
                    ) : null}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </Card>
    </section>
  );
}

/** The time the page's relative times are measured from, moving on while it is open. */
function useNow(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 15_000);
    return () => clearInterval(timer);
  }, []);
  return now;
}
