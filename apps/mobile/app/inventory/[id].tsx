import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { Divider } from '../../src/components/Divider';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { PhotoStrip } from '../../src/components/PhotoThumbnail';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { radius, spacing, type ToneKey } from '../../src/components/theme';
import { useColors } from '../../src/context/ThemeContext';
import type { InventoryItem, InventoryStatus, MarketObservation } from '../../src/domain/types';
import { formatMoney } from '../../src/utils/money';
import { getPhotos, type ItemPhoto } from '../../src/utils/photos';

type WorkflowStage = {
  status: InventoryStatus;
  label: string;
  icon: keyof typeof Ionicons.glyphMap;
};

const workflowStages: WorkflowStage[] = [
  { status: 'NeedsCleaning', label: 'Clean', icon: 'water-outline' },
  { status: 'NeedsTesting', label: 'Test', icon: 'flask-outline' },
  { status: 'NeedsPhotos', label: 'Photo', icon: 'camera-outline' },
  { status: 'ReadyToList', label: 'Ready', icon: 'checkmark-circle-outline' },
  { status: 'Listed', label: 'Listed', icon: 'pricetag-outline' },
  { status: 'Sold', label: 'Sold', icon: 'cash-outline' },
  { status: 'Shipped', label: 'Shipped', icon: 'airplane-outline' },
];

function stageTone(current: InventoryStatus, stage: InventoryStatus): ToneKey {
  const order = workflowStages.map((s) => s.status);
  const ci = order.indexOf(current);
  const si = order.indexOf(stage);
  if (ci < 0 || si < 0) return 'neutral';
  if (si < ci) return 'success';
  if (si === ci) return 'primary';
  return 'neutral';
}

