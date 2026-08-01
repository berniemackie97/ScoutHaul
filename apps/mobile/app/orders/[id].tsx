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
import { TextField } from '../../src/components/TextField';
import { spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { ordersApi } from '../../src/api/orders';
import type { OrderDetail } from '../../src/domain/order.types';
import { OrderLineRow, type LineEvents, type MutationRunner } from '../../src/features/orders/OrderLineWorkspace';
import { formatMoney } from '../../src/utils/money';
import { orderStatusTone } from '../(tabs)/orders';

const dateTime = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—';

export default function OrderDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { colors: c } = useAppTheme();
  const [order, setOrder] = useState<OrderDetail | null>(null);
  const [lineEvents, setLineEvents] = useState<Record<string, LineEvents>>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [editingTracking, setEditingTracking] = useState(false);
  const [carrier, setCarrier] = useState('');
  const [tracking, setTracking] = useState('');

  const load = useCallback(async (showRefresh = false) => {
    if (!id) return;
    if (showRefresh) setRefreshing(true);
    try {
      const detail = await ordersApi.get(id);
      const saleIds = Array.from(new Set(
        detail.lines.map((line) => line.sale?.id).filter((saleId): saleId is string => Boolean(saleId)),
      ));
      const entries = await Promise.all(
        saleIds.map(async (saleId) => {
          const [refunds, returns] = await Promise.all([
            ordersApi.listRefunds(saleId),
            ordersApi.listReturns(saleId),
          ]);
          return [saleId, { refunds, returns }] as const;
        }),
      );
      setOrder(detail);
      setLineEvents(Object.fromEntries(entries));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Order unavailable.');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [id]);

  useFocusEffect(useCallback(() => { void load(); }, [load]));

  // Controls are only disabled while the MUTATION is in flight. The refresh that follows runs in the
  // background — the old version held `busy` across the reload too, so every tap froze the whole
  // screen for two round-trips instead of one.
  async function act(fn: () => Promise<unknown>): Promise<boolean> {
    setBusy(true);
    try {
      await fn();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Action failed.');
      return false;
    } finally {
      setBusy(false);
    }
    await load();
    return true;
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
  // Terminal states — no further fulfillment actions make sense.
  const isClosed = order.status === 'Delivered' || order.status === 'Cancelled' || order.status === 'Refunded';

  return (
    <SafeAreaView style={[styles.safe, { backgroundColor: c.background }]} edges={['top', 'left', 'right']}>
      <Stack.Screen options={{ title: order.orderNumber }} />
      <ScrollView
        contentContainerStyle={styles.body}
        keyboardShouldPersistTaps="handled"
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

        <Card style={styles.card}>
          <Text variant="label" color={c.textMuted}>Line items, money, refunds &amp; returns</Text>
          {order.lines.map((line) => (
            <OrderLineRow
              key={line.id}
              line={line}
              events={line.sale ? lineEvents[line.sale.id] ?? { refunds: [], returns: [] } : { refunds: [], returns: [] }}
              busy={busy}
              runMutation={act}
            />
          ))}
        </Card>

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

        <Card style={styles.card}>
          <Text variant="label" color={c.textMuted}>Fulfillment</Text>
          {order.isLocalPickup ? (
            <Text variant="caption">Local pickup — customer collects in person.</Text>
          ) : order.trackingNumber ? (
            <View style={styles.trackingRow}>
              <Text variant="body" style={styles.flex}>
                {[order.shippingCarrier, order.trackingNumber].filter(Boolean).join(' · ')}
              </Text>
              <Pressable onPress={() => { setCarrier(order.shippingCarrier ?? ''); setTracking(order.trackingNumber ?? ''); setEditingTracking(true); }} hitSlop={8}>
                <Text variant="caption" color={c.primary}>Edit</Text>
              </Pressable>
            </View>
          ) : (
            <Text variant="caption">No tracking recorded yet.</Text>
          )}

          {/* Tracking entry — the API has always accepted carrier/number; the phone had no way to
              enter them, so shipping from mobile silently dropped the tracking the buyer needs. */}
          {!order.isLocalPickup && (editingTracking || (!order.trackingNumber && !isClosed)) ? (
            <View style={styles.trackingForm}>
              <TextField label="Carrier" value={carrier} onChangeText={setCarrier} placeholder="e.g. USPS, UPS, FedEx" autoCapitalize="characters" />
              <TextField label="Tracking number" value={tracking} onChangeText={setTracking} placeholder="Paste or scan the number" autoCapitalize="characters" />
              <View style={styles.actions}>
                {editingTracking ? (
                  <Button label="Cancel" size="sm" variant="ghost" disabled={busy} onPress={() => setEditingTracking(false)} />
                ) : null}
                <Button
                  label={order.shippedAt ? 'Save tracking' : 'Save & mark shipped'}
                  size="sm"
                  disabled={busy || !tracking.trim()}
                  onPress={() => void act(async () => {
                    await ordersApi.updateTracking(order.id, {
                      shippingCarrier: carrier.trim() || null,
                      trackingNumber: tracking.trim(),
                      ...(order.shippedAt ? {} : { markShipped: true }),
                    });
                    setEditingTracking(false);
                  })}
                />
              </View>
            </View>
          ) : null}

          <View style={styles.metaGrid}>
            <MetaCell label="Shipped" value={dateTime(order.shippedAt)} />
            <MetaCell label="Delivered" value={dateTime(order.deliveredAt)} />
          </View>
          <View style={styles.actions}>
            {order.status === 'Pending' && (
              <Button label="Mark paid" size="sm" disabled={busy} onPress={() => void act(() => ordersApi.updateStatus(order.id, 'Paid'))} />
            )}
            {/* Sequenced: you can only ship a paid order, and only mark delivered after it shipped —
                the old screen offered both at once, out of order, on unpaid orders. */}
            {!isClosed && order.status !== 'Pending' && !order.shippedAt && (
              <Button label="Mark shipped" size="sm" variant="secondary" disabled={busy} onPress={() => void act(() => ordersApi.updateTracking(order.id, { markShipped: true }))} />
            )}
            {!isClosed && order.shippedAt && !order.deliveredAt && (
              <Button label="Mark delivered" size="sm" variant="secondary" disabled={busy} onPress={() => void act(() => ordersApi.updateTracking(order.id, { markDelivered: true }))} />
            )}
          </View>
          <Text variant="caption" color={c.textMuted}>Shipment state is order-level. Refunds and returns are handled on each sale line above.</Text>
        </Card>

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
  totalRow: { flexDirection: 'row', justifyContent: 'space-between' },
  metaGrid: { flexDirection: 'row', gap: spacing.xl },
  trackingRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  trackingForm: { gap: spacing.sm, marginTop: spacing.xs },
  metaCell: { gap: spacing.xxs },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginTop: spacing.xs, alignItems: 'center' },
});
