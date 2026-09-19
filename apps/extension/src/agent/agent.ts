import { browser } from "wxt/browser";
import { adapters } from "../adapters/registry";
import { AdapterError } from "../adapters/types";
import { logActivity } from "./activity";
import { ApiError, createAgentApi, type AgentApi } from "./api";
import { describeBrowser } from "./environment";
import { initialGovernorState, nextSlot, resume } from "./governor";
import { governedContext, hasAccess, runWork, type RunnerDeps } from "./runner";
import {
  activityItem,
  authProblemItem,
  governorItem,
  heartbeatItem,
  pairingItem,
  runningItem,
  sessionsItem,
  type StoredPairing,
} from "./storage";
import { openMarketplaceTab } from "./transport";
import type { Heartbeat, LoopKind, MarketplaceReport, Work } from "./types";

const version = () => browser.runtime.getManifest().version;

/**
 * The sync agent: checks in with HaulScout, runs whatever is due, and answers the popup.
 *
 * One of it runs in the background. Check-ins coalesce — an alarm, a wake from the portal and a
 * click in the popup arriving together make one check-in, never three overlapping runs.
 */
export class Agent {
  private checking: Promise<void> | null = null;
  readonly api: AgentApi;
  private readonly deps: RunnerDeps;

  constructor(deps?: Partial<RunnerDeps>) {
    this.api = deps?.api ?? createAgentApi(async () => (await pairingItem.getValue())?.token ?? null);
    this.deps = {
      api: this.api,
      openTab: deps?.openTab ?? openMarketplaceTab,
      now: deps?.now ?? (() => new Date()),
      sleep: deps?.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms))),
    };
  }

  /** Pairs this browser with the one-time code the portal showed. */
  async pair(code: string, name: string | null): Promise<StoredPairing> {
    const { browser: browserName, operatingSystem } = describeBrowser();
    const paired = await this.api.pair(code, browserName, version(), operatingSystem, name);
    const pairing: StoredPairing = {
      agentId: paired.agentId!,
      agentName: paired.agentName ?? browserName,
      organizationName: paired.organizationName ?? "HaulScout",
      token: paired.token!,
      pairedAt: this.deps.now().toISOString(),
    };
    await pairingItem.setValue(pairing);
    await authProblemItem.setValue(null);
    await logActivity("success", `Connected to ${pairing.organizationName} as "${pairing.agentName}".`);
    return pairing;
  }

  /** Checks in now, or joins the check-in already under way. */
  checkIn(): Promise<void> {
    this.checking ??= this.checkInOnce().finally(() => {
      this.checking = null;
    });
    return this.checking;
  }

  private async checkInOnce(): Promise<void> {
    const pairing = await pairingItem.getValue();
    if (!pairing) {
      return;
    }

    const request = { extensionVersion: version(), ...describeBrowser(), marketplaces: await this.marketplaceReports() };
    let heartbeat: Heartbeat;
    try {
      heartbeat = await this.api.heartbeat(request);
    } catch (error) {
      await this.handleApiFailure(error);
      return;
    }

    if (heartbeat.newToken) {
      await pairingItem.setValue({ ...pairing, token: heartbeat.newToken });
    }

    await pairingItem.setValue({
      ...(await pairingItem.getValue())!,
      agentName: heartbeat.agentName ?? pairing.agentName,
      organizationName: heartbeat.organizationName ?? pairing.organizationName,
    });
    await heartbeatItem.setValue({ ...heartbeat, newToken: null, receivedAt: this.deps.now().toISOString() });
    await authProblemItem.setValue(null);

    for (const work of heartbeat.work ?? []) {
      await runWork(work, heartbeat, this.deps);
    }
  }

  /** "Scan sales now" from the popup: runs straight away, as this browser's own request. */
  async scanNow(channelAccountId: string, loop: LoopKind): Promise<void> {
    await this.checkIn();
    const heartbeat = await heartbeatItem.getValue();
    const account = heartbeat?.accounts?.find((item) => item.channelAccountId === channelAccountId);
    if (!heartbeat || !account) {
      throw new Error("That account is not one this browser can sync.");
    }

    const work: Work = { channelAccountId, platform: account.platform, loop, trigger: "Extension", scanRequestId: null, from: null };
    await runWork(work, heartbeat, this.deps);
  }

  /**
   * Checks who this browser is signed in as on each marketplace it may reach — after access is
   * granted, and when the popup opens with a stale answer — then tells the server.
   */
  async checkSessions(): Promise<void> {
    const heartbeat = await heartbeatItem.getValue();
    for (const adapter of adapters) {
      if (!(await hasAccess(adapter))) {
        continue;
      }

      const policy = heartbeat?.platforms?.find((item) => item.platform === adapter.platform);
      const limits = {
        minSecondsBetweenRequests: policy?.minSecondsBetweenRequests ?? 3,
        maxRequestsPerMinute: policy?.maxRequestsPerMinute ?? 12,
        maxRequestsPerDay: policy?.maxRequestsPerDay ?? 1500,
      };
      const state = (await governorItem.getValue())[adapter.platform] ?? initialGovernorState();
      if (!nextSlot(state, limits, this.deps.now().getTime(), () => 0).allowed) {
        continue;
      }

      const tab = await this.deps.openTab(adapter.workerUrl);
      try {
        const session = await adapter.detectSession(governedContext(tab, adapter.platform, limits, this.deps));
        await sessionsItem.setValue({
          ...(await sessionsItem.getValue()),
          [adapter.platform]: { ...session, checkedAt: this.deps.now().toISOString() },
        });
      } catch (error) {
        const message = error instanceof AdapterError || error instanceof Error ? error.message : String(error);
        await logActivity("warning", `Could not check the ${adapter.platform} sign-in: ${message}`);
      } finally {
        await tab.close();
      }
    }

    await this.checkIn();
  }

  /** Lifts a governor pause by hand — the operator has dealt with whatever caused it. */
  async resume(platform: string): Promise<void> {
    const states = await governorItem.getValue();
    await governorItem.setValue({ ...states, [platform]: resume(states[platform] ?? initialGovernorState()) });
    await logActivity("info", `${platform} resumed.`);
    await this.checkIn();
  }

  /** Disconnects this browser: HaulScout stops accepting its token, and it forgets everything. */
  async disconnect(): Promise<void> {
    await this.api.disconnect().catch(() => undefined);
    await this.forget();
  }

  private async forget(): Promise<void> {
    await Promise.all([
      pairingItem.setValue(null),
      heartbeatItem.setValue(null),
      runningItem.setValue(null),
      activityItem.setValue([]),
    ]);
  }

  /** A failed call to HaulScout. Anything that is not one is a bug here, and is not hidden as one. */
  private async handleApiFailure(error: unknown): Promise<void> {
    if (!(error instanceof ApiError)) {
      throw error;
    }

    if (error.disconnected) {
      // The token no longer works — revoked, expired, or its owner left. Nothing will work until the
      // browser is connected again, so say exactly that rather than failing quietly every minute.
      await this.forget();
      await authProblemItem.setValue({ code: error.code ?? "agent_unauthenticated", message: error.message, at: this.deps.now().toISOString() });
    }
  }

  private async marketplaceReports(): Promise<MarketplaceReport[]> {
    const sessions = await sessionsItem.getValue();
    const governors = await governorItem.getValue();
    const now = this.deps.now().getTime();
    return Promise.all(
      adapters.map(async (adapter) => {
        const session = sessions[adapter.platform];
        const governor = governors[adapter.platform];
        const paused = governor?.pausedUntil !== null && governor?.pausedUntil !== undefined && governor.pausedUntil > now;
        return {
          platform: adapter.platform,
          accessGranted: await hasAccess(adapter),
          signedIn: session?.signedIn ?? null,
          username: session?.username ?? null,
          marketplaceUserId: session?.marketplaceUserId ?? null,
          pausedUntil: paused ? new Date(governor!.pausedUntil!).toISOString() : null,
          pauseReason: paused ? governor!.pauseReason : null,
        };
      }),
    );
  }
}

