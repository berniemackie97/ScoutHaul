import { useEffect, useState } from "react";
import { browser } from "wxt/browser";
import type { WxtStorageItem } from "wxt/utils/storage";
import type { AgentResponse, PopupRequest } from "../agent/messages";

/** A stored value that re-renders the popup whenever the background agent changes it. */
export function useStored<T>(item: WxtStorageItem<T, Record<string, unknown>>): T | undefined {
  const [value, setValue] = useState<T>();
  useEffect(() => {
    let live = true;
    void item.getValue().then((current) => live && setValue(current));
    const unwatch = item.watch((next) => setValue(next));
    return () => {
      live = false;
      unwatch();
    };
  }, [item]);
  return value;
}

/** Re-renders every so often, so "checked in 12s ago" stays true while the popup is open. */
export function useNow(intervalMs = 5000): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}

export async function ask(request: PopupRequest): Promise<AgentResponse> {
  return (await browser.runtime.sendMessage(request)) as AgentResponse;
}

/** "just now", "4 min ago", "2 h ago", "3 days ago". */
export function ago(iso: string | null | undefined, now: Date): string {
  if (!iso) {
    return "never";
  }

  const seconds = Math.max(0, Math.round((now.getTime() - Date.parse(iso)) / 1000));
  if (seconds < 45) {
    return "just now";
  }

  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min ago`;
  }

  const hours = Math.round(minutes / 60);
  return hours < 36 ? `${hours} h ago` : `${Math.round(hours / 24)} days ago`;
}

/** "every 10 min", "every 4 h", "off". */
export function cadence(minutes: number | null | undefined): string {
  if (!minutes) {
    return "off";
  }

  return minutes % 60 === 0 ? `every ${minutes / 60} h` : `every ${minutes} min`;
}
