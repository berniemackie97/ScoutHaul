import type { components } from "@overhaul/api-client";
import type { Tone } from "@/features/imports/import-model";

export type SyncOverview = components["schemas"]["SyncOverviewDto"];
export type SyncAccount = components["schemas"]["SyncAccountDto"];
export type SyncLoop = components["schemas"]["SyncLoopDto"];
export type SyncAgent = components["schemas"]["SyncAgentDto"];
export type SyncScanRequest = components["schemas"]["SyncScanRequestDto"];
export type SyncPlatformSettings = components["schemas"]["SyncPlatformSettingsDto"];
export type SyncSession = components["schemas"]["SyncSessionSeenDto"];
export type LoopKind = components["schemas"]["SyncLoopKind"];
export type ScanScope = components["schemas"]["SyncScanScope"];

/** An account's sync health as a badge. */
export function healthBadge(health: SyncAccount["health"]): { label: string; tone: Tone } {
  switch (health) {
    case "Ready":
      return { label: "Ready", tone: "success" };
    case "NotBound":
      return { label: "Not bound", tone: "warning" };
    case "NoBrowser":
      return { label: "No browser", tone: "warning" };
    case "Paused":
      return { label: "Paused", tone: "warning" };
    case "Disabled":
      return { label: "Switched off", tone: "danger" };
    case "Unsupported":
      return { label: "Not yet supported", tone: "neutral" };
    default:
      return { label: "Unknown", tone: "neutral" };
  }
}

export function loopName(loop: LoopKind | undefined): string {
  return loop === "Listings" ? "Listings" : "Sales";
}

/** "every 10 min", "every 4 h", "Off". */
export function cadenceLabel(minutes: number | null | undefined): string {
  if (!minutes) {
    return "Off";
  }

  return minutes % 60 === 0 ? `Every ${minutes / 60} h` : `Every ${minutes} min`;
}

/**
 * The schedules a loop can be set to: Off, then a ladder of cadences no faster than the
 * marketplace allows, with the suggested one marked.
 */
export function cadenceOptions(loop: LoopKind, settings: SyncPlatformSettings | undefined): { value: number | null; label: string }[] {
  const minimum = (loop === "Sales" ? settings?.minSalesMinutes : settings?.minListingsMinutes) ?? (loop === "Sales" ? 5 : 60);
  const suggested = (loop === "Sales" ? settings?.suggestedSalesMinutes : settings?.suggestedListingsMinutes) ?? null;
  const ladder = loop === "Sales" ? [5, 10, 15, 30, 60, 120, 240] : [60, 120, 240, 360, 720, 1440];
  const values = [...new Set([...ladder, ...(suggested ? [suggested] : [])])].filter((value) => value >= minimum).sort((a, b) => a - b);
  return [
    { value: null, label: "Off" },
    ...values.map((value) => ({ value, label: `${cadenceLabel(value)}${value === suggested ? " (suggested)" : ""}` })),
  ];
}

const minute = 60_000;

/** "just now", "4 min ago", "3 h ago", "2 days ago". */
export function ago(iso: string | null | undefined, now: Date): string {
  if (!iso) {
    return "never";
  }

  const elapsed = Math.max(0, now.getTime() - Date.parse(iso));
  if (elapsed < 45_000) {
    return "just now";
  }

  const minutes = Math.round(elapsed / minute);
  if (minutes < 60) {
    return `${minutes} min ago`;
  }

  const hours = Math.round(minutes / 60);
  return hours < 36 ? `${hours} h ago` : `${Math.round(hours / 24)} days ago`;
}

/** "in 6 min", "now". */
export function dueIn(iso: string | null | undefined, now: Date): string | null {
  if (!iso) {
    return null;
  }

  const wait = Date.parse(iso) - now.getTime();
  if (wait <= 30_000) {
    return "due now";
  }

  const minutes = Math.round(wait / minute);
  return minutes < 60 ? `in ${minutes} min` : `in ${Math.round(minutes / 60)} h`;
}

