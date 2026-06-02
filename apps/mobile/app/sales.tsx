import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { FilterChips } from '../src/components/FilterChips';
import { SkeletonList } from '../src/components/Skeleton';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { radius, spacing, type ToneKey } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import { useHaptics } from '../src/hooks/useHaptics';
import type { Sale, SaleStatus } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

type SaleFilter = 'All' | 'NeedsShipping' | SaleStatus;

const filterOptions: { value: SaleFilter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'NeedsShipping', label: 'Ship' },
  { value: 'Recorded', label: 'Sold' },
  { value: 'Packed', label: 'Packed' },
  { value: 'Shipped', label: 'Shipped' },
  { value: 'Delivered', label: 'Done' },
];

function saleTone(status: SaleStatus): ToneKey {
  if (status === 'Delivered') return 'success';
  if (status === 'Shipped') return 'info';
  if (status === 'Packed') return 'warning';
  if (status === 'Returned') return 'danger';
  return 'neutral';
}

type WorkflowStep = { label: string; status: SaleStatus; icon: keyof typeof Ionicons.glyphMap; tone: ToneKey };

const workflowMap: Partial<Record<SaleStatus, WorkflowStep>> = {
  Recorded: { label: 'Mark Packed', status: 'Packed', icon: 'file-tray-full-outline', tone: 'warning' },
  Packed: { label: 'Mark Shipped', status: 'Shipped', icon: 'paper-plane-outline', tone: 'info' },
  Shipped: { label: 'Mark Delivered', status: 'Delivered', icon: 'checkmark-circle-outline', tone: 'success' },
};

export default function SalesScreen() {
  const { colors: c, tones, shadows: s } = useAppTheme();
  const haptics = useHaptics();
  const [sales, setSales] = useState<Sale[]>([]);
  const [filter, setFilter] = useState<SaleFilter>('NeedsShipping');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      setSales(await api.sales());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Sales unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  const filtered = useMemo(() => {
    if (filter === 'All') return sales;
    if (filter === 'NeedsShipping') return sales.filter((sale) => sale.status === 'Recorded' || sale.status === 'Packed');
    return sales.filter((sale) => sale.status === filter);
  }, [sales, filter]);

  async function advanceSale(sale: Sale, status: SaleStatus) {
    try {
      haptics.success();
      if (status === 'Packed') await api.markSalePacked(sale.id);
      if (status === 'Shipped') await api.markSaleShipped(sale.id);
      if (status === 'Delivered') await api.markSaleDelivered(sale.id);
      await load();
    } catch (e) {
      haptics.error();
      setError(e instanceof Error ? e.message : 'Unable to update sale.');
    }
  }

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      <View style={styles.header}>
        <Text variant="title">Sales & Shipping</Text>
        <Button label="Inventory" icon="cube-outline" size="sm" variant="secondary" onPress={() => router.push('/(tabs)/inventory')} />
      </View>

      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}
      <FilterChips options={filterOptions} selected={filter} onSelect={setFilter} />

      {isLoading ? (
        <SkeletonList count={4} />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon="paper-plane-outline"
          title="No sales in this view"
          message="Record a sale from an inventory item, then pack and ship it here."
          actionLabel="Open Inventory"
          onAction={() => router.push('/(tabs)/inventory')}
        />
      ) : (
        filtered.map((sale) => {
          const step = workflowMap[sale.status];
          const tone = tones[saleTone(sale.status)];
          return (
            <View key={sale.id} style={[styles.saleCard, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm }]}>
              <Pressable
                onPress={() => router.push(`/inventory/${sale.inventoryItemId}`)}
                style={styles.saleBody}
              >
                <View style={styles.saleTop}>
                  <View style={styles.saleInfo}>
                    <Text variant="bodyBold" numberOfLines={1}>{sale.inventoryItemTitle || sale.platform}</Text>
                    <Text variant="caption">{sale.platform} · {new Date(sale.soldAt).toLocaleDateString()}</Text>
                  </View>
                  <View style={styles.saleMoney}>
                    <Text variant="metricSmall">{formatMoney(sale.soldPrice)}</Text>
                    <StatusPill label={sale.status} tone={saleTone(sale.status)} size="sm" />
                  </View>
                </View>
                <View style={[styles.profitBar, { backgroundColor: sale.netProfit >= 0 ? c.successLight : c.dangerLight }]}>
                  <Ionicons
                    name={sale.netProfit >= 0 ? 'trending-up' : 'trending-down'}
                    size={14}
                    color={sale.netProfit >= 0 ? c.success : c.danger}
                  />
                  <Text variant="caption" color={sale.netProfit >= 0 ? c.successDark : c.dangerDark}>
                    {formatMoney(sale.netProfit)} net profit
                  </Text>
                </View>
              </Pressable>

              {step ? (
                <Pressable
                  onPress={() => void advanceSale(sale, step.status)}
                  style={[styles.workflowBtn, { backgroundColor: tone.bg, borderColor: tone.border }]}
                >
                  <Ionicons name={step.icon} size={18} color={tone.text} />
                  <Text variant="bodyBold" color={tone.text}>{step.label}</Text>
                  <Ionicons name="arrow-forward" size={16} color={tone.text} />
                </Pressable>
              ) : null}
            </View>
          );
        })
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.md,
  },
  saleCard: {
    borderRadius: radius.lg,
    borderWidth: 1,
    overflow: 'hidden',
  },
  saleBody: {
    padding: spacing.lg,
    gap: spacing.sm,
  },
  saleTop: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  saleInfo: {
    flex: 1,
    gap: spacing.xs,
  },
  saleMoney: {
    alignItems: 'flex-end',
    gap: spacing.xs,
  },
  profitBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
    borderRadius: radius.sm,
  },
  workflowBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.md,
    borderTopWidth: 1,
  },
});
