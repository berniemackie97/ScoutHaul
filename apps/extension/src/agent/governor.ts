/**
 * The safety governor: how hard the agent may press a marketplace.
 *
 * Every marketplace request goes through it. It spaces requests out with random jitter, holds a
 * per-minute and a per-day cap, and pauses the marketplace when it pushes back — a rate limit, a
 * security check, or failures in a row. The limits come from the server, so they can be tightened
 * for every browser at once. Pure functions over plain state, so the rules are tested without a
 * browser and the state survives the background worker being stopped.
 */

export interface GovernorPolicy {
  minSecondsBetweenRequests: number;
  maxRequestsPerMinute: number;
  maxRequestsPerDay: number;
}

export interface GovernorState {
  lastRequestAt: number | null;
  /** Request times within the last minute. */
  recent: number[];
  /** Local calendar day the daily count belongs to, YYYY-MM-DD. */
  day: string;
  dayCount: number;
  pausedUntil: number | null;
  pauseReason: string | null;
  /** What the pause is for — a security check is cleared by a person, a slow-down only by time. */
  pausedFor?: Exclude<Outcome, "ok"> | null;
  /** Failures in a row; a success clears them. */
  strikes: number;
  /** No traffic before this: the operator is signing in, or has just signed in or cleared a security check. */
  quietUntil?: number | null;
  quietReason?: string | null;
  /** Security checks in a row. Each holds off longer than the last, and only a scan that succeeds clears them. */
  challenges?: number;
  /** Sign-in checks that reached the marketplace in the last hour. */
  checks?: number[];
  /** Sign-in checks in a row that ended in trouble rather than an answer. Each waits longer for the next. */
  checkMisses?: number;
}

export type Outcome = "ok" | "rate-limited" | "challenge" | "site-changed" | "failed";

export type Slot = { allowed: true; waitMs: number } | { allowed: false; until: number; reason: string };

const minute = 60_000;
const hour = 60 * minute;

/** The most sign-in checks that may reach a marketplace in an hour, however they were asked for. */
export const maxChecksPerHour = 4;

/** How long a security-check pause lasts, by how many came in a row. A block pressed on only hardens. */
const challengePauses = [hour, 4 * hour, 12 * hour, 24 * hour];

export function initialGovernorState(): GovernorState {
  return { lastRequestAt: null, recent: [], day: "", dayCount: 0, pausedUntil: null, pauseReason: null, pausedFor: null, strikes: 0 };
}

