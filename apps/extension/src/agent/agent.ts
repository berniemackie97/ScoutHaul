import { browser } from "wxt/browser";
import { adapterFor, adapters } from "../adapters/registry";
import type { MarketplaceAdapter } from "../adapters/types";
import { logActivity } from "./activity";
import { ApiError, createAgentApi, type AgentApi } from "./api";
import { aliveBeatMs, recheckMs, signedInFreshMs } from "./config";
import { describeBrowser } from "./environment";
import { describeFailure, waitsForAPerson } from "./failures";
import { pauseOf, resume, type GovernorPolicy } from "./governor";
import { forgetAdapterMemory } from "./memory";
import {
  checkSession,
  describeWork,
  governedContext,
  hasAccess,
  heldBack,
  kindOf,
  learn,
  limitsFor,
  notStarted,
  preflight,
  rememberSession,
  runWork,
  type RunnerDeps,
  type RunResult,
} from "./runner";
import {
  activityItem,
  aliveItem,
  authProblemItem,
  connectionItem,
  governorItem,
  heartbeatItem,
  lastRunsItem,
  pairingItem,
  queuedItem,
  runningItem,
  sessionsItem,
  updateGovernor,
  workKey,
  type StoredPairing,
} from "./storage";
import { SyncTabs, type BorrowedTab, type MarketplaceTab } from "./transport";
import type { Heartbeat, LoopKind, MarketplaceReport, Work } from "./types";

const version = () => browser.runtime.getManifest().version;

/** Where the agent gets marketplace tabs from: its own sync tabs, or one the operator has open. */
export interface TabSource {
  acquire(adapter: MarketplaceAdapter): Promise<MarketplaceTab>;
  release(adapter: MarketplaceAdapter): void;
  borrow(adapter: MarketplaceAdapter, tabId?: number): Promise<BorrowedTab | null>;
  owns(tabId: number): boolean;
}

export type AgentDeps = RunnerDeps & { tabs: TabSource };

/**
 * The sync agent: checks in with HaulScout, runs whatever is due, and answers the popup.
 *
 * One of it runs in the background, and it does one marketplace thing at a time — a scan, a sign-in
 * check — in turn, so two can never fight over a tab. Check-ins coalesce: an alarm, a wake from the
 * portal and a click in the popup arriving together make one check-in.
 */
export class Agent {
  readonly api: AgentApi;
  private readonly deps: AgentDeps;
  private checking: Promise<void> | null = null;
  private turn: Promise<unknown> = Promise.resolve();
  private readonly lastChecked = new Map<string, number>();
  private readonly pendingChecks = new Map<string, ReturnType<typeof setTimeout>>();

