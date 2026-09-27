import type { MarketplaceAdapter } from "../adapters/types";
import { pauseOf, type GovernorState } from "../agent/governor";
import type { LastRun, RunningWork, StoredSession } from "../agent/storage";
import type { AgentAccount, LoopKind, PlatformPolicy } from "../agent/types";
import { aliveBeatMs } from "../agent/config";

/**
 * The work in progress, if the background is still alive to do it. A background the browser stopped mid-task never
 * clears its label, so a label with no recent beat is not shown as running.
 */
export function liveRunning(running: RunningWork | null | undefined, aliveAt: string | null | undefined, now: number): RunningWork | null {
  if (!running || !aliveAt) return null;
  return now - Date.parse(aliveAt) <= 3 * aliveBeatMs ? running : null;
}

/**
 * What the popup shows, worked out from what the agent keeps — pure, so every state the popup can be
 * in is tested without a browser.
 */

export type Tone = "ok" | "busy" | "info" | "warning" | "error";

export type MarketplaceStatus =
  | { kind: "disabled"; text: string }
  | { kind: "no-access" }
  | { kind: "checking" }
  | { kind: "paused"; reason: string; until: number; challenge: boolean }
  | { kind: "signed-out" }
  | { kind: "signed-in"; username: string; checkedAt: string };

export interface MarketplaceInput {
  policy: PlatformPolicy | undefined;
  access: boolean | undefined;
  session: StoredSession | undefined;
  governor: GovernorState | undefined;
  now: number;
}

export function marketplaceStatus(adapter: MarketplaceAdapter, input: MarketplaceInput): MarketplaceStatus {
  if (input.policy?.enabled === false) {
    return { kind: "disabled", text: input.policy.disabledReason ?? `Syncing ${adapter.platform} is switched off in HaulScout for now.` };
  }

  if (input.access === false) {
    return { kind: "no-access" };
  }

  const pause = pauseOf(input.governor, input.now);
  if (pause) {
    return { kind: "paused", reason: pause.reason, until: pause.until, challenge: pause.kind === "challenge" };
  }

  if (input.access === undefined || !input.session) {
    return { kind: "checking" };
  }

  return input.session.signedIn && input.session.username
    ? { kind: "signed-in", username: input.session.username, checkedAt: input.session.checkedAt }
    : { kind: "signed-out" };
}

/** Why an account's loop cannot be scanned from here right now, or null when it can. */
export function scanBlocker(adapter: MarketplaceAdapter | undefined, account: AgentAccount, status: MarketplaceStatus | undefined): string | null {
  if (!adapter || !status) {
    return "This browser cannot sync this marketplace.";
  }

  if (!account.boundUsername) {
    return "Bind this account in HaulScout first.";
  }

  switch (status.kind) {
    case "disabled":
      return status.text;
    case "no-access":
      return `Allow access to ${adapter.platform} first.`;
    case "paused":
      return status.reason;
    case "signed-out":
      return `Sign in to ${adapter.platform} first.`;
    case "signed-in":
      return status.username.toLowerCase() === account.boundUsername.toLowerCase()
        ? null
        : `This browser is signed in as ${status.username}, not ${account.boundUsername}.`;
    default:
      return null;
  }
}

export interface LoopRow {
  loop: LoopKind;
  cadence: string;
  /** The last result: this browser's own, or HaulScout's record when that is newer. */
  last: { tone: "success" | "warning" | "error" | "muted"; text: string } | null;
  next: string | null;
  state: "idle" | "running" | "queued";
}

type ServerLoop = NonNullable<AgentAccount["loops"]>[number];

export function loopRow(
  account: AgentAccount,
  loop: LoopKind,
  local: LastRun | undefined,
  running: RunningWork | null | undefined,
  queued: readonly string[],
  now: Date,
): LoopRow {
  const key = `${account.channelAccountId}:${loop}`;
  const server: ServerLoop | undefined = account.loops?.find((item) => item.loop === loop);
  const everyMinutes = server ? server.everyMinutes : loop === "Sales" ? account.salesEveryMinutes : account.listingsEveryMinutes;
  const serverAt = Date.parse(server?.lastSucceededAt ?? "") || 0;

  let last: LoopRow["last"] = null;
  if (local && Date.parse(local.at) >= serverAt) {
    last = { tone: local.tone, text: `${local.text.replace(/\.$/, "")} · ${ago(local.at, now)}` };
  } else if (server?.lastError) {
    last = { tone: "error", text: server.lastError };
  } else if (server?.lastSucceededAt) {
    last = { tone: "muted", text: `Last synced ${ago(server.lastSucceededAt, now)}` };
  } else {
    last = { tone: "muted", text: "Not synced yet" };
  }

  return {
    loop,
    cadence: cadence(everyMinutes),
    last,
    next: everyMinutes && server?.nextDueAt ? `next ${dueIn(server.nextDueAt, now)}` : null,
    state: running?.key === key ? "running" : queued.includes(key) ? "queued" : "idle",
  };
}

