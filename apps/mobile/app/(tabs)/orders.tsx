import { Ionicons } from '@expo/vector-icons';
import { FlashList } from '@shopify/flash-list';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button } from '../../src/components/Button';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { FilterChips } from '../../src/components/FilterChips';
import { SkeletonList } from '../../src/components/Skeleton';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { ordersApi } from '../../src/api/orders';
import type { OrderSummary } from '../../src/domain/order.types';
import { formatMoney } from '../../src/utils/money';
import { orderStatusTone } from '../../src/utils/statusTone';

type Filter = 'All' | 'Open' | 'Paid' | 'Shipped' | 'Delivered' | 'Refunded';
const filterOptions: { value: Filter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Open', label: 'Open' },
  { value: 'Paid', label: 'Paid' },
  { value: 'Shipped', label: 'Shipped' },
  { value: 'Delivered', label: 'Delivered' },
  { value: 'Refunded', label: 'Refunded' },
];

const shortDate = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' }).format(new Date(value)) : '—';

function OrderCard({ order, onPress }: { order: OrderSummary; onPress: () => void }) {
  const { colors: c, shadows: s } = useAppTheme();
  const buyer = order.customerName || order.buyerReference || order.email || 'Unknown buyer';
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={`Order ${order.orderNumber}, ${order.status}`}
      style={({ pressed }) => [
        styles.card,
        { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm },
        pressed && { opacity: 0.85 },
      ]}
    >
      <View style={styles.cardTop}>
        <View style={styles.cardTitleWrap}>
          <Text variant="bodyBold" numberOfLines={1}>{order.orderNumber}</Text>
          <Text variant="caption" numberOfLines={1}>
            {order.channel} · {buyer}
          </Text>
        </View>
        <StatusPill label={order.status} tone={orderStatusTone(order.status)} size="sm" />
      </View>
      <View style={[styles.cardMeta, { borderTopColor: c.borderLight }]}>
        <View style={styles.metaCol}>
          <Text variant="label" color={c.textMuted}>Placed</Text>
          <Text variant="caption">{shortDate(order.createdAt)}</Text>
        </View>
        <View style={styles.metaCol}>
          <Text variant="label" color={c.textMuted}>Items</Text>
          <Text variant="caption">{order.itemCount}</Text>
        </View>
        <View style={styles.metaCol}>
          <Text variant="label" color={c.textMuted}>Total</Text>
          <Text variant="bodyBold">{formatMoney(order.grandTotal)}</Text>
        </View>
        <View style={styles.metaColRight}>
          {order.isLocalPickup ? (
            <StatusPill label="Pickup" tone="neutral" size="sm" />
          ) : order.trackingNumber ? (
            <View style={styles.trackRow}>
              <Ionicons name="cube-outline" size={13} color={c.textMuted} />
              <Text variant="caption" color={c.textMuted}>{order.shippingCarrier ?? 'Tracked'}</Text>
            </View>
          ) : (
            <StatusPill label={order.paymentStatus} tone={order.paymentStatus === 'Paid' ? 'success' : 'neutral'} size="sm" />
          )}
        </View>
      </View>
    </Pressable>
  );
}

export default function OrdersListScreen() {
  const { colors: c } = useAppTheme();
  const [orders, setOrders] = useState<OrderSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [filter, setFilter] = useState<Filter>('All');
  const [search, setSearch] = useState('');

  const load = useCallback(async (showRefresh = false) => {
    if (showRefresh) setRefreshing(true);
    try {
      setOrders(await ordersApi.list());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Orders unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }, []);

  useFocusEffect(useCallback(() => { void load(); }, [load]));

  const filtered = useMemo(() => {
    let result = orders;
    if (filter === 'Open') {
      result = result.filter((o) => !['Delivered', 'Cancelled', 'Refunded'].includes(o.status));
    } else if (filter !== 'All') {
      result = result.filter((o) => o.status === filter);
    }
    if (search.trim()) {
      const q = search.toLowerCase();
      result = result.filter((o) =>
        o.orderNumber.toLowerCase().includes(q) ||
        (o.customerName ?? '').toLowerCase().includes(q) ||
        (o.buyerReference ?? '').toLowerCase().includes(q) ||
        (o.externalOrderId ?? '').toLowerCase().includes(q),
      );
    }
    return result;
  }, [orders, filter, search]);

  const listHeader = useMemo(() => (
    <View style={styles.listHeader}>
      <View style={styles.headerRow}>
        <View style={styles.headerText}>
          <Text variant="title">Orders</Text>
          <Text variant="caption">Order-backed sales, fulfillment, refunds, and returns.</Text>
        </View>
        <Button label="Record" icon="cash-outline" size="sm" onPress={() => router.push('/record-sale')} />
      </View>
      <TextField label="" value={search} onChangeText={setSearch} placeholder="Search order #, buyer, channel..." />
      <FilterChips options={filterOptions} selected={filter} onSelect={setFilter} />
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}
      {isLoading ? (
        <SkeletonList count={4} />
      ) : filtered.length === 0 ? (
        orders.length === 0 ? (
          <EmptyState icon="receipt-outline" title="No orders yet" message="Orders from every channel show here once recorded or imported." />
        ) : (
          <EmptyState icon="filter-outline" title="No matches" message="Try a different filter or search." />
        )
      ) : (
        <Text variant="caption">{filtered.length} order{filtered.length !== 1 ? 's' : ''}</Text>
      )}
    </View>
  ), [search, filter, error, isLoading, filtered.length, orders.length, load]);

  return (
    <SafeAreaView style={[styles.safeArea, { backgroundColor: c.background }]} edges={['top', 'left', 'right']}>
      <FlashList
        data={isLoading || filtered.length === 0 ? [] : filtered}
        renderItem={({ item }) => (
          <View style={styles.listItem}>
            <OrderCard order={item} onPress={() => router.push(`/orders/${item.id}`)} />
          </View>
        )}
        keyExtractor={(item) => item.id}
        ListHeaderComponent={listHeader}
        contentContainerStyle={styles.listContent}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={() => void load(true)} tintColor={c.primary} colors={[c.primary]} />
        }
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1 },
  listContent: { paddingHorizontal: spacing.lg, paddingBottom: spacing.xxl },
  listHeader: { gap: spacing.md, paddingTop: spacing.lg },
  headerRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  headerText: { flex: 1, gap: spacing.xxs },
  listItem: { paddingTop: spacing.md },
  card: { borderRadius: radius.lg, padding: spacing.lg, borderWidth: 1, gap: spacing.md },
  cardTop: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.sm },
  cardTitleWrap: { flex: 1, gap: spacing.xxs },
  cardMeta: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.lg, borderTopWidth: 1, paddingTop: spacing.md },
  metaCol: { gap: spacing.xxs },
  metaColRight: { marginLeft: 'auto', alignItems: 'flex-end' },
  trackRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xxs },
});