/** One loop's last outcome, in a line. */
export function loopSummary(loop: SyncLoop, now: Date): { text: string; tone: Tone } {
  if (loop.runningOn) {
    return { text: `Running on ${loop.runningOn}`, tone: "info" };
  }

  if (loop.lastError && (loop.consecutiveFailures ?? 0) > 0) {
    return { text: `Failed ${ago(loop.lastStartedAt, now)}: ${loop.lastError}`, tone: "danger" };
  }

  if (loop.lastSucceededAt) {
    return { text: `Last synced ${ago(loop.lastSucceededAt, now)}`, tone: "success" };
  }

  return { text: "Not synced yet", tone: "neutral" };
}

/**
 * A scan request's state in the operator's words, with what a queued one is waiting for. A click must
 * never look as if it is about to happen when nothing can run it: a queued scan says whether a
 * browser will pick it up in a moment, or what it is waiting on — a sign-in, a security check, a
 * browser being opened — which it then runs after, by itself.
 */
export function requestStatus(
  request: SyncScanRequest,
  account: Pick<SyncAccount, "health" | "healthMessage"> | undefined,
  anyBrowserOnline: boolean,
): { label: string; tone: Tone; note: string | null } {
  switch (request.status) {
    case "Queued":
      if (!anyBrowserOnline) {
        return { label: "Waiting for a browser", tone: "warning", note: "No browser with HaulScout Sync is online. It starts when one is." };
      }

      return !account || account.health === "Ready"
        ? { label: "Queued", tone: "info", note: "Starting in a moment." }
        : { label: "Waiting", tone: "warning", note: `${account.healthMessage ?? ""} It starts by itself after.`.trim() };
    case "Running":
      return { label: request.runningOn ? `Running on ${request.runningOn}` : "Running", tone: "info", note: null };
    case "Succeeded":
      return { label: "Done", tone: "success", note: null };
    case "Failed":
      return { label: "Failed", tone: "danger", note: null };
    case "Cancelled":
      return { label: "Cancelled", tone: "neutral", note: null };
    case "Expired":
      return { label: "Expired", tone: "neutral", note: null };
    default:
      return { label: request.status ?? "Unknown", tone: "neutral", note: null };
  }
}

/** Where an account's photo sync stands, in a line. */
export function photoSyncSummary(account: Pick<SyncAccount, "photoSyncEnabled" | "photosWaiting">): { text: string; tone: Tone } {
  if (!account.photoSyncEnabled) {
    return { text: "Item photos are left as they are", tone: "neutral" };
  }

  const waiting = account.photosWaiting ?? 0;
  return waiting > 0
    ? { text: `${waiting} ${waiting === 1 ? "listing" : "listings"} still to come over`, tone: "info" }
    : { text: "Item photos match their listings", tone: "success" };
}

/** The scan of one loop on one account that is queued or running, if there is one. */
export function openRequestFor(requests: SyncScanRequest[], channelAccountId: string, loop: LoopKind): SyncScanRequest | undefined {
  return requests.find((request) => request.channelAccountId === channelAccountId && request.loop === loop && !request.from && isOpen(request));
}

export function isOpen(request: SyncScanRequest): boolean {
  return request.status === "Queued" || request.status === "Running";
}

/** Usernames a browser has been seen signed in as, on the account's marketplace, that could be bound. */
export function bindableUsernames(account: SyncAccount): string[] {
  return [
    ...new Set(
      (account.sessions ?? [])
        .filter((session) => session.signedIn && session.username)
        .map((session) => session.username!),
    ),
  ];
}

/** A pairing code split for reading aloud: "7KQ2-MX9D" stays as is; anything else is grouped. */
export function formatCode(code: string): string {
  const bare = code.replace(/[^0-9A-Z]/gi, "").toUpperCase();
  return bare.length === 8 ? `${bare.slice(0, 4)}-${bare.slice(4)}` : code;
}
