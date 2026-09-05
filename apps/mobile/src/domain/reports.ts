import type { BusinessReport, ReportDimension, ReportSeriesPoint } from './report.types';

export type ReportRangeKey = '30d' | '90d' | 'ytd';

/**
 * The reports endpoint takes a calendar day, not an instant: `DateOnly? from` on the server. Sending
 * a full timestamp meant it could not be bound at all, and every range on the reports screen came
 * back with "The value '2026-09-05T03:09:37.178Z' is not valid" — the screen has never worked.
 */
function asCalendarDay(value: Date): string {
  return value.toISOString().slice(0, 10);
}

export function reportRange(key: ReportRangeKey, now = new Date()): { from: string; to: string; label: string } {
  const to = new Date(now);
  const from = new Date(now);

  if (key === 'ytd') {
    const yearStart = new Date(Date.UTC(to.getUTCFullYear(), 0, 1, 0, 0, 0, 0));
    return { from: asCalendarDay(yearStart), to: asCalendarDay(to), label: 'Year to date' };
  }

  from.setUTCDate(from.getUTCDate() - (key === '30d' ? 30 : 90));
  return {
    from: asCalendarDay(from),
    to: asCalendarDay(to),
    label: key === '30d' ? 'Last 30 days' : 'Last 90 days',
  };
}

export function reportGrossMargin(report: BusinessReport): number | null {
  const grossSales = report.kpis?.grossSales ?? 0;
  if (grossSales <= 0) return null;
  return ((report.kpis?.grossProfit ?? 0) / grossSales) * 100;
}

export function topDimensions(rows: ReportDimension[] | null | undefined, limit = 5): ReportDimension[] {
  return [...(rows ?? [])]
    .sort((a, b) => (b.amount ?? 0) - (a.amount ?? 0))
    .slice(0, limit);
}

export function maxSeriesAmount(series: ReportSeriesPoint[] | null | undefined): number {
  return Math.max(1, ...(series ?? []).map((point) => Math.max(point.grossSales ?? 0, point.netProfit ?? 0, point.inventorySpend ?? 0)));
}
