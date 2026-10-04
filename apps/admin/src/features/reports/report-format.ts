import type { components } from "@overhaul/api-client";

type Kpis = components["schemas"]["ReportKpiDto"];

export const RANGE_PRESETS = [
  { key: "7d", label: "7D", title: "Last 7 days" },
  { key: "30d", label: "30D", title: "Last 30 days" },
  { key: "90d", label: "90D", title: "Last 90 days" },
  { key: "ytd", label: "YTD", title: "Year to date" },
  { key: "12m", label: "12M", title: "Last 12 months" },
] as const;

export type RangePreset = (typeof RANGE_PRESETS)[number]["key"];

export function isRangePreset(value: unknown): value is RangePreset {
  return RANGE_PRESETS.some((preset) => preset.key === value);
}

/** Percent change, or null when there is no prior figure to compare against. */
export function percentChange(current: number | null | undefined, previous: number | null | undefined): number | null {
  if (current == null || previous == null || previous === 0) return null;
  return ((current - previous) / Math.abs(previous)) * 100;
}

/** Whether a change is good news. Costs going up is bad news. */
export function changeTone(change: number | null, upIsGood = true): "up" | "down" | "flat" {
  if (change == null || Math.abs(change) < 0.05) return "flat";
  return change > 0 === upIsGood ? "up" : "down";
}

export type BreakdownSegment = { key: string; label: string; value: number; color: string };

/**
 * Where gross sales went, as parts that add back up to gross sales. Net profit leads because it is
 * what the business kept; a loss has no segment and is reported beside the bar instead.
 */
export function moneyBreakdown(kpis: Kpis): { segments: BreakdownSegment[]; loss: number } {
  const fees = (kpis.platformFees ?? 0) + (kpis.paymentFees ?? 0);
  const other = (kpis.suppliesAndOther ?? 0) + (kpis.refundsAndAdjustments ?? 0);
  const net = kpis.realizedNetProfit ?? 0;
  const segments: BreakdownSegment[] = [
    { key: "net", label: "Net profit", value: Math.max(0, net), color: "var(--viz-1)" },
    { key: "cogs", label: "Cost of goods", value: kpis.costOfGoods ?? 0, color: "var(--viz-2)" },
    { key: "fees", label: "Marketplace & payment fees", value: fees, color: "var(--viz-3)" },
    { key: "shipping", label: "Shipping labels", value: kpis.shippingCosts ?? 0, color: "var(--viz-4)" },
    { key: "other", label: "Supplies, other & refunds", value: other, color: "var(--viz-5)" },
  ];
  return { segments: segments.filter((segment) => segment.value > 0), loss: Math.max(0, -net) };
}

/** "InventoryPurchase" → "Inventory purchase". */
export function humanize(value: string | null | undefined): string {
  if (!value) return "—";
  const spaced = value.replace(/([a-z])([A-Z])/g, "$1 $2");
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

export function compactMoney(value: number): string {
  const absolute = Math.abs(value);
  const sign = value < 0 ? "−" : "";
  if (absolute >= 1_000_000) return `${sign}$${(absolute / 1_000_000).toFixed(1)}M`;
  if (absolute >= 10_000) return `${sign}$${Math.round(absolute / 1_000)}K`;
  if (absolute >= 1_000) return `${sign}$${(absolute / 1_000).toFixed(1)}K`;
  return `${sign}$${Math.round(absolute)}`;
}

/** "2026-10-04" for a report boundary, read in the zone the API already converted it to. */
export function reportDay(value: string | null | undefined): string | undefined {
  return value?.slice(0, 10);
}
