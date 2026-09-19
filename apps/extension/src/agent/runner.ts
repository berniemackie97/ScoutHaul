import { browser } from "wxt/browser";
import { adapterFor } from "../adapters/registry";
import { AdapterError, type MarketplaceAdapter, type MarketplaceContext, type Session } from "../adapters/types";
import { logActivity } from "./activity";
import { ApiError, type AgentApi } from "./api";
import { describeFailure, waitsForAPerson } from "./failures";
import { initialGovernorState, nextSlot, pauseOf, recordOutcome, recordRequest, type GovernorPolicy, type Outcome } from "./governor";
import { adapterMemory } from "./memory";
import { governorItem, sessionsItem, type StoredSession } from "./storage";
import type { MarketplaceTab } from "./transport";
import type { AgentAccount, FailureKind, Heartbeat, Work } from "./types";

export interface RunnerDeps {
  api: AgentApi;
  now(): Date;
  sleep(ms: number): Promise<void>;
}

/** How a piece of work ended, in words for the popup. */
export type RunResult =
  /** `issues`: some records could not be used, and the run's summary says how many. */
  | { status: "done"; text: string; changed: boolean; issues: boolean }
  | { status: "failed"; kind: FailureKind; text: string }
  /** Nothing to say: another browser is running it, or it was never this browser's to run. */
  | { status: "skipped" };

const ordersPerBatch = 200;
const listingsPerBatch = 500;

/** Whether this browser has been allowed to reach the marketplace. */
export async function hasAccess(adapter: MarketplaceAdapter): Promise<boolean> {
  return browser.permissions.contains({ origins: adapter.origins });
}

/** How hard a marketplace may be pressed: the server's policy, or safe limits before it has said. */
export function limitsFor(heartbeat: Heartbeat | null | undefined, platform: string): GovernorPolicy {
  const policy = heartbeat?.platforms?.find((item) => item.platform === platform);
  return {
    minSecondsBetweenRequests: policy?.minSecondsBetweenRequests ?? 3,
    maxRequestsPerMinute: policy?.maxRequestsPerMinute ?? 12,
    maxRequestsPerDay: policy?.maxRequestsPerDay ?? 1500,
  };
}

/** Why the governor will not let the marketplace be read now — or null, when it will. */
export async function heldBack(platform: string, limits: GovernorPolicy, now: Date): Promise<{ kind: FailureKind; reason: string } | null> {
  const state = (await governorItem.getValue())[platform] ?? initialGovernorState();
  const slot = nextSlot(state, limits, now.getTime(), () => 0);
  if (slot.allowed) {
    return null;
  }

  const kind = pauseOf(state, now.getTime())?.kind;
  return {
    kind: kind === "challenge" ? "Challenge" : kind === "site-changed" ? "SiteChanged" : kind === "failed" ? "Error" : "RateLimited",
    reason: slot.reason,
  };
}

/**
 * Wraps a marketplace tab so every request first passes the governor: it waits out the spacing,
 * counts toward the caps, and refuses outright while the marketplace is paused.
 */
export function governedContext(tab: MarketplaceTab, platform: string, policy: GovernorPolicy, deps: Pick<RunnerDeps, "now" | "sleep">, onRequest?: () => void): MarketplaceContext {
  const paced = async <T>(go: () => Promise<T>): Promise<T> => {
    const states = await governorItem.getValue();
    const slot = nextSlot(states[platform] ?? initialGovernorState(), policy, deps.now().getTime());
    if (!slot.allowed) {
      throw new AdapterError("RateLimited", slot.reason);
    }

    if (slot.waitMs > 0) {
      await deps.sleep(slot.waitMs);
    }

    const latest = await governorItem.getValue();
    await governorItem.setValue({ ...latest, [platform]: recordRequest(latest[platform] ?? initialGovernorState(), deps.now().getTime()) });
    onRequest?.();
    return go();
  };

  return {
    now: () => deps.now(),
    page: (url: string) => paced(() => tab.page(url)),
    request: (url: string) => paced(() => tab.request(url)),
    memory: adapterMemory(platform),
  };
}

/** Teaches the governor how a marketplace reacted, pausing it when it pushed back. */
export async function learn(platform: string, kind: FailureKind | "ok", now: Date): Promise<void> {
  const outcome = outcomeFor(kind);
  if (outcome === null) {
    return;
  }

  const states = await governorItem.getValue();
  await governorItem.setValue({ ...states, [platform]: recordOutcome(states[platform] ?? initialGovernorState(), outcome, now.getTime(), platform) });
}

/**
 * The marketplace pushing back teaches the governor; trouble that is not the marketplace's — a
 * sign-in, access, a tab closed by hand — does not, or a person signing in late would pause sync.
 */