  constructor(deps?: Partial<AgentDeps>) {
    this.api = deps?.api ?? createAgentApi(async () => (await pairingItem.getValue())?.token ?? null);
    this.deps = {
      api: this.api,
      tabs: deps?.tabs ?? new SyncTabs(),
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
    await logActivity("success", `Connected to ${pairing.organizationName} as "${pairing.agentName}".`, this.deps.now());
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
    if (!(await pairingItem.getValue())) {
      return;
    }

    // A marketplace whose sign-in has never been read (the first check failed, say) is looked at
    // again now and then, so it recovers without anyone having to notice.
    await this.refreshSessions({ unknownOnly: true });
    const heartbeat = await this.heartbeat();
    const work = heartbeat?.work ?? [];
    if (!heartbeat || work.length === 0) {
      return;
    }

    // Anything that turned up while working — a sign-out, a pause — is told to HaulScout at once, so
    // it holds the work back and the portal says why, rather than a minute later.
    if (await this.runAll(work, heartbeat)) {
      await this.heartbeat();
    }
  }

  /**
   * A scan asked for in the popup. It waits its turn behind whatever is running, then runs in the
   * sync tab; the popup follows it from storage, so it need not stay open.
   */
  async scanNow(channelAccountId: string, loop: LoopKind): Promise<void> {
    const key = workKey(channelAccountId, loop);
    await this.enqueue(key);
    try {
      let heartbeat = await heartbeatItem.getValue();
      if (!heartbeat || this.deps.now().getTime() - Date.parse(heartbeat.receivedAt) > 2 * 60 * 1000) {
        heartbeat = (await this.heartbeat()) ? await heartbeatItem.getValue() : heartbeat;
      }

      const account = heartbeat?.accounts?.find((item) => item.channelAccountId === channelAccountId);
      const adapter = adapterFor(account?.platform);
      if (!heartbeat || !account || !adapter) {
        await logActivity("warning", "That account is not one this browser can sync.", this.deps.now());
        return;
      }

      const work: Work = { channelAccountId, platform: adapter.platform, loop, trigger: "Extension", scanRequestId: null, from: null };
      await this.exclusive(() => this.session(adapter, heartbeat, [work]));
    } finally {
      await this.dequeue(key);
    }
  }

  /**
   * Checks who this browser is signed in as on each marketplace it may reach, then tells HaulScout.
   * `force` checks even an answer that is still fresh — "Refresh" in the popup.
   */
  async checkSessions(options: { platform?: string; tabId?: number; force?: boolean } = {}): Promise<void> {
    await this.refreshSessions(options);
    await this.checkIn();
  }

  /** The popup opened: a sign-in answer it would show as stale, or as a problem, is looked at again. */
  async popupOpened(): Promise<void> {
    await this.refreshSessions({ staleOnly: true });
    await this.checkIn();
  }

  /**
   * A marketplace page finished loading in one of the operator's own tabs. If this browser is not
   * known to be signed in there, or is paused for a security check, that may just have changed — they
   * signed in, or cleared the check — so it looks again, from that very tab, once the pages settle.
   */
  async noticePage(tabId: number, url: string): Promise<void> {
    const adapter = adapters.find((candidate) => candidate.readable(url));
    if (!adapter || this.deps.tabs.owns(tabId) || !(await pairingItem.getValue())) {
      return;
    }

    const kind = adapter.pageKind(url);
    if (kind === "sign-in" || kind === "challenge") {
      return;
    }

    const now = this.deps.now().getTime();
    const session = (await sessionsItem.getValue())[adapter.platform];
    const pause = pauseOf((await governorItem.getValue())[adapter.platform], now);
    const checkCleared = pause?.kind === "challenge" && kind === "seller";
    if (!checkCleared && session?.signedIn) {
      return;
    }

    const wait = Math.max(1500, recheckMs - (now - (this.lastChecked.get(adapter.platform) ?? 0)));
    clearTimeout(this.pendingChecks.get(adapter.platform));
    this.pendingChecks.set(adapter.platform, setTimeout(() => {
      this.pendingChecks.delete(adapter.platform);
      void (async () => {
        if (checkCleared) {
          await this.resumePlatform(adapter.platform, `${adapter.platform}'s security check is done. Syncing picks up again.`);
        }

        await this.checkSessions({ platform: adapter.platform, tabId, force: true });
      })().catch((error: unknown) => console.warn("HaulScout Sync could not look at the sign-in again.", error));
    }, wait));
  }

  /** Lifts a pause by hand — the operator has dealt with whatever caused it. */
  async resume(platform: string): Promise<void> {
    await this.resumePlatform(platform, `${platform} resumed.`);
    await this.checkSessions({ platform, force: true });
  }

  /** Disconnects this browser: HaulScout stops accepting its token, and it forgets everything. */
  async disconnect(): Promise<void> {
    await this.api.disconnect().catch(() => undefined);
    await this.forget();
  }

  private async resumePlatform(platform: string, message: string): Promise<void> {
    await updateGovernor(platform, resume);
    await logActivity("info", message, this.deps.now());
  }

  /** One marketplace thing at a time, in the order asked for. */
  private exclusive<T>(work: () => Promise<T>): Promise<T> {
    const next = this.turn.then(work, work);
    this.turn = next.catch(() => undefined);
    return next;
  }

  /** Tells HaulScout what this browser can see, and learns what it should do. Null when that failed. */
  private async heartbeat(): Promise<Heartbeat | null> {
    const pairing = await pairingItem.getValue();
    if (!pairing) {
      return null;
    }

    let heartbeat: Heartbeat;
    try {
      heartbeat = await this.api.heartbeat({ extensionVersion: version(), ...describeBrowser(), marketplaces: await this.marketplaceReports() });
    } catch (error) {
      await this.handleApiFailure(error);
      return null;
    }

    await pairingItem.setValue({
      ...pairing,
      token: heartbeat.newToken ?? pairing.token,
      agentName: heartbeat.agentName ?? pairing.agentName,
      organizationName: heartbeat.organizationName ?? pairing.organizationName,
    });
    await heartbeatItem.setValue({ ...heartbeat, newToken: null, receivedAt: this.deps.now().toISOString() });
    await authProblemItem.setValue(null);
    await this.connected(true, null);
    return heartbeat;
  }

  /** Runs the work a heartbeat handed out, a marketplace at a time. True when HaulScout should hear about something at once. */
  private async runAll(work: Work[], heartbeat: Heartbeat): Promise<boolean> {
    let tell = false;
    for (const adapter of adapters) {
      const mine = work.filter((item) => adapterFor(item.platform) === adapter);
      if (mine.length > 0) {
        tell = (await this.exclusive(() => this.session(adapter, heartbeat, mine))) || tell;
      }
    }

    return tell;
  }

  /**
   * Runs work for one marketplace in its sync tab, one piece after another. Work that cannot run
   * anyway opens nothing. Returns whether something turned up that HaulScout should hear at once.
   */
  private async session(adapter: MarketplaceAdapter, heartbeat: Heartbeat, work: Work[]): Promise<boolean> {
    const runnable: Work[] = [];
    for (const item of work) {
      const blocked = await preflight(item, heartbeat, this.deps.now());
      if (!blocked) {
        runnable.push(item);
      } else if (item.trigger === "Extension") {
        // Asked for in the popup: say why not. Work HaulScout handed out that cannot run is already
        // explained there, from the heartbeat.
        await this.record(item, heartbeat, blocked);
      }
    }

    if (runnable.length === 0) {
      return false;
    }

    const stop = keepAlive();
    let tab: MarketplaceTab;
    try {
      await this.running(`Opening ${adapter.platform}`, null);
      tab = await this.deps.tabs.acquire(adapter);
    } catch (error) {
      for (const item of runnable) {
        await this.record(item, heartbeat, await notStarted(adapter, item, kindOf(error), null, this.deps));
      }

      await runningItem.setValue(null);
      stop();
      this.deps.tabs.release(adapter);
      return false;
    }

    let tell = false;
    try {
      for (const item of runnable) {
        const account = heartbeat.accounts?.find((candidate) => candidate.channelAccountId === item.channelAccountId);
        await this.running(`Reading ${describeWork(item, account)}`, workKey(item.channelAccountId!, item.loop!));
        const result = await runWork(item, heartbeat, tab, this.deps, () => void this.countPage());
        await this.record(item, heartbeat, result);
        if (result.status === "failed" && (waitsForAPerson(result.kind) || result.kind === "SiteChanged")) {
          // The rest would stop the same way; HaulScout holds them back once it hears why.
          tell = true;
          break;
        }
      }
    } finally {
      await runningItem.setValue(null);
      stop();
      this.deps.tabs.release(adapter);
    }

    return tell;
  }

  /**
   * How a piece of work ended, for the popup: the account's last result for that loop, and a line
   * in the activity — always for a scan someone asked for, and for a scheduled one only when it
   * found something or something went wrong, so a quiet schedule stays quiet.
   */
  private async record(work: Work, heartbeat: Heartbeat, result: RunResult): Promise<void> {
    if (result.status === "skipped" || !work.channelAccountId || !work.loop) {
      return;
    }

    const account = heartbeat.accounts?.find((item) => item.channelAccountId === work.channelAccountId);
    const at = this.deps.now();
    const tone = result.status === "done"
      ? result.issues ? "warning" : "success"
      : waitsForAPerson(result.kind) ? "warning" : "error";
    await lastRunsItem.setValue({
      ...(await lastRunsItem.getValue()),
      [workKey(work.channelAccountId, work.loop)]: { at: at.toISOString(), tone, text: result.text },
    });

    const asked = work.trigger !== "Scheduled";
    if (result.status === "failed" || result.changed || asked) {
      await logActivity(tone, `${describeWork(work, account)}: ${result.text}`, at);
    }
  }

  /**
   * Looks at who this browser is signed in as. `unknownOnly` looks only where that has never been
   * read, now and then; `staleOnly` where the answer has aged, or is a problem worth a second look.
   */
  private async refreshSessions(options: { platform?: string; tabId?: number; force?: boolean; unknownOnly?: boolean; staleOnly?: boolean }): Promise<void> {
    const heartbeat = await heartbeatItem.getValue();
    const sessions = await sessionsItem.getValue();
    for (const adapter of adapters) {
      if (options.platform && adapter.platform !== options.platform) {
        continue;
      }

      const now = this.deps.now().getTime();
      const session = sessions[adapter.platform];
      const sinceTried = now - (this.lastChecked.get(adapter.platform) ?? 0);
      const age = session ? now - Date.parse(session.checkedAt) : Number.POSITIVE_INFINITY;
      const due = options.force
        || (options.unknownOnly ? !session && sinceTried > 10 * 60 * 1000
          : options.staleOnly ? sinceTried > recheckMs && (session?.signedIn ? age > signedInFreshMs : age > recheckMs)
            : sinceTried > recheckMs);
      if (!due || !(await hasAccess(adapter))) {
        continue;
      }

      const limits = limitsFor(heartbeat, adapter.platform);
      if (await heldBack(adapter.platform, limits, this.deps.now())) {
        continue;
      }

      // A browser known to be signed out is not knocked on again unprompted: the marketplace sees a stranger asking,
      // which is what bot protection is for. A tab the operator has open still tells, for free, when they sign in.
      const ownTabOnly = !options.force && session?.signedIn === false;
      await this.exclusive(() => this.checkSignIn(adapter, options.tabId, ownTabOnly));
    }
  }

  /**
   * One sign-in check: through a tab the operator already has open on the marketplace when there
   * is one — just after they sign in, the very tab they signed in with — or else the sync tab.
   */
  private async checkSignIn(adapter: MarketplaceAdapter, tabId?: number, ownTabOnly = false): Promise<void> {
    this.lastChecked.set(adapter.platform, this.deps.now().getTime());
    const limits = limitsFor(await heartbeatItem.getValue(), adapter.platform);
    const stop = keepAlive();
    try {
      await this.running(`Checking the ${adapter.platform} sign-in`, null);
      const borrowed = await this.deps.tabs.borrow(adapter, tabId);
      if (borrowed) {
        try {
          if (await this.signInFrom(adapter, borrowed, limits)) {
            return;
          }
        } catch {
          // Their tab moved on mid-check; the sync tab answers instead.
        }
      }

      if (ownTabOnly) {
        return;
      }

      try {
        await checkSession(adapter, governedContext(await this.deps.tabs.acquire(adapter), adapter.platform, limits, this.deps));
      } finally {
        this.deps.tabs.release(adapter);
      }
    } catch (error) {
      const kind = kindOf(error);
      await learn(adapter.platform, kind, this.deps.now());
      await logActivity(waitsForAPerson(kind) ? "warning" : "error", `Could not check the ${adapter.platform} sign-in. ${describeFailure(kind, adapter.platform)}`, this.deps.now());
    } finally {
      stop();
      await runningItem.setValue(null);
    }
  }

  /**
   * The sign-in as the operator's own tab shows it: read where it stands when the page tells, or asked of the
   * marketplace from it. False when the page cannot tell, so the sync tab answers.
   */
  private async signInFrom(adapter: MarketplaceAdapter, borrowed: BorrowedTab, limits: GovernorPolicy): Promise<boolean> {
    if (!adapter.sessionFrom) {
      await checkSession(adapter, governedContext(borrowed, adapter.platform, limits, this.deps));
      return true;
    }

    const session = adapter.sessionFrom(await borrowed.current(adapter.sessionGlobal));
    if (!session) {
      return false;
    }

    await rememberSession(adapter, session, this.deps.now());
    return true;
  }

  private async running(label: string, key: string | null): Promise<void> {
    await runningItem.setValue({ label, key, startedAt: this.deps.now().toISOString(), pages: 0 });
  }

  private async countPage(): Promise<void> {
    const running = await runningItem.getValue();
    if (running) {
      await runningItem.setValue({ ...running, pages: running.pages + 1 });
    }
  }

  private async enqueue(key: string): Promise<void> {
    const queued = await queuedItem.getValue();
    if (!queued.includes(key)) {
      await queuedItem.setValue([...queued, key]);
    }
  }

  private async dequeue(key: string): Promise<void> {
    await queuedItem.setValue((await queuedItem.getValue()).filter((item) => item !== key));
  }

  private async forget(): Promise<void> {
    await Promise.all([
      pairingItem.setValue(null),
      heartbeatItem.setValue(null),
      runningItem.setValue(null),
      queuedItem.setValue([]),
      lastRunsItem.setValue({}),
      connectionItem.setValue(null),
      activityItem.setValue([]),
      forgetAdapterMemory(),
    ]);
  }

  private async connected(ok: boolean, message: string | null): Promise<void> {
    const current = await connectionItem.getValue();
    if (current?.ok !== ok || current.message !== message) {
      await connectionItem.setValue({ ok, message, since: current?.ok === ok ? current.since : this.deps.now().toISOString() });
    }
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
      return;
    }

    await this.connected(false, error.status === 0 || error.status >= 500 ? "HaulScout could not be reached." : error.message);
  }

