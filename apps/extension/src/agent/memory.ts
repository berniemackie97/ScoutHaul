import { storage } from "wxt/utils/storage";
import type { AdapterMemory } from "../adapters/types";

/** The most entries one marketplace's memory keeps; the oldest are forgotten first. */
export const memoryLimit = 2000;

type Entries = Record<string, { at: number; value: unknown }>;

const memoryItem = storage.defineItem<Record<string, Entries>>("local:adapterMemory", { fallback: {} });

/**
 * One marketplace's memory between scans, in this browser's local storage. Bounded by
 * {@link memoryLimit}: when it is full the least recently written entries go, and an adapter that
 * finds nothing simply reads the marketplace again.
 */
export function adapterMemory(platform: string, now: () => number = Date.now): AdapterMemory {
  return {
    async get<T>(key: string): Promise<T | undefined> {
      return (await memoryItem.getValue())[platform]?.[key]?.value as T | undefined;
    },
    async set<T>(key: string, value: T): Promise<void> {
      const all = await memoryItem.getValue();
      const entries: Entries = { ...all[platform], [key]: { at: now(), value } };
      const keys = Object.keys(entries);
      if (keys.length > memoryLimit) {
        for (const stale of keys.sort((left, right) => entries[left]!.at - entries[right]!.at).slice(0, keys.length - memoryLimit)) {
          delete entries[stale];
        }
      }

      await memoryItem.setValue({ ...all, [platform]: entries });
    },
  };
}

/** Forgets everything every marketplace kept — when the browser is disconnected. */
export async function forgetAdapterMemory(): Promise<void> {
  await memoryItem.setValue({});
}
