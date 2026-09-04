import { Ionicons } from '@expo/vector-icons';
import { Stack, router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import { Card } from '../../src/components/Card';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { MetricCard } from '../../src/components/MetricCard';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { spacing } from '../../src/components/theme';
import { useColors } from '../../src/context/ThemeContext';
import type { CustomerAddress, CustomerDetail, OrderSummary } from '../../src/domain/types';
import { formatMoney } from '../../src/utils/money';
import { orderStatusTone } from '../../src/utils/statusTone';

const dateLabel = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(new Date(value)) : '—';

export default function CustomerDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const c = useColors();
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async (showRefresh = false) => {
    if (!id) return;
    if (showRefresh) setRefreshing(true);
    try {
      setCustomer(await api.customer(id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Customer unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }, [id]);

  useFocusEffect(useCallback(() => { void load(); }, [load]));

  if (isLoading) {
    return <Screen><LoadingState message="Loading customer..." /></Screen>;
  }

  if (!customer) {
    return (
      <Screen>
        <ErrorBanner message={error ?? 'Customer not found.'} onRetry={() => void load()} />
      </Screen>
    );
  }

  const name = customer.displayName || customer.email || customer.channelIdentities?.[0]?.externalUserId || 'Buyer';

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      <Stack.Screen options={{ title: name }} />
      <Pressable onPress={() => router.back()} style={styles.backRow} hitSlop={8}>
        <Ionicons name="chevron-back" size={18} color={c.textSecondary} />
        <Text variant="caption" color={c.textSecondary}>Customers</Text>
      </Pressable>

      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text variant="title">{name}</Text>
          <Text variant="caption">{customer.email || customer.phone || `Joined ${dateLabel(customer.createdAt)}`}</Text>
        </View>
        {customer.acceptsMarketing ? <StatusPill label="Marketing" tone="info" size="sm" /> : null}
      </View>

      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      <View style={styles.metricRow}>
        <MetricCard label="Lifetime" value={formatMoney(customer.lifetimeSpend ?? 0)} icon="wallet-outline" tone="success" />
        <MetricCard label="Orders" value={String(customer.orderCount ?? 0)} icon="receipt-outline" tone="primary" />
      </View>
      <View style={styles.metricRow}>
        <MetricCard label="Credit" value={formatMoney(customer.storeCreditBalance ?? 0)} icon="card-outline" tone={(customer.storeCreditBalance ?? 0) > 0 ? 'warning' : 'neutral'} />
        <MetricCard label="Last Order" value={dateLabel(customer.lastOrderAt)} icon="calendar-outline" />
      </View>

      <SectionHeader title="Channel identities" />
      {(customer.channelIdentities ?? []).length === 0 ? (
        <Card><Text variant="caption">No channel identity records.</Text></Card>
      ) : (
        customer.channelIdentities!.map((identity) => (
          <Card key={identity.id}>
            <View style={styles.identityTop}>
              <Text variant="bodyBold">{identity.displayName || identity.externalUserId}</Text>
              <StatusPill label={identity.channel} tone="neutral" size="sm" />
            </View>
            <Text variant="caption" color={c.textMuted}>{identity.externalUserId}</Text>
            {identity.email ? <Text variant="caption">{identity.email}</Text> : null}
            <Text variant="caption" color={c.textMuted}>Seen {dateLabel(identity.firstSeenAt)} → {dateLabel(identity.lastSeenAt)}</Text>
          </Card>
        ))
      )}

      <SectionHeader title="Order history" />
      {(customer.orders ?? []).length === 0 ? (
        <EmptyState icon="receipt-outline" title="No orders" message="Orders will attach here when this customer is recognized during recording or import." />
      ) : (
        customer.orders.map((order) => <OrderCard key={order.id} order={order} />)
      )}

      <SectionHeader title="Addresses" />
      {(customer.addresses ?? []).length === 0 ? (
        <Card><Text variant="caption">No saved addresses.</Text></Card>
      ) : (
        customer.addresses.map((address) => <AddressCard key={address.id} address={address} />)
      )}
    </Screen>
  );
}

function OrderCard({ order }: { order: OrderSummary }) {
  const c = useColors();
  return (
    <Pressable
      onPress={() => router.push(`/orders/${order.id}`)}
      accessibilityRole="button"
      accessibilityLabel={`Open order ${order.orderNumber}`}
      style={({ pressed }) => [pressed && { opacity: 0.85 }]}
    >
      <Card>
        <View style={styles.identityTop}>
          <View style={styles.headerText}>
            <Text variant="bodyBold">{order.orderNumber}</Text>
            <Text variant="caption" color={c.textMuted}>{order.channel} · {dateLabel(order.createdAt)}</Text>
          </View>
          <StatusPill label={order.status} tone={orderStatusTone(order.status)} size="sm" />
        </View>
        <View style={[styles.customerStats, { borderTopColor: c.borderLight }]}>
          <Stat label="Items" value={String(order.itemCount ?? 0)} />
          <Stat label="Total" value={formatMoney(order.grandTotal ?? 0)} />
          <Stat label="Fulfillment" value={order.fulfillmentStatus} />
        </View>
      </Card>
    </Pressable>
  );
}

function AddressCard({ address }: { address: CustomerAddress }) {
  const c = useColors();
  const cityLine = [address.city, address.region, address.postalCode].filter(Boolean).join(', ');
  return (
    <Card>
      <View style={styles.identityTop}>
        <Text variant="bodyBold">{address.recipientName}</Text>
        {address.isDefault ? <StatusPill label="Default" tone="primary" size="sm" /> : null}
      </View>
      <Text variant="caption" color={c.textMuted}>
        {[address.line1, address.line2, cityLine, address.country].filter(Boolean).join('\n')}
      </Text>
      {address.phone ? <Text variant="caption">{address.phone}</Text> : null}
    </Card>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  const c = useColors();
  return (
    <View style={styles.stat}>
      <Text variant="label" color={c.textMuted}>{label}</Text>
      <Text variant="caption" numberOfLines={1}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  backRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xxs },
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.md },
  headerText: { flex: 1, gap: spacing.xxs },
  metricRow: { flexDirection: 'row', gap: spacing.md },
  identityTop: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: spacing.md },
  customerStats: { flexDirection: 'row', gap: spacing.md, borderTopWidth: 1, paddingTop: spacing.md, marginTop: spacing.xs },
  stat: { flex: 1, gap: spacing.xxs },
});
