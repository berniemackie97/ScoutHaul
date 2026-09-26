import { storage } from "wxt/utils/storage";
import { initialGovernorState, type GovernorState } from "./governor";
import type { Heartbeat } from "./types";

/**
 * Everything the agent keeps, in this browser only (`storage.local`, never synced to other browsers).
 * The device token lives here and nowhere else; the popup reads the rest to show what is going on.
 */

export interface StoredPairing {
  agentId: string;
  agentName: string;
  organizationName: string;
  token: string;
  pairedAt: string;
}

/** Who this browser is signed in as on one marketplace, as last checked. */
export interface StoredSession {
  signedIn: boolean;
  username: string | null;
  marketplaceUserId: string | null;
  checkedAt: string;
}

export interface ActivityEntry {
  at: string;
  tone: "info" | "success" | "warning" | "error";
  message: string;
  /** How many times this same line happened in a row, when more than once. */
  count?: number;
}

/** Why the device token stopped working, so the popup can say so instead of silently doing nothing. */
export interface AuthProblem {
  code: string;
  message: string;
  at: string;
}

/** What the agent is doing right now, for the popup. */
export interface RunningWork {
  /** "Reading Overhaul eBay sales", "Checking the eBay sign-in". */
  label: string;
  /** `accountId:loop` for a scan; null for anything else. */
  key: string | null;
  startedAt: string;
  /** Marketplace pages read so far. */
  pages: number;
}

/** How the last scan of one loop run in this browser ended. */
export interface LastRun {
  at: string;
  tone: "success" | "warning" | "error";
  text: string;
}

/** Whether HaulScout answered the last check-in — an outage is shown in the popup, not hidden. */
export interface Connection {
  ok: boolean;
  /** When it last changed between reachable and not. */
  since: string;
  message: string | null;
}

export const pairingItem = storage.defineItem<StoredPairing | null>("local:pairing", { fallback: null });

export const heartbeatItem = storage.defineItem<(Heartbeat & { receivedAt: string }) | null>("local:heartbeat", { fallback: null });

export const sessionsItem = storage.defineItem<Record<string, StoredSession>>("local:sessions", { fallback: {} });

export const governorItem = storage.defineItem<Record<string, GovernorState>>("local:governor", { fallback: {} });

let governorWrites: Promise<unknown> = Promise.resolve();

/** Every change to the governor, one after another: each reads what the last wrote, so none is lost. */
export function updateGovernors(change: (states: Record<string, GovernorState>) => Record<string, GovernorState>): Promise<void> {
  const write = governorWrites.then(async () => governorItem.setValue(change(await governorItem.getValue())));
  governorWrites = write.catch(() => undefined);
  return write;
}

export function updateGovernor(platform: string, change: (state: GovernorState) => GovernorState): Promise<void> {
  return updateGovernors((states) => ({ ...states, [platform]: change(states[platform] ?? initialGovernorState()) }));
}

export const activityItem = storage.defineItem<ActivityEntry[]>("local:activity", { fallback: [] });

export const authProblemItem = storage.defineItem<AuthProblem | null>("local:authProblem", { fallback: null });

export const connectionItem = storage.defineItem<Connection | null>("local:connection", { fallback: null });

export const runningItem = storage.defineItem<RunningWork | null>("local:running", { fallback: null });

/** Scans asked for in this browser that are waiting their turn, as `accountId:loop`. */
export const queuedItem = storage.defineItem<string[]>("local:queued", { fallback: [] });

/** How the last scan of each loop run here ended, by `accountId:loop`. */
export const lastRunsItem = storage.defineItem<Record<string, LastRun>>("local:lastRuns", { fallback: {} });

/** The portal address the bridge last ran on — where "Open HaulScout" goes. */
export const portalItem = storage.defineItem<string | null>("local:portal", { fallback: null });

/**
 * The sync tabs open now, by marketplace. Session storage, so it is gone with the browser session
 * that owned those tabs — a background restarted mid-session uses it to close what it left open.
 */
export const syncTabsItem = storage.defineItem<Record<string, number>>("session:syncTabs", { fallback: {} });

/** The key a scan of one loop on one account is known by. */
export function workKey(channelAccountId: string, loop: string): string {
  return `${channelAccountId}:${loop}`;
}
