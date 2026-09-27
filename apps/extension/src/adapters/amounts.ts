/** Rounds to the cent, so sums of amounts compare exactly. */
export function cents(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * Splits what the buyer paid for a bundle across its items, in proportion to their listed prices —
 * evenly when those are unknown — to the cent, the last item taking the rounding.
 */
export function allocate(total: number, listed: (number | null)[]): number[] {
  if (listed.length === 0) {
    return [];
  }

  const weights = listed.map((price) => (price !== null && price > 0 ? price : 0));
  const sum = weights.reduce((running, weight) => running + weight, 0);
  const shares = weights.map((weight) => cents(sum > 0 ? (total * weight) / sum : total / listed.length));
  shares[shares.length - 1] = cents(total - shares.slice(0, -1).reduce((running, share) => running + share, 0));
  return shares;
}
