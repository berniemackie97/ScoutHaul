import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import type { DimensionValue } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { FilterChips } from '../src/components/FilterChips';
import { LoadingState } from '../src/components/LoadingState';
import { MetricCard } from '../src/components/MetricCard';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { radius, spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import { maxSeriesAmount, reportGrossMargin, reportRange, topDimensions, type ReportRangeKey } from '../src/domain/reports';
import type { BusinessReport, ReportDimension, ReportSeriesPoint } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

const rangeOptions: { value: ReportRangeKey; label: string }[] = [
  { value: '30d', label: '30D' },
  { value: '90d', label: '90D' },
  { value: 'ytd', label: 'YTD' },
];

export default function ReportsScreen() {
  const c = useColors();
  const [range, setRange] = useState<ReportRangeKey>('90d');
  const [report, setReport] = useState<BusinessReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  const selectedRange = useMemo(() => reportRange(range), [range]);

  const load = useCallback(async (showRefresh = false) => {
    if (showRefresh) setRefreshing(true);
    try {
      setReport(await api.reportOverview({ from: selectedRange.from, to: selectedRange.to }));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Reports unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }, [selectedRange.from, selectedRange.to]);

  useFocusEffect(useCallback(() => { void load(); }, [load]));

  const reportData = report;
  const margin = reportData ? reportGrossMargin(reportData) : null;
  const platforms = topDimensions(reportData?.platforms, 5);
  const expenses = topDimensions(reportData?.expenseCategories, 5);
  const categories = topDimensions(reportData?.inventoryCategories, 5);

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text variant="title">Reports</Text>
          <Text variant="caption">Sales, profit, operating costs, inventory health, and channel mix.</Text>
        </View>
        <Button label="Orders" icon="receipt-outline" size="sm" variant="secondary" onPress={() => router.push('/(tabs)/orders')} />
      </View>

      <FilterChips options={rangeOptions} selected={range} onSelect={(value) => setRange(value as ReportRangeKey)} />
      <Text variant="caption" color={c.textMuted}>{selectedRange.label}</Text>

      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      {isLoading ? (
        <LoadingState message="Loading reports..." />
      ) : !reportData ? (
        <EmptyState icon="analytics-outline" title="No report data" message="Record or import sales to generate reporting." />
      ) : (
        <>
          <View style={styles.metricRow}>
            <MetricCard label="Gross Sales" value={formatMoney(reportData.kpis?.grossSales ?? 0)} icon="cash-outline" tone="primary" />
            <MetricCard label="Net Income" value={formatMoney(reportData.kpis?.netIncomeAfterExpenses ?? 0)} icon="trending-up-outline" tone={(reportData.kpis?.netIncomeAfterExpenses ?? 0) >= 0 ? 'success' : 'danger'} />
          </View>
          <View style={styles.metricRow}>
            <MetricCard label="Net Profit" value={formatMoney(reportData.kpis?.realizedNetProfit ?? 0)} icon="wallet-outline" tone={(reportData.kpis?.realizedNetProfit ?? 0) >= 0 ? 'success' : 'danger'} />
            <MetricCard label="Sales" value={String(reportData.kpis?.salesCount ?? 0)} icon="bag-check-outline" tone="info" />
          </View>
          <View style={styles.metricRow}>
            <MetricCard label="Avg Sale" value={formatMoney(reportData.kpis?.averageSaleValue ?? 0)} icon="stats-chart-outline" />
            <MetricCard label="Margin" value={margin == null ? '—' : `${margin.toFixed(1)}%`} icon="pie-chart-outline" tone={margin != null && margin >= 25 ? 'success' : 'warning'} />
          </View>

          <Card>
            <SectionHeader title="Profit bridge" />
            <ReportRow label="Gross sales" value={formatMoney(reportData.kpis?.grossSales ?? 0)} />
            <ReportRow label="Cost of goods" value={`-${formatMoney(reportData.kpis?.costOfGoods ?? 0)}`} />
            <ReportRow label="Sale costs" value={`-${formatMoney(reportData.kpis?.saleCosts ?? 0)}`} />
            <ReportRow label="Operating expenses" value={`-${formatMoney(reportData.kpis?.operatingExpenses ?? 0)}`} />
            <ReportRow label="Sales tax owed" value={formatMoney(reportData.kpis?.salesTaxOwed ?? 0)} />
            <ReportRow label="Net income after expenses" value={formatMoney(reportData.kpis?.netIncomeAfterExpenses ?? 0)} strong />
          </Card>

          <SectionHeader title="Trend" />
          <Card>
            {(reportData.series ?? []).length === 0 ? (
              <Text variant="caption">No time-series data for this range.</Text>
            ) : (
              <View style={styles.seriesList}>
                {(reportData.series ?? []).map((point, index) => (
                  <SeriesRow key={point.periodStart ?? point.label ?? `series-${index}`} point={point} max={maxSeriesAmount(reportData.series)} />
                ))}
              </View>
            )}
          </Card>

          <SectionHeader title="Channels" />
          <DimensionList rows={platforms} empty="No channel sales yet." />

          <SectionHeader title="Expense categories" />
          <DimensionList rows={expenses} empty="No operating expenses in this range." secondary />

          <SectionHeader title="Inventory health" />
          <Card>
            <ReportRow label="Items" value={String(reportData.inventory?.itemCount ?? 0)} />
            <ReportRow label="Cost basis" value={formatMoney(reportData.inventory?.cost ?? 0)} />
            <ReportRow label="Estimated value" value={formatMoney(reportData.inventory?.estimatedValue ?? 0)} />
            <ReportRow label="Unlisted" value={String(reportData.inventory?.unlistedCount ?? 0)} />
            <ReportRow label="Stale" value={String(reportData.inventory?.staleCount ?? 0)} />
            <ReportRow label="Missing cost" value={String(reportData.inventory?.missingCostCount ?? 0)} />
            <ReportRow label="Missing photos" value={String(reportData.inventory?.missingPhotoCount ?? 0)} />
          </Card>

          <SectionHeader title="Inventory categories" />
          <DimensionList rows={categories} empty="No category breakdown yet." />

          <SectionHeader title="Aging" />
          <Card>
            {(reportData.aging ?? []).length === 0 ? (
              <Text variant="caption">No aging data yet.</Text>
            ) : (
              (reportData.aging ?? []).map((bucket) => (
                <ReportRow
                  key={bucket.label ?? 'bucket'}
                  label={`${bucket.label ?? 'Unknown'} · ${bucket.count ?? 0} items`}
                  value={`${formatMoney(bucket.cost ?? 0)} cost`}
                />
              ))
            )}
          </Card>

          <SectionHeader title="Top sales" />
          {(reportData.topSales ?? []).length === 0 ? (
            <Card><Text variant="caption">No realized sales in this range.</Text></Card>
          ) : (
            (reportData.topSales ?? []).map((sale) => (
              <Pressable
                key={sale.saleId ?? sale.inventoryItemId ?? sale.title}
                onPress={() => sale.inventoryItemId ? router.push(`/inventory/${sale.inventoryItemId}`) : undefined}
                accessibilityRole="button"
                accessibilityLabel={`Open ${sale.title ?? 'sale'}`}
                style={({ pressed }) => [pressed && { opacity: 0.85 }]}
              >
                <Card>
                  <View style={styles.saleRow}>
                    <View style={styles.saleTitle}>
                      <Text variant="bodyBold" numberOfLines={2}>{sale.title ?? 'Sale'}</Text>
                      <Text variant="caption" color={c.textMuted}>{[sale.platform, formatDate(sale.soldAt)].filter(Boolean).join(' · ')}</Text>
                    </View>
                    <View style={styles.saleMoney}>
                      <Text variant="bodyBold">{formatMoney(sale.soldPrice ?? 0)}</Text>
                      <StatusPill label={formatMoney(sale.netProfit ?? 0)} tone={(sale.netProfit ?? 0) >= 0 ? 'success' : 'danger'} size="sm" />
                    </View>
                  </View>
                </Card>
              </Pressable>
            ))
          )}
        </>
      )}
    </Screen>
  );
}

function ReportRow({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <View style={styles.reportRow}>
      <Text variant="caption">{label}</Text>
      <Text variant={strong ? 'bodyBold' : 'body'}>{value}</Text>
    </View>
  );
}

function SeriesRow({ point, max }: { point: ReportSeriesPoint; max: number }) {
  const c = useColors();
  const grossWidth = percentWidth(((point.grossSales ?? 0) / max) * 100);
  const profitWidth = percentWidth((Math.max(0, point.netProfit ?? 0) / max) * 100);
  return (
    <View style={styles.seriesRow}>
      <Text variant="caption" style={styles.seriesLabel}>{point.label ?? formatDate(point.periodStart) ?? 'Period'}</Text>
      <View style={styles.seriesBars}>
        <View style={[styles.barTrack, { backgroundColor: c.surfaceAlt }]}>
          <View style={[styles.barFill, { width: grossWidth, backgroundColor: c.primary }]} />
        </View>
        <View style={[styles.barTrack, { backgroundColor: c.surfaceAlt }]}>
          <View style={[styles.barFill, { width: profitWidth, backgroundColor: c.success }]} />
        </View>
      </View>
      <Text variant="caption" style={styles.seriesValue}>{formatMoney(point.netProfit ?? 0)}</Text>
    </View>
  );
}

function DimensionList({ rows, empty, secondary }: { rows: ReportDimension[]; empty: string; secondary?: boolean }) {
  if (rows.length === 0) {
    return <Card><Text variant="caption">{empty}</Text></Card>;
  }
  const max = Math.max(1, ...rows.map((row) => row.amount ?? 0));
  return (
    <Card>
      {rows.map((row) => (
        <DimensionRow key={row.name ?? 'unknown'} row={row} max={max} secondary={secondary} />
      ))}
    </Card>
  );
}

function DimensionRow({ row, max, secondary }: { row: ReportDimension; max: number; secondary?: boolean }) {
  const c = useColors();
  const width = percentWidth(((row.amount ?? 0) / max) * 100);
  return (
    <View style={styles.dimensionRow}>
      <View style={styles.dimensionTop}>
        <Text variant="caption" numberOfLines={1} style={styles.dimensionName}>{row.name ?? 'Unknown'}</Text>
        <Text variant="bodyBold">{formatMoney(row.amount ?? 0)}</Text>
      </View>
      <View style={[styles.barTrack, { backgroundColor: c.surfaceAlt }]}>
        <View style={[styles.barFill, { width, backgroundColor: secondary ? c.warning : c.primary }]} />
      </View>
      <Text variant="caption" color={c.textMuted}>{row.count ?? 0} record{row.count === 1 ? '' : 's'}</Text>
    </View>
  );
}

function percentWidth(percent: number): DimensionValue {
  const safePercent = Number.isFinite(percent) ? percent : 0;
  return `${Math.max(3, Math.min(100, safePercent))}%` as DimensionValue;
}

function formatDate(value?: string | null): string | null {
  if (!value) return null;
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' }).format(new Date(value));
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  headerText: {
    flex: 1,
    gap: spacing.xs,
  },
  metricRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  reportRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  seriesList: {
    gap: spacing.md,
  },
  seriesRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  seriesLabel: {
    width: 56,
  },
  seriesBars: {
    flex: 1,
    gap: spacing.xs,
  },
  seriesValue: {
    width: 72,
    textAlign: 'right',
  },
  barTrack: {
    height: 8,
    borderRadius: radius.full,
    overflow: 'hidden',
  },
  barFill: {
    height: '100%',
    borderRadius: radius.full,
  },
  dimensionRow: {
    gap: spacing.xs,
  },
  dimensionTop: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  dimensionName: {
    flex: 1,
  },
  saleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  saleTitle: {
    flex: 1,
    gap: spacing.xxs,
  },
  saleMoney: {
    alignItems: 'flex-end',
    gap: spacing.xs,
  },
});
