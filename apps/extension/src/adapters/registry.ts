import { depopAdapter } from "./depop";
import { ebayAdapter } from "./ebay";
import { poshmarkAdapter } from "./poshmark";
import { vintedAdapter } from "./vinted";
import type { MarketplaceAdapter } from "./types";

/**
 * Every marketplace this extension can sync. A marketplace is added here in the same change that
 * the server lists it as supported — the server decides what runs; this says what this build can do.
 */
export const adapters: readonly MarketplaceAdapter[] = [ebayAdapter, vintedAdapter, poshmarkAdapter, depopAdapter];

export function adapterFor(platform: string | null | undefined): MarketplaceAdapter | undefined {
  return adapters.find((adapter) => adapter.platform.toLowerCase() === platform?.toLowerCase());
}
