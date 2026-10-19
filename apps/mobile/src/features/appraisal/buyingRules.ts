import { getPreference, setPreference } from '../../db/preferences';

/** The person's own floor for a buy. Kept on the phone until the business has shared buying rules. */
export type BuyingRules = {
  minimumProfit: number;
  targetMarginPercent: number | null;
};

const KEY = 'buyingRules';

export const defaultBuyingRules: BuyingRules = { minimumProfit: 20, targetMarginPercent: null };

export async function loadBuyingRules(): Promise<BuyingRules> {
  const saved = await getPreference<BuyingRules>(KEY);
  return saved ? { ...defaultBuyingRules, ...saved } : defaultBuyingRules;
}

export function saveBuyingRules(rules: BuyingRules): Promise<void> {
  return setPreference(KEY, rules);
}
