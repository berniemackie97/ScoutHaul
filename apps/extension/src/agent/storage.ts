import { storage } from "wxt/utils/storage";
import type { GovernorState } from "./governor";
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
}

/** Why the device token stopped working, so the popup can say so instead of silently doing nothing. */
export interface AuthProblem {
  code: string;
  message: string;
  at: string;
}

export const pairingItem = storage.defineItem<StoredPairing | null>("local:pairing", { fallback: null });

export const heartbeatItem = storage.defineItem<(Heartbeat & { receivedAt: string }) | null>("local:heartbeat", { fallback: null });

export const sessionsItem = storage.defineItem<Record<string, StoredSession>>("local:sessions", { fallback: {} });

export const governorItem = storage.defineItem<Record<string, GovernorState>>("local:governor", { fallback: {} });

export const activityItem = storage.defineItem<ActivityEntry[]>("local:activity", { fallback: [] });

export const authProblemItem = storage.defineItem<AuthProblem | null>("local:authProblem", { fallback: null });

/** What is running right now, so a restarted background worker never starts a second run beside it. */
export const runningItem = storage.defineItem<{ label: string; startedAt: string } | null>("local:running", { fallback: null });