export function dayKey(now: number): string {
  const date = new Date(now);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

function startOfNextDay(now: number): number {
  const date = new Date(now);
  date.setHours(24, 0, 0, 0);
  return date.getTime();
}

/** Whether a request may go now, and if so how long to wait first. */
export function nextSlot(state: GovernorState, policy: GovernorPolicy, now: number, random: () => number = Math.random): Slot {
  if (state.pausedUntil !== null && state.pausedUntil > now) {
    return { allowed: false, until: state.pausedUntil, reason: state.pauseReason ?? "Paused." };
  }

  if (state.quietUntil && state.quietUntil > now) {
    return { allowed: false, until: state.quietUntil, reason: state.quietReason ?? "Holding off for a moment." };
  }

  const dayCount = state.day === dayKey(now) ? state.dayCount : 0;
  if (dayCount >= policy.maxRequestsPerDay) {
    return { allowed: false, until: startOfNextDay(now), reason: "Reached today's request limit for this marketplace." };
  }

  const recent = state.recent.filter((at) => at > now - minute);
  const windowWait = recent.length >= policy.maxRequestsPerMinute ? recent[0]! + minute - now : 0;

  // Jitter makes the spacing irregular, the way a person's browsing is, rather than a metronome.
  const gap = policy.minSecondsBetweenRequests * 1000 * (1 + random() * 0.5);
  const spacingWait = state.lastRequestAt === null ? 0 : Math.max(0, state.lastRequestAt + gap - now);

  return { allowed: true, waitMs: Math.max(windowWait, spacingWait) };
}

export function recordRequest(state: GovernorState, now: number): GovernorState {
  const today = dayKey(now);
  return {
    ...state,
    lastRequestAt: now,
    recent: [...state.recent.filter((at) => at > now - minute), now],
    day: today,
    dayCount: (state.day === today ? state.dayCount : 0) + 1,
  };
}

/** Learns from how a request or run ended; pushing back pauses the marketplace. */
export function recordOutcome(state: GovernorState, outcome: Outcome, now: number, platform: string): GovernorState {
  switch (outcome) {
    case "ok":
      return { ...state, strikes: 0, challenges: 0 };
    case "rate-limited": {
      const strikes = state.strikes + 1;
      const pause = Math.min(15 * minute * 2 ** (strikes - 1), 6 * hour);
      return {
        ...state,
        strikes,
        pausedUntil: now + pause,
        pausedFor: outcome,
        pauseReason: `${platform} asked for a slower pace, so the extension is holding off.`,
      };
    }
    case "challenge": {
      // A person clears this one — the pause lifts once their own tab shows it cleared, or after a while regardless,
      // longer each time it comes back before a scan has gone through.
      const challenges = (state.challenges ?? 0) + 1;
      return {
        ...state,
        strikes: state.strikes + 1,
        challenges,
        pausedUntil: now + challengePauses[Math.min(challenges, challengePauses.length) - 1]!,
        pausedFor: outcome,
        pauseReason: `${platform} showed a security check. Complete it on ${platform} in the browser that syncs, and syncing picks up by itself.`,
      };
    }
    case "site-changed":
      // Reading a changed page wrong is worse than not reading it: hold off until the adapter is fixed.
      return {
        ...state,
        strikes: state.strikes + 1,
        pausedUntil: now + 6 * hour,
        pausedFor: outcome,
        pauseReason: `${platform}'s pages changed and the extension could not read them. It is paused until an update.`,
      };
    case "failed": {
      const strikes = state.strikes + 1;
      return strikes >= 3
        ? { ...state, strikes, pausedUntil: now + 30 * minute, pausedFor: outcome, pauseReason: `Paused for a while after ${strikes} failed attempts in a row.` }
        : { ...state, strikes };
    }
  }
}

/**
 * Clears a pause — "Resume now" in the popup. The run of security checks is kept, so one that comes straight back
 * holds off longer; only a scan that goes through clears it.
 */
export function resume(state: GovernorState): GovernorState {
  return { ...state, pausedUntil: null, pauseReason: null, pausedFor: null, strikes: 0, quietUntil: null, quietReason: null };
}

/** Holds all traffic off until `until` — never shortening a hold already longer. */
export function quiet(state: GovernorState, until: number, reason: string): GovernorState {
  return (state.quietUntil ?? 0) >= until ? state : { ...state, quietUntil: until, quietReason: reason };
}

/**
 * The operator's own tab shows a security check cleared. The pause lifts, but nothing is asked of the marketplace for
 * a few minutes: a request straight after a captcha is the clearest sign there is of a script waiting on it.
 */
export function checkCleared(state: GovernorState, now: number, platform: string): GovernorState {
  return quiet({ ...state, pausedUntil: null, pauseReason: null, pausedFor: null, strikes: 0 }, now + 3 * minute,
    `Holding off for a few minutes after ${platform}'s security check, so ${platform} sees a person browsing, not a script.`);
}

/**
 * Whether a sign-in check may reach the marketplace now. At most {@link maxChecksPerHour} an hour, whatever asked
 * for them; and after checks that ended in trouble, each waits longer than the last — unless a person asked (`force`).
 */
export function checkSlot(state: GovernorState, now: number, force = false): { allowed: true } | { allowed: false; until: number } {
  const checks = (state.checks ?? []).filter((at) => at > now - hour);
  if (checks.length >= maxChecksPerHour) {
    return { allowed: false, until: checks[0]! + hour };
  }

  const misses = state.checkMisses ?? 0;
  const last = checks.at(-1);
  if (!force && misses > 0 && last !== undefined) {
    const until = last + Math.min(2 * minute * 4 ** (misses - 1), 2 * hour);
    if (until > now) {
      return { allowed: false, until };
    }
  }

  return { allowed: true };
}

/** A sign-in check reached the marketplace; `answered` is whether it came back with an answer rather than trouble. */
export function recordCheck(state: GovernorState, now: number, answered: boolean): GovernorState {
  return {
    ...state,
    checks: [...(state.checks ?? []).filter((at) => at > now - hour), now],
    checkMisses: answered ? 0 : (state.checkMisses ?? 0) + 1,
  };
}

/** A new build is the update a changed-site pause waits for; every other pause stands. */
export function afterUpdate(state: GovernorState): GovernorState {
  return state.pausedFor === "site-changed" ? resume(state) : state;
}

/** What holds traffic off now, as HaulScout is told it: a pause, or else a quiet spell. */
export function holdOf(state: GovernorState | undefined, now: number): { until: number; reason: string } | null {
  const pause = pauseOf(state, now);
  if (pause) {
    return pause;
  }

  return state?.quietUntil && state.quietUntil > now ? { until: state.quietUntil, reason: state.quietReason ?? "Holding off for a moment." } : null;
}

/** The pause in force, if any. */
export function pauseOf(state: GovernorState | undefined, now: number): { until: number; reason: string; kind: Exclude<Outcome, "ok"> | null } | null {
  return state?.pausedUntil !== null && state?.pausedUntil !== undefined && state.pausedUntil > now
    ? { until: state.pausedUntil, reason: state.pauseReason ?? "Paused.", kind: state.pausedFor ?? null }
    : null;
}
