/** Rounds to the cent, so sums of amounts compare exactly. */
export function cents(value: number): number {
  return Math.round(value * 100) / 100;
}
