import { activityLimit, activityMergeMs } from "./config";
import { activityItem, type ActivityEntry } from "./storage";

/**
 * Adds a line to what the popup shows under Activity, newest first. The same line again soon after
 * is counted on the one already there and moved to the top — a problem that repeats every minute
 * reads as one line saying so, not a wall of copies.
 */
export async function logActivity(tone: ActivityEntry["tone"], message: string, at: Date = new Date()): Promise<void> {
  const entries = await activityItem.getValue();
  const index = entries.findIndex((entry) => entry.message === message && entry.tone === tone && at.getTime() - Date.parse(entry.at) < activityMergeMs);
  if (index >= 0) {
    const repeated = entries[index]!;
    const rest = entries.filter((_, position) => position !== index);
    await activityItem.setValue([{ ...repeated, at: at.toISOString(), count: (repeated.count ?? 1) + 1 }, ...rest]);
    return;
  }

  await activityItem.setValue([{ at: at.toISOString(), tone, message }, ...entries].slice(0, activityLimit));
}
