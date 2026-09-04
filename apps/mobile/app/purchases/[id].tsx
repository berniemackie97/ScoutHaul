import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { Divider } from '../../src/components/Divider';
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
import {
  acquisitionAllocatedTotal,
  acquisitionDisplayName,
  acquisitionItemCount,
  acquisitionMethodLabel,
  acquisitionUnallocatedCost,
} from '../../src/domain/acquisition';
import type { AcquisitionLot } from '../../src/domain/types';
import { formatMoney } from '../../src/utils/money';
import { inventoryStatusTone } from '../../src/utils/statusTone';

export default function PurchaseDetailScreen() {
  const c = useColors();
  const { id } = useLocalSearchParams<{ id: string }>();
  const [lot, setLot] = useState<AcquisitionLot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  async function load(showRefresh = false) {
    if (!id) return;
    if (showRefresh) setRefreshing(true);
    try {
      setLot(await api.acquisitionLot(id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Purchase unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, [id]));

  if (isLoading) {
    return (
      <Screen>
        <LoadingState message="Loading purchase..." />
      </Screen>
    );
  }

  if (!lot) {
    return (
      <Screen>
        {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}
        <EmptyState icon="receipt-outline" title="Purchase not found" message="This acquisition lot may have been deleted." />
      </Screen>
    );
  }

  const allocated = acquisitionAllocatedTotal(lot);
  const unallocated = acquisitionUnallocatedCost(lot);
  const items = lot.items ?? [];

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      <View style={styles.header}>
        <Button label="Purchases" icon="arrow-back" size="sm" variant="ghost" onPress={() => router.push('/purchases')} />
        <StatusPill label={acquisitionMethodLabel(lot.allocationMethod)} tone="primary" />
      </View>

      <View style={styles.titleBlock}>
        <Text variant="title">{acquisitionDisplayName(lot)}</Text>
        <Text variant="caption" color={c.textMuted}>
          {[lot.supplierName, lot.externalReference, formatDate(lot.purchasedAt)].filter(Boolean).join(' · ') || 'No supplier/reference'}
        </Text>
      </View>

      <View style={styles.metricRow}>
        <MetricCard label="Landed" value={formatMoney(lot.totalLandedCost ?? 0)} icon="cash-outline" tone="primary" />
        <MetricCard label="Allocated" value={formatMoney(allocated)} icon="cube-outline" tone="success" />
      </View>
      <View style={styles.metricRow}>
        <MetricCard label="Unallocated" value={formatMoney(unallocated)} icon="alert-circle-outline" tone={Math.abs(unallocated) > 0.009 ? 'warning' : 'neutral'} />
        <MetricCard label="Items" value={String(acquisitionItemCount(lot))} icon="layers-outline" tone="info" />
      </View>

      <Card>
        <SectionHeader title="Cost breakdown" />
        <DetailRow label="Subtotal" value={formatMoney(lot.purchaseSubtotal ?? 0)} />
        <DetailRow label="Shipping" value={formatMoney(lot.shippingCost ?? 0)} />
        <DetailRow label="Tax" value={formatMoney(lot.taxCost ?? 0)} />
        <DetailRow label="Buyer/platform fees" value={formatMoney(lot.feeCost ?? 0)} />
        <DetailRow label="Other" value={formatMoney(lot.otherCost ?? 0)} />
        <Divider />
        <DetailRow label="Total landed cost" value={formatMoney(lot.totalLandedCost ?? 0)} strong />
        <DetailRow label="Allocated to inventory" value={formatMoney(allocated)} strong />
        <DetailRow label="Remaining unallocated" value={formatMoney(unallocated)} strong valueColor={Math.abs(unallocated) > 0.009 ? c.warning : c.success} />
      </Card>

      {lot.notes ? (
        <Card>
          <SectionHeader title="Notes" />
          <Text variant="caption">{lot.notes}</Text>
        </Card>
      ) : null}

      <SectionHeader title={`Allocated inventory (${items.length})`} />
      {items.length === 0 ? (
        <Card>
          <EmptyState
            icon="cube-outline"
            title="No itemized inventory"
            message="Itemize this purchase in the admin portal to allocate landed cost to inventory."
          />
        </Card>
      ) : (
        items.map((item) => (
          <Pressable
            key={item.id}
            onPress={() => router.push(`/inventory/${item.inventoryItemId}`)}
            accessibilityRole="button"
            accessibilityLabel={`Open ${item.inventoryItemTitle ?? 'inventory item'}`}
            style={({ pressed }) => [pressed && { opacity: 0.85 }]}
          >
            <Card>
              <View style={styles.itemHeader}>
                <View style={styles.itemTitle}>
                  <Text variant="bodyBold" numberOfLines={2}>{item.inventoryItemTitle ?? 'Inventory item'}</Text>
                  {item.notes ? <Text variant="caption" color={c.textMuted}>{item.notes}</Text> : null}
                </View>
                <Ionicons name="chevron-forward" size={18} color={c.textMuted} />
              </View>
              <View style={styles.itemMeta}>
                <StatusPill label={item.inventoryItemStatus ?? 'Unknown'} tone={inventoryStatusTone(item.inventoryItemStatus ?? '')} size="sm" />
                <StatusPill label={formatMoney(item.allocatedCost ?? 0)} tone="success" size="sm" />
                {item.allocationWeight != null ? <StatusPill label={`Weight ${item.allocationWeight}`} tone="neutral" size="sm" /> : null}
              </View>
            </Card>
          </Pressable>
        ))
      )}
    </Screen>
  );
}

function DetailRow({ label, value, strong, valueColor }: { label: string; value: string; strong?: boolean; valueColor?: string }) {
  return (
    <View style={styles.detailRow}>
      <Text variant="caption">{label}</Text>
      <Text variant={strong ? 'bodyBold' : 'body'} color={valueColor}>{value}</Text>
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
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  titleBlock: {
    gap: spacing.xs,
  },
  metricRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  detailRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  itemHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  itemTitle: {
    flex: 1,
    gap: spacing.xxs,
  },
  itemMeta: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
  },
});
