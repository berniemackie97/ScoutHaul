import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { MetricCard } from '../../src/components/MetricCard';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import {
  acquisitionAllocatedTotal,
  acquisitionDisplayName,
  acquisitionItemCount,
  acquisitionMethodLabel,
  acquisitionUnallocatedCost,
} from '../../src/domain/acquisition';
import type { AcquisitionLot } from '../../src/domain/types';
import { formatMoney } from '../../src/utils/money';

export default function PurchasesScreen() {
  const { colors: c } = useAppTheme();
  const [lots, setLots] = useState<AcquisitionLot[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      setLots(await api.acquisitionLots());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Purchases unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  const sortedLots = useMemo(
    () => [...lots].sort((a, b) => new Date(b.purchasedAt ?? b.createdAt ?? 0).getTime() - new Date(a.purchasedAt ?? a.createdAt ?? 0).getTime()),
    [lots],
  );

  const summary = useMemo(() => {
    const landed = lots.reduce((sum, lot) => sum + (lot.totalLandedCost ?? 0), 0);
    const allocated = lots.reduce((sum, lot) => sum + acquisitionAllocatedTotal(lot), 0);
    const items = lots.reduce((sum, lot) => sum + acquisitionItemCount(lot), 0);
    return { landed, allocated, unallocated: landed - allocated, items };
  }, [lots]);

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text variant="title">Purchases</Text>
          <Text variant="caption">Acquisition lots, single-item buys, landed cost, and allocated COGS.</Text>
        </View>
        <StatusPill label={`${lots.length} lot${lots.length === 1 ? '' : 's'}`} tone="primary" />
      </View>

      <Button label="Record a purchase" icon="add-circle-outline" onPress={() => router.push('/purchases/new')} />

      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      {isLoading ? (
        <LoadingState message="Loading purchases..." />
      ) : lots.length === 0 ? (
        <EmptyState
          icon="receipt-outline"
          title="No purchases recorded"
          message="Record what you buy as you buy it. Costs now, items once you have unpacked them."
          actionLabel="Record a purchase"
          onAction={() => router.push('/purchases/new')}
        />
      ) : (
        <>
          <View style={styles.metricRow}>
            <MetricCard label="Landed" value={formatMoney(summary.landed)} icon="cash-outline" tone="primary" />
            <MetricCard label="Allocated" value={formatMoney(summary.allocated)} icon="cube-outline" tone="success" />
          </View>
          <View style={styles.metricRow}>
            <MetricCard label="Unallocated" value={formatMoney(summary.unallocated)} icon="alert-circle-outline" tone={summary.unallocated > 0.009 ? 'warning' : 'neutral'} />
            <MetricCard label="Items" value={String(summary.items)} icon="layers-outline" tone="info" />
          </View>

          <SectionHeader title="Purchase records" />
          {sortedLots.map((lot) => (
            <Pressable
              key={lot.id}
              onPress={() => router.push(`/purchases/${lot.id}`)}
              accessibilityRole="button"
              accessibilityLabel={`Open ${acquisitionDisplayName(lot)}`}
              style={({ pressed }) => [pressed && { opacity: 0.85 }]}
            >
              <Card>
                <View style={styles.lotHeader}>
                  <View style={styles.lotTitle}>
                    <Text variant="bodyBold" numberOfLines={1}>{acquisitionDisplayName(lot)}</Text>
                    <Text variant="caption" color={c.textMuted} numberOfLines={1}>
                      {[lot.supplierName, lot.externalReference, formatDate(lot.purchasedAt)].filter(Boolean).join(' · ') || 'No supplier/reference'}
                    </Text>
                  </View>
                  <Ionicons name="chevron-forward" size={18} color={c.textMuted} />
                </View>
                <View style={styles.lotMeta}>
                  <StatusPill label={acquisitionMethodLabel(lot.allocationMethod)} tone="neutral" size="sm" />
                  <StatusPill label={`${acquisitionItemCount(lot)} item${acquisitionItemCount(lot) === 1 ? '' : 's'}`} tone="info" size="sm" />
                  {Math.abs(acquisitionUnallocatedCost(lot)) > 0.009 ? (
                    <StatusPill label={`${formatMoney(acquisitionUnallocatedCost(lot))} open`} tone="warning" size="sm" />
                  ) : (
                    <StatusPill label="Fully allocated" tone="success" size="sm" />
                  )}
                </View>
                <View style={styles.moneyRow}>
                  <MoneyColumn label="Landed" value={lot.totalLandedCost ?? 0} />
                  <MoneyColumn label="Allocated" value={acquisitionAllocatedTotal(lot)} />
                  <MoneyColumn label="Open" value={acquisitionUnallocatedCost(lot)} />
                </View>
              </Card>
            </Pressable>
          ))}
        </>
      )}
    </Screen>
  );
}

function MoneyColumn({ label, value }: { label: string; value: number }) {
  return (
    <View style={styles.moneyColumn}>
      <Text variant="caption">{label}</Text>
      <Text variant="bodyBold">{formatMoney(value)}</Text>
    </View>
  );
}

function formatDate(value?: string | null): string | null {
  if (!value) return null;
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date(value));
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
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
  lotHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  lotTitle: {
    flex: 1,
    gap: spacing.xxs,
  },
  lotMeta: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
  },
  moneyRow: {
    flexDirection: 'row',
    borderRadius: radius.md,
    overflow: 'hidden',
  },
  moneyColumn: {
    flex: 1,
    gap: spacing.xxs,
    paddingVertical: spacing.sm,
  },
});
