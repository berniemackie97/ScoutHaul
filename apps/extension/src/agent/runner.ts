import { browser } from "wxt/browser";
import { adapterFor } from "../adapters/registry";
import { AdapterError, type MarketplaceAdapter, type MarketplaceContext, type PageLook, type PageRead, type PageResponse, type Session } from "../adapters/types";
import { logActivity } from "./activity";
import { ApiError, type AgentApi } from "./api";
import { describeFailure, waitsForAPerson } from "./failures";
import { initialGovernorState, nextSlot, pauseOf, recordOutcome, recordRequest, type GovernorPolicy, type Outcome } from "./governor";
import { adapterMemory } from "./memory";
import { governorItem, sessionsItem, updateGovernor, type StoredSession } from "./storage";
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

/** How often a run renews its lease when HaulScout has not said how long the lease lasts. */
const fallbackRenewMs = 2 * 60 * 1000;

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
    await pace(platform, policy, deps);
    onRequest?.();
    return go();
  };

  return {
    now: () => deps.now(),
    page: (url: string, read?: PageRead) => paced(() => tab.page(url, read)),
    request: (url: string) => paced(() => tab.request(url)),
    sleep: (ms: number) => deps.sleep(ms),
    cookie: (name: string) => tab.cookie?.(name) ?? Promise.resolve(undefined),
    memory: adapterMemory(platform),
  };
}

/**
 * Waits out the governor and counts one request against it — refusing outright while the marketplace is paused or
 * held quiet. Every page load counts, a sync tab opening at its first page among them.
 */
export async function pace(platform: string, policy: GovernorPolicy, deps: Pick<RunnerDeps, "now" | "sleep">): Promise<void> {
  const states = await governorItem.getValue();
  const slot = nextSlot(states[platform] ?? initialGovernorState(), policy, deps.now().getTime());
  if (!slot.allowed) {
    throw new HeldBack(slot.reason);
  }

  if (slot.waitMs > 0) {
    await deps.sleep(slot.waitMs);
  }

  await updateGovernor(platform, (state) => recordRequest(state, deps.now().getTime()));
}

/** What a marketplace page shows as it stands: the adapter's own reading, or else what its address and sign-in say. */
export function lookOf(adapter: MarketplaceAdapter, page: PageResponse): PageLook {
  return adapter.look?.(page) ?? { challenge: adapter.pageKind(page.url) === "challenge", signedIn: adapter.sessionFrom?.(page)?.signedIn ?? null };
}

/**
 * The governor's own refusal — a pause, a quiet spell, a cap — rather than the marketplace pushing back. The
 * marketplace was asked nothing, so there is nothing to learn from it.
 */
export class HeldBack extends AdapterError {
  constructor(message: string) {
    super("RateLimited", message);
  }
}

/** Teaches the governor how a marketplace reacted, pausing it when it pushed back. */
export async function learn(platform: string, kind: FailureKind | "ok", now: Date): Promise<void> {
  const outcome = outcomeFor(kind);
  if (outcome === null) {
    return;
  }

  await updateGovernor(platform, (state) => recordOutcome(state, outcome, now.getTime(), platform));
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

  console.error("HaulScout Sync hit an unexpected error.");
  return "Error";
}

