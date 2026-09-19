import type { FailureKind } from "./types";

/** What a failure means for the person at this browser, and what, if anything, they can do about it. */
export function describeFailure(kind: FailureKind, platform: string): string {
  switch (kind) {
    case "SignedOut":
      return `Signed out of ${platform}. Sign in to ${platform} in this browser and syncing picks up again.`;
    case "WrongAccount":
      return `This browser is signed in to a different ${platform} account than the one this account syncs.`;
    case "RateLimited":
      return `${platform} asked for a slower pace, so syncing is holding off for a while.`;
    case "Challenge":
      return `${platform} wants to confirm a person is here. Open ${platform}, complete the check, and syncing picks up again.`;
    case "SiteChanged":
      return `${platform}'s pages changed and could not be read. Syncing ${platform} is paused until the extension is updated.`;
    case "Network":
      return `${platform} or HaulScout could not be reached.`;
    case "NoAccess":
      return `HaulScout Sync is not allowed to reach ${platform}. Choose Allow access.`;
    case "Interrupted":
      return `The sync tab was closed, or moved away from ${platform}, before the scan finished.`;
    default:
      return "The scan stopped unexpectedly.";
  }
}

/**
 * Trouble a person or time clears — a sign-in, the right account, access, a security check, a pause
 * the marketplace asked for. It is reported on the heartbeat, not as a failed scan: HaulScout then
 * holds work back until it clears, and a scan someone asked for waits and runs instead of failing.
 */
export function waitsForAPerson(kind: FailureKind): boolean {
  return kind === "SignedOut" || kind === "WrongAccount" || kind === "NoAccess" || kind === "Challenge" || kind === "RateLimited";
}
