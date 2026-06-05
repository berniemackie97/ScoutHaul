import type { Ionicons } from '@expo/vector-icons';

/**
 * Deep-links to real resale comp sources. Free, instant, always work — the core workflow for a
 * reseller pricing an item live: tap "eBay Sold" to see what the item ACTUALLY sold for.
 *
 * Ordered by resale relevance: realized sold prices first, then active secondhand listings,
 * then retail/reference last. Built client-side (just URL templates) so the buttons are instant
 * and work offline up to the moment the browser opens.
 */

export type CompTier = 'sold' | 'active' | 'retail';

export type CompSource = {
  id: string;
  label: string;
  tier: CompTier;
  icon: keyof typeof Ionicons.glyphMap;
  build: (query: string) => string;
};

const enc = (q: string) => encodeURIComponent(q.trim());

export const COMP_SOURCES: CompSource[] = [
  // ── Tier 1: SOLD — realized resale value (the gold standard) ──
  {
    id: 'ebay-sold',
    label: 'eBay Sold',
    tier: 'sold',
    icon: 'pricetag',
    build: (q) => `https://www.ebay.com/sch/i.html?_nkw=${enc(q)}&LH_Sold=1&LH_Complete=1&_sop=13`, // _sop=13 = newest sold first
  },

  // ── Tier 2: ACTIVE secondhand listings ──
  {
    id: 'ebay-active',
    label: 'eBay Active',
    tier: 'active',
    icon: 'pricetag-outline',
    build: (q) => `https://www.ebay.com/sch/i.html?_nkw=${enc(q)}&_sop=10`, // newest first
  },
  {
    id: 'facebook',
    label: 'Facebook',
    tier: 'active',
    icon: 'logo-facebook',
    build: (q) => `https://www.facebook.com/marketplace/search/?query=${enc(q)}&sortBy=creation_time_descend`,
  },
  {
    id: 'mercari',
    label: 'Mercari',
    tier: 'active',
    icon: 'bag-handle-outline',
    build: (q) => `https://www.mercari.com/search/?keyword=${enc(q)}&sortBy=2`, // newest
  },
  {
    id: 'poshmark',
    label: 'Poshmark',
    tier: 'active',
    icon: 'shirt-outline',
    build: (q) => `https://poshmark.com/search?query=${enc(q)}&sort_by=added_desc`,
  },
  {
    id: 'offerup',
    label: 'OfferUp',
    tier: 'active',
    icon: 'location-outline',
    build: (q) => `https://offerup.com/search/?q=${enc(q)}`,
  },

  // ── Tier 3: RETAIL / reference (use only if no secondhand comps) ──
  {
    id: 'google-shopping',
    label: 'Google Shopping',
    tier: 'retail',
    icon: 'cart-outline',
    build: (q) => `https://www.google.com/search?tbm=shop&q=${enc(q)}`,
  },
  {
    id: 'amazon',
    label: 'Amazon',
    tier: 'retail',
    icon: 'logo-amazon',
    build: (q) => `https://www.amazon.com/s?k=${enc(q)}`,
  },
  {
    id: 'google',
    label: 'Google',
    tier: 'retail',
    icon: 'search-outline',
    build: (q) => `https://www.google.com/search?q=${enc(q)}`,
  },
];

export const COMP_TIER_META: Record<CompTier, { label: string; hint: string }> = {
  sold: { label: 'Sold Comps', hint: 'What it actually sold for' },
  active: { label: 'Active Listings', hint: 'Currently for sale' },
  retail: { label: 'Retail & Reference', hint: 'New / MSRP' },
};

export const COMP_TIER_ORDER: CompTier[] = ['sold', 'active', 'retail'];

export function compSourcesByTier(): { tier: CompTier; sources: CompSource[] }[] {
  return COMP_TIER_ORDER.map((tier) => ({
    tier,
    sources: COMP_SOURCES.filter((s) => s.tier === tier),
  }));
}

/** The single most important comp link — eBay sold, newest first. */
export const PRIMARY_COMP: CompSource = COMP_SOURCES[0];