  private async marketplaceReports(): Promise<MarketplaceReport[]> {
    const sessions = await sessionsItem.getValue();
    const governors = await governorItem.getValue();
    const now = this.deps.now().getTime();
    return Promise.all(
      adapters.map(async (adapter) => {
        const session = sessions[adapter.platform];
        const pause = pauseOf(governors[adapter.platform], now);
        return {
          platform: adapter.platform,
          accessGranted: await hasAccess(adapter),
          signedIn: session?.signedIn ?? null,
          username: session?.username ?? null,
          marketplaceUserId: session?.marketplaceUserId ?? null,
          pausedUntil: pause ? new Date(pause.until).toISOString() : null,
          pauseReason: pause?.reason ?? null,
        };
      }),
    );
  }
}

/**
 * A browser stops an extension's background when it looks idle — even mid-scan, while it waits on a
 * page. Any call into the browser counts as activity, so a light one every 20 seconds keeps a long
 * scan from being cut off part-way.
 */
function keepAlive(): () => void {
  const beat = () => void aliveItem.setValue(new Date().toISOString());
  beat();
  const timer = setInterval(() => {
    beat();
    try {
      void browser.runtime.getPlatformInfo().catch(() => undefined);
    } catch {
      // Nothing to keep alive where there is no such call.
    }
  }, aliveBeatMs);
  return () => clearInterval(timer);
}
