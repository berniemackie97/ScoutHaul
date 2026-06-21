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
import type { Sale, SaleStatus } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

type SaleFilter = 'All' | 'NeedsShipping' | 'InTransit' | 'Complete' | 'Returns' | 'Cancelled';

const filterOptions: { value: SaleFilter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'NeedsShipping', label: 'Ship' },
  { value: 'InTransit', label: 'Transit' },
  { value: 'Complete', label: 'Done' },
  { value: 'Returns', label: 'Returns' },
  { value: 'Cancelled', label: 'Cancelled' },
];

function saleTone(status: SaleStatus): ToneKey {
  if (status === 'Delivered') return 'success';
  if (status === 'Shipped') return 'info';
  if (status === 'Packed') return 'warning';
  if (status === 'ReturnRequested') return 'warning';
  if (status === 'Returned' || status === 'Refunded' || status === 'Cancelled') return 'danger';
  return 'neutral';
}

export default function SalesScreen() {
  const { colors: c, shadows: s } = useAppTheme();
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

  useFocusEffect(
    useCallback(() => {
      void load();
    }, [])
  );

  const filtered = useMemo(() => {
    if (filter === 'All') return sales;
    if (filter === 'NeedsShipping') return sales.filter((sale) => sale.status === 'Recorded' || sale.status === 'Packed');
    if (filter === 'InTransit') return sales.filter((sale) => sale.status === 'Shipped');
    if (filter === 'Complete') return sales.filter((sale) => sale.status === 'Delivered');
    if (filter === 'Returns') return sales.filter((sale) => ['ReturnRequested', 'Returned', 'Refunded'].includes(sale.status));
    return sales.filter((sale) => sale.status === 'Cancelled');
  }, [sales, filter]);

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
          return (
            <View key={sale.id} style={[styles.saleCard, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm }]}>
              <Pressable onPress={() => router.push(`/inventory/${sale.inventoryItemId}`)} style={styles.saleBody}>
                <View style={styles.saleTop}>
                  <View style={styles.saleInfo}>
                    <Text variant="bodyBold" numberOfLines={1}>
                      {sale.inventoryItemTitle || sale.platform}
                    </Text>
                    <Text variant="caption">
                      {sale.platform} · {new Date(sale.soldAt).toLocaleDateString()}
                    </Text>
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

              {(sale.allowedTransitions ?? []).length > 0 ? (
                <Pressable
                  onPress={() => router.push({ pathname: '/sale-transition', params: { saleId: sale.id } })}
                  style={[styles.workflowBtn, { backgroundColor: c.primaryLight, borderColor: c.primary }]}
                >
                  <Ionicons name="git-branch-outline" size={18} color={c.primary} />
                  <Text variant="bodyBold" color={c.primary}>
                    Update fulfillment / return
                  </Text>
                  <Ionicons name="arrow-forward" size={16} color={c.primary} />
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