export interface Headline {
  tone: Tone;
  title: string;
  text: string | null;
}

/**
 * The one line at the top that says whether anything needs doing: HaulScout out of reach, work under
 * way, a marketplace that needs the person at this browser, an account still to bind — or all set.
 */
export function headline(input: {
  connection: { ok: boolean; message: string | null } | null | undefined;
  running: RunningWork | null | undefined;
  marketplaces: { adapter: MarketplaceAdapter; status: MarketplaceStatus }[];
  accounts: AgentAccount[];
  now: Date;
}): Headline {
  if (input.connection && !input.connection.ok) {
    return { tone: "error", title: "Can't reach HaulScout", text: `${input.connection.message ?? "HaulScout could not be reached."} Trying again every minute.` };
  }

  if (input.running) {
    const pages = input.running.pages;
    return { tone: "busy", title: `${input.running.label}…`, text: pages > 0 ? `${pages} page${pages === 1 ? "" : "s"} read so far.` : null };
  }

  for (const { adapter, status } of input.marketplaces) {
    switch (status.kind) {
      case "no-access":
        return { tone: "warning", title: `Allow access to ${adapter.platform}`, text: `HaulScout Sync needs your permission to read your ${adapter.platform} seller pages.` };
      case "signed-out":
        return { tone: "warning", title: `Sign in to ${adapter.platform}`, text: `This browser is signed out of ${adapter.platform}. Syncing picks up by itself once you sign in.` };
      case "paused":
        return status.challenge
          ? { tone: "warning", title: `${adapter.platform} wants to check you're a person`, text: `Open ${adapter.platform} and complete the check — syncing picks up by itself after.` }
          : { tone: "warning", title: `${adapter.platform} is paused`, text: `${status.reason} Back by itself at ${clock(status.until, input.now)}.` };
      default:
        break;
    }
  }

  const bound = input.accounts.filter((account) => account.boundUsername);
  if (input.accounts.length > 0 && bound.length === 0) {
    return { tone: "info", title: "Bind your accounts", text: "In HaulScout, open Sync and bind each account to the seller account this browser is signed in as." };
  }

  const upcoming = bound
    .flatMap((account) => (account.loops ?? [])
      .filter((loop) => loop.everyMinutes && loop.nextDueAt)
      .map((loop) => ({ account, loop, at: Date.parse(loop.nextDueAt!) })))
    .sort((left, right) => left.at - right.at)[0];
  return upcoming
    ? { tone: "ok", title: "All set", text: `Next: ${upcoming.account.displayName} ${upcoming.loop.loop === "Listings" ? "listings" : "sales"} ${dueIn(upcoming.loop.nextDueAt, input.now)}.` }
    : { tone: "ok", title: "All set", text: "No schedules are on. Scan from here, or switch schedules on in HaulScout." };
}

/** "just now", "4 min ago", "2 h ago", "3 days ago". */
export function ago(iso: string | null | undefined, now: Date): string {
  if (!iso) {
    return "never";
  }

  const seconds = Math.max(0, Math.round((now.getTime() - Date.parse(iso)) / 1000));
  if (seconds < 45) {
    return "just now";
  }

  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min ago`;
  }

  const hours = Math.round(minutes / 60);
  return hours < 36 ? `${hours} h ago` : `${Math.round(hours / 24)} days ago`;
}

/** "in 6 min", "in 3 h", "now". */
export function dueIn(iso: string | null | undefined, now: Date): string {
  const wait = Date.parse(iso ?? "") - now.getTime();
  if (!Number.isFinite(wait) || wait <= 60_000) {
    return "now";
  }

  const minutes = Math.round(wait / 60_000);
  return minutes < 60 ? `in ${minutes} min` : `in ${Math.round(minutes / 60)} h`;
}

/** "every 10 min", "every 4 h", "off". */
export function cadence(minutes: number | null | undefined): string {
  if (!minutes) {
    return "schedule off";
  }

  return minutes % 60 === 0 ? `every ${minutes / 60} h` : `every ${minutes} min`;
}

/** "3:40 PM" today, "Sat 12:00 AM" another day — in this browser's own format. */
export function clock(at: number, now: Date = new Date()): string {
  const when = new Date(at);
  return when.toDateString() === now.toDateString()
    ? when.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })
    : when.toLocaleString([], { weekday: "short", hour: "numeric", minute: "2-digit" });
}
