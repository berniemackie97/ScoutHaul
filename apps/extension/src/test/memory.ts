import type { AdapterMemory } from "../adapters/types";

/** An adapter's memory between scans, held in a map — and the map, to look into. */
export function memoryInMap(): AdapterMemory & { entries: Map<string, unknown> } {
  const entries = new Map<string, unknown>();
  return {
    entries,
    get: async <T>(key: string) => entries.get(key) as T | undefined,
    set: async <T>(key: string, value: T) => {
      entries.set(key, value);
    },
  };
}
