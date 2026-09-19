import { useEffect, useState } from "react";
import { browser } from "wxt/browser";
import type { WxtStorageItem } from "wxt/utils/storage";
import { adapters } from "../adapters/registry";
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

/** Re-renders every so often, so "checked 12s ago" stays true while the popup is open. */
export function useNow(intervalMs = 5000): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}

/** Whether each marketplace may be reached, kept current as access is granted or taken away. */
export function useAccess(): Record<string, boolean> | undefined {
  const [access, setAccess] = useState<Record<string, boolean>>();
  useEffect(() => {
    let live = true;
    const read = async () => {
      const entries = await Promise.all(adapters.map(async (adapter) => [adapter.platform, await browser.permissions.contains({ origins: adapter.origins })] as const));
      if (live) {
        setAccess(Object.fromEntries(entries));
      }
    };
    const changed = () => void read();
    void read();
    browser.permissions.onAdded.addListener(changed);
    browser.permissions.onRemoved.addListener(changed);
    return () => {
      live = false;
      browser.permissions.onAdded.removeListener(changed);
      browser.permissions.onRemoved.removeListener(changed);
    };
  }, []);
  return access;
}

export async function ask(request: PopupRequest): Promise<AgentResponse> {
  return (await browser.runtime.sendMessage(request)) as AgentResponse;
}
