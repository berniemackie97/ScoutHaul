import { Ionicons } from '@expo/vector-icons';
import { Stack, router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { ActivityIndicator, Pressable, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { ordersApi } from '../../src/api/orders';
import type { OrderDetail, OrderLine } from '../../src/domain/order.types';
import { formatMoney } from '../../src/utils/money';
import { orderStatusTone } from '../(tabs)/orders';

const dateTime = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—';

export default function OrderDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { colors: c, shadows: s } = useAppTheme();
  const [order, setOrder] = useState<OrderDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async (showRefresh = false) => {
    if (!id) return;
    if (showRefresh) setRefreshing(true);
    try {
      setOrder(await ordersApi.get(id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Order unavailable.');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [id]);

  useFocusEffect(useCallback(() => { void load(); }, [load]));

  async function act(fn: () => Promise<unknown>) {
    setBusy(true);
    try {
      await fn();
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Action failed.');
    } finally {
      setBusy(false);
    }
  }

  if (loading) {
    return (
      <SafeAreaView style={[styles.safe, styles.center, { backgroundColor: c.background }]}>
        <ActivityIndicator color={c.primary} />
      </SafeAreaView>
    );
  }

  if (!order) {
    return (
      <SafeAreaView style={[styles.safe, { backgroundColor: c.background }]} edges={['top', 'left', 'right']}>
        <View style={styles.body}>
          <ErrorBanner message={error ?? 'Order not found.'} onRetry={() => void load()} />
        </View>
      </SafeAreaView>
    );
  }

  const buyer = order.customerName || order.buyerReference || order.email || 'Unknown buyer';
  const netReceived = order.grandTotal - order.totalRefunded;

  return (
    <SafeAreaView style={[styles.safe, { backgroundColor: c.background }]} edges={['top', 'left', 'right']}>
      <Stack.Screen options={{ title: order.orderNumber }} />
      <ScrollView
        contentContainerStyle={styles.body}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void load(true)} tintColor={c.primary} colors={[c.primary]} />}
      >
        <Pressable onPress={() => router.back()} style={styles.backRow} hitSlop={8}>
          <Ionicons name="chevron-back" size={18} color={c.textSecondary} />
          <Text variant="caption" color={c.textSecondary}>Orders</Text>
        </Pressable>

        <View style={styles.titleRow}>
          <Text variant="title" style={styles.flex}>{order.orderNumber}</Text>
        </View>
        <Text variant="caption">{order.channel} · placed {dateTime(order.createdAt)}</Text>
        <View style={styles.pills}>
          <StatusPill label={order.status} tone={orderStatusTone(order.status)} size="sm" />
          <StatusPill label={order.paymentStatus} tone={order.paymentStatus === 'Paid' ? 'success' : order.paymentStatus === 'Refunded' ? 'danger' : 'neutral'} size="sm" />
          <StatusPill label={order.fulfillmentStatus} tone={order.fulfillmentStatus === 'Fulfilled' ? 'success' : 'neutral'} size="sm" />
        </View>

        {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

        {/* Line items */}
        <Card style={styles.card}>
          <Text variant="label" color={c.textMuted}>Line items &amp; money</Text>
          {order.lines.map((line) => <LineRow key={line.id} line={line} />)}
        </Card>

        {/* Totals */}
        <Card style={styles.card}>
          <TotalRow label="Subtotal" value={order.subtotal} />
          <TotalRow label="Tax" value={order.taxTotal} />
          <TotalRow label="Shipping" value={order.shippingTotal} />
          <TotalRow label="Total" value={order.grandTotal} strong />
          {order.totalRefunded > 0 && (
            <>
              <TotalRow label="Refunded to buyer" value={-order.totalRefunded} tone={c.warning} />
              <TotalRow label="Net received" value={netReceived} strong />
            </>
          )}
        </Card>

        {/* Payment + fulfillment actions */}
        <Card style={styles.card}>
          <Text variant="label" color={c.textMuted}>Fulfillment</Text>
          {order.isLocalPickup ? (
            <Text variant="caption">Local pickup — customer collects in person.</Text>
          ) : order.trackingNumber ? (
            <Text variant="body">
              {[order.shippingCarrier, order.trackingNumber].filter(Boolean).join(' · ')}
            </Text>
          ) : (
            <Text variant="caption">No tracking recorded yet.</Text>
          )}
          <View style={styles.metaGrid}>
            <MetaCell label="Shipped" value={dateTime(order.shippedAt)} />
            <MetaCell label="Delivered" value={dateTime(order.deliveredAt)} />
          </View>
          <View style={styles.actions}>
            {order.status === 'Pending' && (
              <Button label="Mark paid" size="sm" disabled={busy} onPress={() => void act(() => ordersApi.updateStatus(order.id, 'Paid'))} />
            )}
            {order.status !== 'Delivered' && order.status !== 'Cancelled' && order.status !== 'Refunded' && (
              <>
                <Button label="Mark shipped" size="sm" variant="secondary" disabled={busy} onPress={() => void act(() => ordersApi.updateTracking(order.id, { markShipped: true }))} />
                <Button label="Mark delivered" size="sm" variant="secondary" disabled={busy} onPress={() => void act(() => ordersApi.updateTracking(order.id, { markDelivered: true }))} />
              </>
            )}
          </View>
          <Text variant="caption" color={c.textMuted}>Refunds and returns are managed per line (coming to mobile next).</Text>
        </Card>

        {/* Customer */}
        <Card style={styles.card}>
          <Text variant="label" color={c.textMuted}>Customer</Text>
          <Text variant="bodyBold">{buyer}</Text>
          {order.email ? <Text variant="caption">{order.email}</Text> : null}
          {order.customerId ? (
            <Pressable onPress={() => router.push(`/customers/${order.customerId}`)} hitSlop={6}>
              <Text variant="caption" color={c.primary}>View customer →</Text>
            </Pressable>
          ) : null}
        </Card>

        {/* Ship to */}
        {!order.isLocalPickup && (order.shipToName || order.shipToLine1) ? (
          <Card style={styles.card}>
            <Text variant="label" color={c.textMuted}>Ship to</Text>
            {order.shipToName ? <Text variant="bodyBold">{order.shipToName}</Text> : null}
            <Text variant="caption">
              {[order.shipToLine1, order.shipToLine2, [order.shipToCity, order.shipToRegion, order.shipToPostalCode].filter(Boolean).join(', '), order.shipToCountry].filter(Boolean).join('\n')}
            </Text>
          </Card>
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
}

function LineRow({ line }: { line: OrderLine }) {
  const { colors: c } = useAppTheme();
  const sale = line.sale;
  const fees = (sale?.platformFees ?? 0) + (sale?.paymentFees ?? 0);
  return (
    <View style={[styles.lineRow, { borderTopColor: c.borderLight }]}>
      <View style={styles.lineHead}>
        <Text variant="bodyBold" numberOfLines={2} style={styles.flex}>{line.titleSnapshot}</Text>
        <Text variant="bodyBold">{formatMoney(sale?.netProfit ?? 0)}</Text>
      </View>
      <View style={styles.lineMeta}>
        <Text variant="caption" color={c.textMuted}>Sold {formatMoney(line.unitPrice)}</Text>
        {fees > 0 ? <Text variant="caption" color={c.textMuted}>Fees -{formatMoney(fees)}</Text> : null}
        <Text variant="caption" color={c.textMuted}>COGS {formatMoney(sale?.costOfGoods ?? line.unitCostSnapshot ?? 0)}</Text>
        {(sale?.totalRefunded ?? 0) > 0 ? <Text variant="caption" color={c.warning}>Refunded -{formatMoney(sale?.totalRefunded ?? 0)}</Text> : null}
      </View>
      {sale ? <Text variant="label" color={c.textMuted}>Net profit · true</Text> : <Text variant="caption" color={c.textMuted}>No sale line</Text>}
    </View>
  );
}

function TotalRow({ label, value, strong, tone }: { label: string; value: number; strong?: boolean; tone?: string }) {
  const { colors: c } = useAppTheme();
  return (
    <View style={styles.totalRow}>
      <Text variant={strong ? 'bodyBold' : 'body'} color={strong ? undefined : c.textSecondary}>{label}</Text>
      <Text variant={strong ? 'bodyBold' : 'body'} color={tone}>{formatMoney(value)}</Text>
    </View>
  );
}

function MetaCell({ label, value }: { label: string; value: string }) {
  const { colors: c } = useAppTheme();
  return (
    <View style={styles.metaCell}>
      <Text variant="label" color={c.textMuted}>{label}</Text>
      <Text variant="caption">{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  center: { alignItems: 'center', justifyContent: 'center' },
  body: { padding: spacing.lg, gap: spacing.sm, paddingBottom: spacing.xxl },
  backRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xxs, marginBottom: spacing.xs },
  titleRow: { flexDirection: 'row', alignItems: 'center' },
  flex: { flex: 1 },
  pills: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.xs },
  card: { gap: spacing.sm, marginTop: spacing.md },
  lineRow: { gap: spacing.xxs, borderTopWidth: 1, paddingTop: spacing.sm, marginTop: spacing.xs },
  lineHead: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
  lineMeta: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  totalRow: { flexDirection: 'row', justifyContent: 'space-between' },
  metaGrid: { flexDirection: 'row', gap: spacing.xl },
  metaCell: { gap: spacing.xxs },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginTop: spacing.xs },
});
