import { browser } from "wxt/browser";
import { AdapterError, type MarketplaceAdapter, type MarketplaceContext } from "../adapters/types";
import { adapterFor } from "../adapters/registry";
import { logActivity } from "./activity";
import { ApiError, type AgentApi } from "./api";
import { initialGovernorState, nextSlot, recordOutcome, recordRequest, type GovernorPolicy, type Outcome } from "./governor";
import { governorItem, runningItem, sessionsItem } from "./storage";
import type { MarketplaceTab } from "./transport";
import type { FailureKind, Heartbeat, PlatformPolicy, SyncRun, Work } from "./types";

export interface RunnerDeps {
  api: AgentApi;
  openTab(workerUrl: string): Promise<MarketplaceTab>;
  now(): Date;
  sleep(ms: number): Promise<void>;
}

const ordersPerBatch = 200;
const listingsPerBatch = 500;

/** Whether this browser has been allowed to reach the marketplace. */
export async function hasAccess(adapter: MarketplaceAdapter): Promise<boolean> {
  return browser.permissions.contains({ origins: adapter.origins });
}

/**
 * Wraps a marketplace tab so every request first passes the governor: it waits out the spacing,
 * counts toward the caps, and refuses outright while the marketplace is paused.
 */
export function governedContext(tab: MarketplaceTab, platform: string, policy: GovernorPolicy, deps: RunnerDeps): MarketplaceContext {
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
    return go();
  };

  return {
    now: () => deps.now(),
    page: (url: string) => paced(() => tab.page(url)),
    request: (url: string) => paced(() => tab.request(url)),
  };
}

async function learn(platform: string, outcome: Outcome, now: Date): Promise<void> {
  const states = await governorItem.getValue();
  await governorItem.setValue({ ...states, [platform]: recordOutcome(states[platform] ?? initialGovernorState(), outcome, now.getTime(), platform) });
}

function outcomeFor(kind: FailureKind): Outcome {
  switch (kind) {
    case "RateLimited":
      return "rate-limited";
    case "Challenge":
      return "challenge";
    case "SiteChanged":
      return "site-changed";
    default:
      return "failed";
  }
}

function chunks<T>(items: T[], size: number): T[][] {
  const result: T[][] = [];
  for (let index = 0; index < items.length; index += size) {
    result.push(items.slice(index, index + size));
  }

  return result.length > 0 ? result : [[]];
}

/** A quiet run — nothing new, nothing wrong — is recorded on the server but not repeated in the popup. */
async function report(account: string, what: string, done: SyncRun): Promise<void> {
  const changed = (done.created ?? 0) + (done.updated ?? 0) + (done.rejected ?? 0) > 0;
  if (changed || done.status !== "Succeeded") {
    await logActivity(done.status === "Succeeded" ? "success" : "warning", `${account} ${what}: ${done.summary ?? "done"}`);
  }
}

function describe(work: Work, account: string): string {
  return `${account} ${work.loop === "Listings" ? "listings" : work.from ? "sales history" : "sales"}`;
}

/**
 * Runs one piece of work from start to finish: open a marketplace tab, check who is signed in, take
 * the lease, read, deliver in batches, and close the run — or fail it with a reason the hub can
 * show. Whatever happens, the tab is closed and the governor learns from the outcome.
 */
export async function runWork(work: Work, heartbeat: Heartbeat, deps: RunnerDeps): Promise<void> {
  const adapter = adapterFor(work.platform);
  const policy: PlatformPolicy | undefined = heartbeat.platforms?.find((item) => item.platform === work.platform);
  const account = heartbeat.accounts?.find((item) => item.channelAccountId === work.channelAccountId)?.displayName ?? work.platform ?? "An account";
  if (!adapter || !policy?.enabled || !work.channelAccountId || !work.loop) {
    return;
  }

  const limits: GovernorPolicy = {
    minSecondsBetweenRequests: policy.minSecondsBetweenRequests ?? 3,
    maxRequestsPerMinute: policy.maxRequestsPerMinute ?? 12,
    maxRequestsPerDay: policy.maxRequestsPerDay ?? 1500,
  };
  const state = (await governorItem.getValue())[adapter.platform] ?? initialGovernorState();
  const slot = nextSlot(state, limits, deps.now().getTime(), () => 0);
  if (!slot.allowed || !(await hasAccess(adapter))) {
    return;
  }

  const tab = await deps.openTab(adapter.workerUrl);
  let runId: string | undefined;
  try {
    const context = governedContext(tab, adapter.platform, limits, deps);
    const session = await adapter.detectSession(context);
    const sessions = await sessionsItem.getValue();
    await sessionsItem.setValue({
      ...sessions,
      [adapter.platform]: { ...session, checkedAt: deps.now().toISOString() },
    });
    if (!session.signedIn || !session.username) {
      await logActivity("warning", `Sign in to ${adapter.platform} in this browser to sync ${account}.`);
      return;
    }

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
      runId = started.runId;
      cursor = started.cursor ?? null;
      from = started.from ?? from;
    } catch (error) {
      // Another browser has it — that is the lease doing its job, not a problem.
      if (error instanceof ApiError && error.code === "sync_lease_held") {
        return;
      }

      await logActivity("warning", error instanceof Error ? error.message : `Could not start ${describe(work, account)}.`);
      return;
    }

    await runningItem.setValue({ label: `Reading ${describe(work, account)}`, startedAt: deps.now().toISOString() });
    if (work.loop === "Sales") {
      const run = await adapter.scanSales(context, { from: from ? new Date(from) : null, cursor });
      let first = true;
      for (const orders of chunks(run.orders, ordersPerBatch)) {
        await deps.api.addBatch(runId!, { orders, issues: first ? run.issues : [] });
        first = false;
      }

      await report(account, "sales", await deps.api.complete(runId!, { cursor: run.cursor, coversFrom: run.coversFrom, coversTo: run.coversTo }));
    } else {
      const snapshot = await adapter.snapshotListings(context);
      let first = true;
      for (const listings of chunks(snapshot.listings, listingsPerBatch)) {
        await deps.api.addBatch(runId!, { listings, issues: first ? snapshot.issues : [] });
        first = false;
      }

      await report(account, "listings", await deps.api.complete(runId!, { completeSnapshot: snapshot.complete }));
    }

    await learn(adapter.platform, "ok", deps.now());
  } catch (error) {
    // The lease went to another browser, or the run was closed under us: stop, and say nothing
    // to the server — the run is already someone else's to finish.
    if (error instanceof ApiError && (error.code === "sync_lease_lost" || error.code === "sync_run_finished")) {
      return;
    }

    const kind: FailureKind = error instanceof AdapterError ? error.kind : error instanceof ApiError && error.status === 0 ? "Network" : "Error";
    const message = error instanceof Error ? error.message : String(error);
    if (runId) {
      await deps.api.fail(runId, kind, message).catch(() => undefined);
    }

    await learn(adapter.platform, outcomeFor(kind), deps.now());
    await logActivity("error", `${describe(work, account)} stopped: ${message}`);
  } finally {
    await runningItem.setValue(null);
    await tab.close();
  }
}