function outcomeFor(kind: FailureKind | "ok"): Outcome | null {
  switch (kind) {
    case "ok":
      return "ok";
    case "RateLimited":
      return "rate-limited";
    case "Challenge":
      return "challenge";
    case "SiteChanged":
      return "site-changed";
    case "Network":
    case "Error":
      return "failed";
    default:
      return null;
  }
}

/** What went wrong, classified. Anything unclassified is a bug here, and is logged as one. */
export function kindOf(error: unknown): FailureKind {
  if (error instanceof AdapterError) {
    return error.kind;
  }

  if (error instanceof ApiError) {
    return error.status === 0 ? "Network" : "Error";
  }

  console.error("HaulScout Sync hit an unexpected error.", error);
  return "Error";
}

/** Detail worth passing on beside the reason: an adapter's own account of a page, or HaulScout's refusal. Never a raw browser message. */
function detailOf(error: unknown): string | null {
  if (error instanceof AdapterError && (error.kind === "SiteChanged" || error.kind === "Error")) {
    return error.message;
  }

  return error instanceof ApiError && error.status !== 0 ? error.message : null;
}

/**
 * Records who the browser is signed in as, and says so in the activity when that changed — signed
 * in, signed out, or someone else now.
 */
export async function rememberSession(adapter: MarketplaceAdapter, session: Session, now: Date): Promise<void> {
  const sessions = await sessionsItem.getValue();
  const before: StoredSession | undefined = sessions[adapter.platform];
  await sessionsItem.setValue({ ...sessions, [adapter.platform]: { ...session, checkedAt: now.toISOString() } });

  const signedIn = session.signedIn && session.username !== null;
  if (signedIn && (!before?.signedIn || before.username?.toLowerCase() !== session.username!.toLowerCase())) {
    await logActivity("success", `Signed in to ${adapter.platform} as ${session.username}.`, now);
  } else if (!signedIn && before?.signedIn !== false) {
    await logActivity("warning", describeFailure("SignedOut", adapter.platform), now);
  }
}

/** Checks who the browser is signed in as, through a tab, and remembers it. */
export async function checkSession(adapter: MarketplaceAdapter, context: MarketplaceContext): Promise<Session> {
  const session = await adapter.detectSession(context);
  await rememberSession(adapter, session, context.now());
  return session;
}

/** "Overhaul eBay sales", "Overhaul eBay listings", "Overhaul eBay sales history". */
export function describeWork(work: Work, account: AgentAccount | undefined): string {
  const what = work.loop === "Listings" ? "listings" : work.from ? "sales history" : "sales";
  return `${account?.displayName ?? work.platform ?? "An account"} ${what}`;
}

/**
 * What stops a piece of work before any tab is opened for it, or null when nothing does. None of it
 * is reported: the heartbeat already tells HaulScout, which holds such work back and says why.
 */
export async function preflight(work: Work, heartbeat: Heartbeat, now: Date): Promise<RunResult | null> {
  const adapter = adapterFor(work.platform);
  const account = heartbeat.accounts?.find((item) => item.channelAccountId === work.channelAccountId);
  if (!adapter || !account || !work.channelAccountId || !work.loop) {
    return { status: "skipped" };
  }

  const policy = heartbeat.platforms?.find((item) => item.platform === adapter.platform);
  if (policy?.enabled === false) {
    return { status: "failed", kind: "Error", text: policy.disabledReason ?? `Syncing ${adapter.platform} is switched off in HaulScout for now.` };
  }

  if (!account.boundUsername) {
    return { status: "failed", kind: "Error", text: `Bind ${account.displayName} to its ${adapter.platform} seller account in HaulScout first.` };
  }

  if (!(await hasAccess(adapter))) {
    return { status: "failed", kind: "NoAccess", text: describeFailure("NoAccess", adapter.platform) };
  }

  const held = await heldBack(adapter.platform, limitsFor(heartbeat, adapter.platform), now);
  return held ? { status: "failed", kind: held.kind, text: held.reason } : null;
}

/**
 * Work that could not start. Trouble a person or time clears goes no further — the heartbeat
 * carries it; anything else is reported, so a scan someone asked for is closed with the reason
 * instead of being offered again every minute. A scan asked for in the popup is the popup's to
 * explain, and is not held against the account's schedule.
 */
export async function notStarted(adapter: MarketplaceAdapter, work: Work, kind: FailureKind, detail: string | null, deps: RunnerDeps): Promise<RunResult> {
  await learn(adapter.platform, kind, deps.now());
  if (!waitsForAPerson(kind) && work.trigger !== "Extension") {
    await deps.api
      .reportWorkFailure({ channelAccountId: work.channelAccountId!, loop: work.loop!, kind, message: detail, scanRequestId: work.scanRequestId ?? null })
      .catch(() => undefined);
  }

  return { status: "failed", kind, text: kind === "WrongAccount" && detail ? detail : describeFailure(kind, adapter.platform) };
}

