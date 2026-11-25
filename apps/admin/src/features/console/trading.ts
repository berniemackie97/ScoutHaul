import type { components } from "@overhaul/api-client";

export type OperatorConsole = components["schemas"]["OperatorConsoleDto"];
type ReportChange = components["schemas"]["ReportChangeDto"];

/**
 * "+12%", "−8%", "new", "no change" or "—": a change a reader can take in at a glance.
 *
 * "new" only when there was nothing before and something now; nothing both times is not news.
 */
export function describeChange(change: ReportChange | null | undefined): string {
  const current = change?.current ?? 0;
  const previous = change?.previous ?? 0;
  if (previous === 0) return current === 0 ? "—" : "new";

  const percent = change?.percentChange;
  if (percent === null || percent === undefined) return "—";
  const rounded = Math.round(percent);
  if (rounded === 0) return "no change";
  return `${rounded > 0 ? "+" : "−"}${Math.abs(rounded)}%`;
}

/** Whether a change is good news. Expenses going up is not. */
export function changeTone(change: ReportChange | null | undefined, higherIsBetter = true): "up" | "down" | "flat" {
  const delta = (change?.current ?? 0) - (change?.previous ?? 0);
  if (Math.abs(delta) < 0.005) return "flat";
  return delta > 0 === higherIsBetter ? "up" : "down";
}