/** Detail worth passing on beside the reason: an adapter's own account of a page, or HaulScout's refusal. Never a raw browser message. */
function detailOf(error: unknown): string | null {
  // Which page failed, and how, is what tells a slow site from a broken read; a person's trouble explains itself.
  if (error instanceof AdapterError && !waitsForAPerson(error.kind)) {
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
export async function checkSession(adapter: MarketplaceAdapter, context: MarketplaceContext, shown?: PageResponse): Promise<Session> {
  const session = await adapter.detectSession(context, shown);
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

  // Known to be signed out: a scan would only load a page to be told so again. Signing in is noticed by itself.
  if ((await sessionsItem.getValue())[adapter.platform]?.signedIn === false) {
    return { status: "failed", kind: "SignedOut", text: describeFailure("SignedOut", adapter.platform) };
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
export async function notStarted(adapter: MarketplaceAdapter, work: Work, kind: FailureKind, detail: string | null, deps: RunnerDeps, error?: unknown): Promise<RunResult> {
  if (error instanceof HeldBack) {
    return { status: "failed", kind, text: error.message };
  }

  await learn(adapter.platform, kind, deps.now());
  if (!waitsForAPerson(kind) && work.trigger !== "Extension") {
    await deps.api
      .reportWorkFailure({ channelAccountId: work.channelAccountId!, loop: work.loop!, kind, message: detail, scanRequestId: work.scanRequestId ?? null })
      .catch(() => undefined);
  }

  return { status: "failed", kind, text: kind === "WrongAccount" && detail ? detail : withDetail(describeFailure(kind, adapter.platform), detail) };
}

function withDetail(text: string, detail: string | null): string {
  return detail ? `${text} ${detail}` : text;
}

/**
 * Keeps a run's lease while it reads. Only a batch renews a lease, and a scan delivers nothing until it has read
 * everything — which, a page at a time at a person's pace, can outlast the lease, and the run is then closed as
 * abandoned under a browser still working on it. So, before each page once half the lease has gone, an empty batch
 * renews it. HaulScout not answering is left for the next page to try again; a lease lost stops the run.
 */
export function keepingLease(context: MarketplaceContext, runId: string, leaseExpiresAt: string | null | undefined, deps: RunnerDeps): MarketplaceContext {
  const lease = Date.parse(leaseExpiresAt ?? "") - deps.now().getTime();
  const renewEvery = Number.isFinite(lease) && lease > 0 ? lease / 2 : fallbackRenewMs;
  let renewed = deps.now().getTime();
  const renew = async () => {
    if (deps.now().getTime() - renewed < renewEvery) {
      return;
    }

    try {
      await deps.api.addBatch(runId, {});
      renewed = deps.now().getTime();
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 0)) {
        throw error;
      }
    }
  };

  return {
    ...context,
    page: async (url: string, read?: PageRead) => {
      await renew();
      return context.page(url, read);
    },
    request: async (url: string) => {
      await renew();
      return context.request(url);
    },
  };
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
    return notStarted(adapter, work, kindOf(error), detailOf(error), deps, error);
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
  let leaseExpiresAt: string | null = null;
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
    leaseExpiresAt = started.leaseExpiresAt ?? null;
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

  const reading = keepingLease(context, runId, leaseExpiresAt, deps);
  try {
    let done;
    if (work.loop === "Sales") {
      const run = await adapter.scanSales(reading, { from: from ? new Date(from) : null, cursor }, session);
      const orders = chunks(run.orders, ordersPerBatch);
      const cancelled = chunks(run.cancelledOrderIds, ordersPerBatch);
      // An order the server set aside is read again next time: the loop's place stays where it was until none is.
      let setAside = false;
      for (let index = 0; index < Math.max(orders.length, cancelled.length); index++) {
        const landed = await deps.api.addBatch(runId, { orders: orders[index] ?? [], cancelledOrderIds: cancelled[index] ?? [], issues: index === 0 ? run.issues : [] });
        setAside ||= (landed.rejected ?? 0) > 0;
      }

      done = await deps.api.complete(runId, { cursor: setAside ? cursor : run.cursor, coversFrom: run.coversFrom, coversTo: run.coversTo });
    } else {
      const snapshot = await adapter.snapshotListings(reading, session);
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
    const detail = error instanceof HeldBack ? error.message : detailOf(error);
    await deps.api.fail(runId, kind, detail).catch(() => undefined);
    if (!(error instanceof HeldBack)) {
      await learn(adapter.platform, kind, deps.now());
    }

    return { status: "failed", kind, text: withDetail(describeFailure(kind, adapter.platform), detail) };
  }
}