function chunks<T>(items: T[], size: number): T[][] {
  const result: T[][] = [];
  for (let index = 0; index < items.length; index += size) {
    result.push(items.slice(index, index + size));
  }

  return result.length > 0 ? result : [[]];
}

/**
 * Runs one piece of work in a marketplace tab: check who is signed in, take the lease, read,
 * deliver in batches, and close the run — or fail it with a reason the hub and the popup can show.
 * The governor learns from how it went either way.
 */
export async function runWork(work: Work, heartbeat: Heartbeat, tab: MarketplaceTab, deps: RunnerDeps, onRequest?: () => void): Promise<RunResult> {
  const adapter = adapterFor(work.platform);
  const account = heartbeat.accounts?.find((item) => item.channelAccountId === work.channelAccountId);
  if (!adapter || !account || !work.channelAccountId || !work.loop) {
    return { status: "skipped" };
  }

  const context = governedContext(tab, adapter.platform, limitsFor(heartbeat, adapter.platform), deps, onRequest);

  // Who is this browser signed in as, right now? Nothing is opened on the server until it is the
  // account this one syncs.
  let session: Session;
  try {
    session = await checkSession(adapter, context);
  } catch (error) {
    return notStarted(adapter, work, kindOf(error), detailOf(error), deps);
  }

  if (!session.signedIn || !session.username) {
    return notStarted(adapter, work, "SignedOut", null, deps);
  }

  if (account.boundUsername && account.boundUsername.trim().toLowerCase() !== session.username.trim().toLowerCase()) {
    return notStarted(adapter, work, "WrongAccount",
      `This browser is signed in to ${adapter.platform} as ${session.username}, but ${account.displayName} syncs ${account.boundUsername}. `
        + `Sign in as ${account.boundUsername}, or bind the account to ${session.username} in HaulScout.`,
      deps);
  }

  let runId: string;
  let cursor: string | null = null;
  let from: string | null = work.from ?? null;
  try {
    const started = await deps.api.startRun({
      channelAccountId: work.channelAccountId,
      loop: work.loop,
      trigger: work.trigger ?? "Scheduled",
      signedInAs: session.username,
      scanRequestId: work.scanRequestId ?? null,
      marketplaceUserId: session.marketplaceUserId,
    });
    runId = started.runId!;
    cursor = started.cursor ?? null;
    from = started.from ?? from;
  } catch (error) {
    // Another browser has it — that is the lease doing its job, not a problem.
    if (error instanceof ApiError && error.code === "sync_lease_held") {
      return { status: "skipped" };
    }

    const kind = kindOf(error);
    return { status: "failed", kind, text: kind === "Network" ? describeFailure(kind, adapter.platform) : detailOf(error) ?? describeFailure(kind, adapter.platform) };
  }

  try {
    let done;
    if (work.loop === "Sales") {
      const run = await adapter.scanSales(context, { from: from ? new Date(from) : null, cursor }, session);
      const orders = chunks(run.orders, ordersPerBatch);
      const cancelled = chunks(run.cancelledOrderIds, ordersPerBatch);
      for (let index = 0; index < Math.max(orders.length, cancelled.length); index++) {
        await deps.api.addBatch(runId, { orders: orders[index] ?? [], cancelledOrderIds: cancelled[index] ?? [], issues: index === 0 ? run.issues : [] });
      }

      done = await deps.api.complete(runId, { cursor: run.cursor, coversFrom: run.coversFrom, coversTo: run.coversTo });
    } else {
      const snapshot = await adapter.snapshotListings(context, session);
      let first = true;
      for (const listings of chunks(snapshot.listings, listingsPerBatch)) {
        await deps.api.addBatch(runId, { listings, issues: first ? snapshot.issues : [] });
        first = false;
      }

      done = await deps.api.complete(runId, { completeSnapshot: snapshot.complete });
    }

    await learn(adapter.platform, "ok", deps.now());
    const changed = (done.created ?? 0) + (done.updated ?? 0) + (done.rejected ?? 0) > 0;
    return done.status === "Failed"
      ? { status: "failed", kind: "Error", text: done.summary ?? describeFailure("Error", adapter.platform) }
      : { status: "done", text: done.summary ?? "Done.", changed, issues: done.status === "CompletedWithIssues" };
  } catch (error) {
    // The lease went to another browser, or the run was closed under us: stop, and say nothing to
    // the server — the run is already someone else's to finish.
    if (error instanceof ApiError && (error.code === "sync_lease_lost" || error.code === "sync_run_finished")) {
      return { status: "skipped" };
    }

    const kind = kindOf(error);
    await deps.api.fail(runId, kind, detailOf(error)).catch(() => undefined);
    await learn(adapter.platform, kind, deps.now());
    return { status: "failed", kind, text: describeFailure(kind, adapter.platform) };
  }
}