export default function InventoryDetailScreen() {
  const c = useColors();
  const { id } = useLocalSearchParams<{ id: string }>();
  const [item, setItem] = useState<InventoryItem | null>(null);
  const [comps, setComps] = useState<MarketObservation[]>([]);
  const [photos, setPhotos] = useState<ItemPhoto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  async function load() {
    if (!id) return;
    try {
      const [inv, obs, ph] = await Promise.all([
        api.inventoryItem(id),
        api.comps(`inventoryItemId=${encodeURIComponent(id)}`),
        getPhotos(id),
      ]);
      setItem(inv);
      setComps(obs);
      setPhotos(ph);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Item unavailable.');
    } finally {
      setIsLoading(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, [id]));

  async function setStatus(status: InventoryStatus) {
    if (!id) return;
    try {
      setItem(await api.changeInventoryStatus(id, status));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to update status.');
    }
  }

  async function archiveItem() {
    if (!id) return;
    try {
      await api.deleteInventory(id);
      router.replace('/(tabs)/inventory');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to archive item.');
    }
  }

  function confirmArchiveItem() {
    if (!item) return;
    Alert.alert(
      'Archive item?',
      item.title,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Archive', style: 'destructive', onPress: () => void archiveItem() },
      ],
    );
  }

  async function createDraftListing() {
    if (!item) return;
    const listPrice = item.estimatedSalePrice ?? item.sellerAskingPrice ?? item.purchasePrice;
    if (!listPrice || listPrice <= 0) {
      setError('Set an estimated sale price before creating a listing draft.');
      return;
    }

    try {
      await api.createListing({
        inventoryItemId: item.id,
        platform: 'eBay',
        listingUrl: null,
        listPrice,
        notes: null,
      });
      router.push('/listing-queue');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to create listing draft.');
    }
  }

  if (isLoading) {
    return (
      <Screen>
        <LoadingState message="Loading item..." />
      </Screen>
    );
  }

  if (!item) {
    return (
      <Screen>
        {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}
        <EmptyState icon="alert-circle-outline" title="Item not found" message="This item may have been deleted or is unavailable." />
      </Screen>
    );
  }

  const cost = item.purchasePrice ?? item.sellerAskingPrice;
  const projectedProfit = item.estimatedSalePrice && cost
    ? item.estimatedSalePrice - cost
    : null;

  return (
    <Screen>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      <View style={styles.titleRow}>
        <Text variant="title" style={styles.titleText}>{item.title}</Text>
        <StatusPill label={item.status} tone={item.status === 'Sold' ? 'success' : item.status === 'Listed' ? 'info' : 'neutral'} />
      </View>

      <PhotoStrip photos={photos.map((p) => p.localUri)} />

      <Card>
        <View style={styles.detailGrid}>
          <DetailRow label="Cost" value={formatMoney(cost)} />
          <DetailRow label="Estimated Value" value={formatMoney(item.estimatedSalePrice)} />
          <DetailRow
            label="Projected Profit"
            value={formatMoney(projectedProfit)}
            valueColor={projectedProfit && projectedProfit > 0 ? c.success : projectedProfit && projectedProfit < 0 ? c.danger : undefined}
          />
          <Divider />
          <DetailRow label="Condition" value={item.condition ?? 'Not set'} />
          <DetailRow label="Barcode" value={item.barcode ?? 'None'} />
        </View>
        {item.notes ? (
          <>
            <Divider />
            <Text variant="caption">{item.notes}</Text>
          </>
        ) : null}
      </Card>

      <View style={styles.actionRow}>
        <Button
          label="Edit"
          icon="create-outline"
          variant="secondary"
          fullWidth
          onPress={() => router.push({ pathname: '/inventory/edit', params: { id: item.id } })}
        />
        <Button
          label="Analyze"
          icon="calculator-outline"
          fullWidth
          onPress={() => router.push({ pathname: '/deal-analyzer', params: { inventoryItemId: item.id } })}
        />
      </View>
      <View style={styles.actionRow}>
        <Button
          label="Add Comp"
          icon="add-circle-outline"
          variant="secondary"
          fullWidth
          onPress={() => router.push({ pathname: '/comp-capture', params: { inventoryItemId: item.id, title: item.title } })}
        />
        <Button
          label="Marketplace"
          icon="open-outline"
          variant="secondary"
          fullWidth
          onPress={() => router.push({ pathname: '/marketplace-links', params: { query: item.title } })}
        />
      </View>
      <View style={styles.actionRow}>
        <Button
          label="Create Listing"
          icon="pricetag-outline"
          variant="secondary"
          fullWidth
          onPress={() => void createDraftListing()}
        />
        <Button
          label="Archive"
          icon="archive-outline"
          variant="danger"
          fullWidth
          onPress={confirmArchiveItem}
        />
      </View>

      <SectionHeader title="Workflow" />
      <Card noPadding>
        <View style={styles.workflowRow}>
          {workflowStages.map((stage) => {
            const tone = stageTone(item.status, stage.status);
            const isCurrent = item.status === stage.status;
            return (
              <Pressable
                key={stage.status}
                onPress={() => void setStatus(stage.status)}
                accessibilityRole="button"
                accessibilityLabel={`Set status to ${stage.label}`}
                accessibilityState={{ selected: isCurrent }}
                style={[styles.workflowStep, isCurrent && { backgroundColor: c.primaryLight }]}
              >
                <Ionicons
                  name={stage.icon}
                  size={20}
                  color={
                    tone === 'success' ? c.success
                      : tone === 'primary' ? c.primary
                        : c.textMuted
                  }
                />
                <Text
                  variant="label"
                  color={
                    tone === 'success' ? c.success
                      : tone === 'primary' ? c.primary
                        : c.textMuted
                  }
                >
                  {stage.label}
                </Text>
              </Pressable>
            );
          })}
        </View>
      </Card>

      <SectionHeader title={`Comps (${comps.length})`} />
      {comps.length === 0 ? (
        <Card>
          <EmptyState
            icon="analytics-outline"
            title="No comps yet"
            message="Add a sold comp or active listing to strengthen confidence."
            actionLabel="Add Comp"
            onAction={() => router.push({ pathname: '/comp-capture', params: { inventoryItemId: item.id, title: item.title } })}
          />
        </Card>
      ) : (
        comps.map((comp) => (
          <Card key={comp.id}>
            <View style={styles.compHeader}>
              <Text variant="bodyBold" numberOfLines={1} style={styles.flex}>
                {comp.title}
              </Text>
              <Text variant="metricSmall">{formatMoney(comp.totalPrice)}</Text>
            </View>
            <View style={styles.compMeta}>
              <StatusPill label={comp.observationType} size="sm" />
              <Text variant="caption">{comp.source}</Text>
            </View>
          </Card>
        ))
      )}
    </Screen>
  );
}

function DetailRow({ label, value, valueColor }: { label: string; value: string; valueColor?: string }) {
  return (
    <View style={styles.detailRow}>
      <Text variant="caption">{label}</Text>
      <Text variant="bodyBold" color={valueColor}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  titleRow: {
    gap: spacing.sm,
  },
  titleText: {
    flex: 1,
  },
  detailGrid: {
    gap: spacing.sm,
  },
  detailRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  actionRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  workflowRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
  },
  workflowStep: {
    alignItems: 'center',
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.sm,
    gap: spacing.xs,
    flexBasis: `${100 / 4}%`,
  },
  workflowStepActive: {
    borderRadius: radius.md,
  },
  compHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.sm,
  },
  compMeta: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  flex: {
    flex: 1,
  },
});
