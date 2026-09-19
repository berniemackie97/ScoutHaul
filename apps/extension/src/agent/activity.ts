import { activityLimit } from "./config";
import { activityItem, type ActivityEntry } from "./storage";

/** Adds a line to what the popup shows under Activity, newest first. */
export async function logActivity(tone: ActivityEntry["tone"], message: string, at: Date = new Date()): Promise<void> {
  const entries = await activityItem.getValue();
  await activityItem.setValue([{ at: at.toISOString(), tone, message }, ...entries].slice(0, activityLimit));
}
