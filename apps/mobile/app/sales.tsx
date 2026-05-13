import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { FilterChips } from '../src/components/FilterChips';
import { SkeletonList } from '../src/components/Skeleton';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { spacing, type ToneKey } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
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

export default function SalesScreen() {
  const c = useColors();
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

  async function updateSaleStatus(sale: Sale, status: SaleStatus) {
    try {
      if (status === 'Packed') await api.markSalePacked(sale.id);
      if (status === 'Shipped') await api.markSaleShipped(sale.id);
      if (status === 'Delivered') await api.markSaleDelivered(sale.id);
      await load();
    } catch (e) {
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
        filtered.map((sale) => (
          <Card key={sale.id}>
            <View style={styles.saleHeader}>
              <View style={styles.saleInfo}>
                <Text variant="bodyBold" numberOfLines={1}>{sale.inventoryItemTitle || sale.platform}</Text>
                <Text variant="caption">{sale.platform} · {new Date(sale.soldAt).toLocaleDateString()}</Text>
              </View>
              <View style={styles.saleMoney}>
                <Text variant="metricSmall">{formatMoney(sale.soldPrice)}</Text>
                <StatusPill label={sale.status} tone={saleTone(sale.status)} size="sm" />
              </View>
            </View>
            <Text variant="caption" color={sale.netProfit >= 0 ? c.success : c.danger}>
              Net profit {formatMoney(sale.netProfit)}
            </Text>
            <View style={styles.actions}>
              <SaleAction icon="cube-outline" label="Item" color={c.primary} onPress={() => router.push(`/inventory/${sale.inventoryItemId}`)} />
              {sale.status === 'Recorded' ? (
                <SaleAction icon="file-tray-full-outline" label="Packed" color={c.warning} onPress={() => void updateSaleStatus(sale, 'Packed')} />
              ) : null}
              {sale.status === 'Packed' ? (
                <SaleAction icon="paper-plane-outline" label="Shipped" color={c.info} onPress={() => void updateSaleStatus(sale, 'Shipped')} />
              ) : null}
              {sale.status === 'Shipped' ? (
                <SaleAction icon="checkmark-circle-outline" label="Delivered" color={c.success} onPress={() => void updateSaleStatus(sale, 'Delivered')} />
              ) : null}
            </View>
          </Card>
        ))
      )}
    </Screen>
  );
}

function SaleAction({ icon, label, color, onPress }: {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  color: string;
  onPress: () => void;
}) {
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={label} hitSlop={6} style={styles.action}>
      <Ionicons name={icon} size={16} color={color} />
      <Text variant="caption" color={color}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.md,
  },
  saleHeader: {
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
  actions: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.md,
    paddingTop: spacing.md,
  },
  action: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
});
