import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
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
import type { InventoryIssue, InventoryItem, InventoryStatus, MarketObservation } from '../../src/domain/types';
import { commerceManagedStatuses, inventoryStatusLabel } from '../../src/domain/inventoryOptions';
import {
  archiveInventoryPolicy,
  inventoryCostBasis,
  inventoryCostBasisLabel,
  isOrderManagedInventory,
} from '../../src/domain/inventoryOperations';
import { factsFromInventoryItem, readinessBlockers } from '../../src/domain/lifecycleReadiness';
import { formatMoney } from '../../src/utils/money';
import { getPhotos, type ItemPhoto } from '../../src/utils/photos';
import { resolvePhotoUrl } from '../../src/utils/photoUrl';

type WorkflowStage = {
  status: InventoryStatus;
  label: string;
  icon: keyof typeof Ionicons.glyphMap;
};

const workflowStages: WorkflowStage[] = [
  { status: 'Draft', label: 'Draft', icon: 'document-outline' },
  { status: 'Researching', label: 'Research', icon: 'search-outline' },
  { status: 'Watchlist', label: 'Watch', icon: 'eye-outline' },
  { status: 'Purchased', label: 'Bought', icon: 'bag-check-outline' },
  { status: 'NeedsCleaning', label: 'Clean', icon: 'water-outline' },
  { status: 'NeedsTesting', label: 'Test', icon: 'flask-outline' },
  { status: 'NeedsRepair', label: 'Repair', icon: 'construct-outline' },
  { status: 'NeedsPhotos', label: 'Photo', icon: 'camera-outline' },
  { status: 'ReadyToList', label: 'Ready', icon: 'checkmark-circle-outline' },
  { status: 'Listed', label: 'Listed', icon: 'pricetag-outline' },
  { status: 'Sold', label: 'Sold', icon: 'cash-outline' },
  { status: 'Packed', label: 'Packed', icon: 'file-tray-full-outline' },
  { status: 'Shipped', label: 'Shipped', icon: 'airplane-outline' },
  { status: 'Delivered', label: 'Delivered', icon: 'checkmark-done-outline' },
  { status: 'Returned', label: 'Returned', icon: 'return-down-back-outline' },
  { status: 'Donated', label: 'Donated', icon: 'gift-outline' },
  { status: 'Disposed', label: 'Disposed', icon: 'trash-outline' },
];

const saleManagedStatuses = new Set<InventoryStatus>(commerceManagedStatuses);

const nextStatusMap: Partial<Record<InventoryStatus, InventoryStatus[]>> = {
  Draft: ['Researching', 'Purchased'],
  Watchlist: ['Purchased', 'Researching'],
  Researching: ['Purchased', 'NeedsPhotos'],
  Purchased: ['NeedsCleaning', 'NeedsTesting', 'NeedsPhotos'],
  NeedsCleaning: ['NeedsTesting', 'NeedsPhotos'],
  NeedsTesting: ['NeedsPhotos', 'ReadyToList'],
  NeedsRepair: ['NeedsTesting', 'NeedsPhotos'],
  NeedsPhotos: ['ReadyToList'],
  ReadyToList: ['Listed'],
  Listed: ['Sold'],
  Returned: ['NeedsRepair', 'ReadyToList'],
};

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
  const [issues, setIssues] = useState<InventoryIssue[]>([]);
  const [photos, setPhotos] = useState<ItemPhoto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  async function load() {
    if (!id) return;
    try {
      const [inv, obs, remoteIssues, localPhotos] = await Promise.all([
        api.inventoryItem(id),
        api.comps(`inventoryItemId=${encodeURIComponent(id)}`),
        api.inventoryIssues(id).catch(() => []),
        getPhotos(id),
      ]);
      setItem(inv);
      setComps(obs);
      setIssues(remoteIssues);
      const typedRemotePhotos = inv.photos?.length
        ? inv.photos.map((photo) => ({
            id: photo.id,
            inventoryItemId: id,
            localUri: resolvePhotoUrl(photo.thumbnailUrl) ?? photo.thumbnailUrl,
            isHero: photo.isHero,
            createdAt: photo.createdAt,
          }))
        : [];
      const legacyRemotePhotos =
        typedRemotePhotos.length > 0
          ? []
          : inv.photoUrls.map((uri) => ({
              id: uri,
              inventoryItemId: id,
              localUri: resolvePhotoUrl(uri) ?? uri,
              isHero: false,
              createdAt: inv.updatedAt,
            }));
      const remotePhotos = [...typedRemotePhotos, ...legacyRemotePhotos];
      setPhotos([...remotePhotos, ...localPhotos]);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Item unavailable.');
    } finally {
      setIsLoading(false);
    }
  }

  useFocusEffect(
    useCallback(() => {
      void load();
    }, [id])
  );

  async function setStatus(status: InventoryStatus) {
    if (!id) return;
    if (item && isOrderManagedInventory(item)) {
      router.push('/(tabs)/orders');
      return;
    }
    if (saleManagedStatuses.has(status)) {
      if (status === 'Sold' && item && !saleManagedStatuses.has(item.status)) {
        openRecordSale(item);
        return;
      }
      router.push('/(tabs)/orders');
      return;
    }
    if (item && (status === 'ReadyToList' || status === 'Listed')) {
      const blockers = readinessBlockers(status, { ...factsFromInventoryItem(item), photoCount: photos.length });
      if (blockers.length > 0) {
        setError(`Resolve ${blockers.length} readiness blocker${blockers.length === 1 ? '' : 's'} before moving to ${inventoryStatusLabel(status)}.`);
        return;
      }
    }
    try {
      setItem(await api.changeInventoryStatus(id, status));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to update status.');
    }
  }

  function openRecordSale(inventoryItem: InventoryItem) {
    router.push({
      pathname: '/record-sale',
      params: {
        inventoryItemId: inventoryItem.id,
        title: inventoryItem.title,
        listPrice: inventoryItem.estimatedSalePrice != null ? String(inventoryItem.estimatedSalePrice) : undefined,
        purchaseCost: inventoryItem.purchasePrice != null ? String(inventoryItem.purchasePrice) : undefined,
      },
    });
  }

  async function archiveItem() {
    if (!id || !item) return;
    const archivePolicy = archiveInventoryPolicy(item);
    if (!archivePolicy.allowed) {
      setError(archivePolicy.reason);
      return;
    }

    try {
      await api.deleteInventory(id);
      router.replace('/(tabs)/inventory');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to archive item.');
    }
  }

  function confirmArchiveItem() {
    if (!item) return;
    const archivePolicy = archiveInventoryPolicy(item);
    if (!archivePolicy.allowed) {
      Alert.alert('Archive locked', archivePolicy.reason, [
        { text: 'Cancel', style: 'cancel' },
        archivePolicy.recovery === 'orders'
          ? { text: 'Open Orders', onPress: () => router.push('/(tabs)/orders') }
          : { text: 'Edit Item', onPress: () => router.push({ pathname: '/inventory/edit', params: { id: item.id } }) },
      ]);
      return;
    }

    Alert.alert('Archive item?', item.title, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Archive', style: 'destructive', onPress: () => void archiveItem() },
    ]);
  }

  async function createDraftListing() {
    if (!item) return;
    if (isOrderManagedInventory(item)) {
      router.push('/(tabs)/orders');
      return;
    }
    if (item.status === 'Listed') {
      setError('This item is already marked Listed. Update the existing listing state before creating another draft.');
      return;
    }

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

  const cost = inventoryCostBasis(item);
  const projectedProfit = item.estimatedSalePrice && cost ? item.estimatedSalePrice - cost : null;
  const attributes = parseAttributes(item.attributesJson);
  const orderManaged = isOrderManagedInventory(item);
  const archivePolicy = archiveInventoryPolicy(item);
  const nextStatuses = nextStatusMap[item.status] ?? [];
  const dimensions = formatDimensions(item);
  const itemWeight = formatWeight(item);
  const hasPhysicalData = Boolean(
    item.barcode ||
      item.manufacturerPartNumber ||
      item.serialNumber ||
      item.countryOfOrigin ||
      item.manufactureYear ||
      dimensions ||
      itemWeight,
  );

  return (
    <Screen>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      <View style={styles.titleRow}>
        <Text variant="title" style={styles.titleText}>
          {item.title}
        </Text>
        <StatusPill label={inventoryStatusLabel(item.status)} tone={item.status === 'Sold' ? 'success' : item.status === 'Listed' ? 'info' : 'neutral'} />
      </View>

      <PhotoStrip photos={photos.map((p) => p.localUri)} />

      <Card>
        <View style={styles.detailGrid}>
          <DetailRow label={inventoryCostBasisLabel(item)} value={formatMoney(cost)} />
          <DetailRow label="Estimated Value" value={formatMoney(item.estimatedSalePrice)} />
          <DetailRow
            label="Projected Profit"
            value={formatMoney(projectedProfit)}
            valueColor={projectedProfit && projectedProfit > 0 ? c.success : projectedProfit && projectedProfit < 0 ? c.danger : undefined}
          />
          <Divider />
          <DetailRow label="Condition" value={item.condition ?? 'Not set'} />
          <DetailRow label="Category" value={item.category ?? 'Not set'} />
          <DetailRow label="Brand/model" value={[item.brand, item.model].filter(Boolean).join(' / ') || 'Not set'} />
          <DetailRow label="Size" value={[item.sizeSystem, item.sizeValue].filter(Boolean).join(' · ') || 'Not set'} />
          <DetailRow label="Storage" value={item.storageLocation ?? 'Not set'} />
        </View>
        {item.notes ? (
          <>
            <Divider />
            <Text variant="caption">{item.notes}</Text>
          </>
        ) : null}
      </Card>

      <Card>
        <SectionHeader title="Operations context" />
        <View style={styles.detailGrid}>
          <DetailRow label="Record type" value={item.isBundle ? `Bundle · ${item.bundleComponentCount ?? 0} component${item.bundleComponentCount === 1 ? '' : 's'}` : 'Single item'} />
          <DetailRow label="Lifecycle owner" value={orderManaged ? 'Orders' : 'Inventory'} />
          {item.acquisitionLotName ? <DetailRow label="Acquisition lot" value={item.acquisitionLotName} /> : null}
          {item.acquisitionAllocatedCost != null ? <DetailRow label="Allocated lot cost" value={formatMoney(item.acquisitionAllocatedCost)} /> : null}
        </View>
        {orderManaged ? (
          <>
            <Divider />
            <Text variant="caption">
              Sale and fulfillment stages are controlled by Orders. Use the order workspace for shipping, delivery, return, or refund changes.
            </Text>
          </>
        ) : item.isBundle ? (
          <>
            <Divider />
            <Text variant="caption">
              Bundle readiness requires component selection in the admin. Mobile can inspect, price, list, and record the bundle sale.
            </Text>
          </>
        ) : null}
        {item.acquisitionLotId ? (
          <>
            <Divider />
            <Button
              label="Open acquisition lot"
              icon="receipt-outline"
              variant="secondary"
              onPress={() => router.push(`/purchases/${item.acquisitionLotId}`)}
            />
          </>
        ) : null}
      </Card>

      {hasPhysicalData ? (
        <Card>
          <SectionHeader title="Identifiers & physical data" />
          <View style={styles.detailGrid}>
            {item.barcode ? <DetailRow label="Barcode / GTIN" value={item.barcode} /> : null}
            {item.manufacturerPartNumber ? <DetailRow label="MPN" value={item.manufacturerPartNumber} /> : null}
            {item.serialNumber ? <DetailRow label="Serial" value={item.serialNumber} /> : null}
            {item.countryOfOrigin ? <DetailRow label="Country of origin" value={item.countryOfOrigin} /> : null}
            {item.manufactureYear ? <DetailRow label="Manufacture year" value={String(item.manufactureYear)} /> : null}
            {dimensions ? <DetailRow label="Dimensions" value={dimensions} /> : null}
            {itemWeight ? <DetailRow label="Weight" value={itemWeight} /> : null}
          </View>
        </Card>
      ) : null}

      {attributes.length > 0 ? (
        <Card>
          <SectionHeader title="Category Details" />
          <View style={styles.detailGrid}>
            {attributes.map(([key, value]) => (
              <DetailRow key={key} label={formatAttributeLabel(key)} value={value} />
            ))}
          </View>
        </Card>
      ) : null}

      <View style={styles.actionRow}>
        <Button
          label="Edit"
          icon="create-outline"
          variant="secondary"
          onPress={() => router.push({ pathname: '/inventory/edit', params: { id: item.id } })}
        />
        <Button
          label="Analyze"
          icon="calculator-outline"
          onPress={() => router.push({ pathname: '/deal-analyzer', params: { inventoryItemId: item.id } })}
        />
      </View>
      <View style={styles.actionRow}>
        <Button
          label="Add Comp"
          icon="add-circle-outline"
          variant="secondary"
          onPress={() =>
            router.push({
              pathname: '/comp-capture',
              params: { inventoryItemId: item.id, title: item.title },
            })
          }
        />
        <Button
          label="Marketplace"
          icon="open-outline"
          variant="secondary"
          onPress={() => router.push({ pathname: '/marketplace-links', params: { query: item.title } })}
        />
      </View>
      <View style={styles.actionRow}>
        <Button
          label="Generate Listing"
          icon="sparkles-outline"
          onPress={() => router.push({ pathname: '/generate-listing', params: { id: item.id } })}
        />
      </View>
      <View style={styles.actionRow}>
        {orderManaged ? (
          <Button label="Open Orders" icon="receipt-outline" variant="secondary" fullWidth onPress={() => router.push('/(tabs)/orders')} />
        ) : (
          <>
            <Button label="Create Listing" icon="pricetag-outline" variant="secondary" onPress={() => void createDraftListing()} />
            <Button label="Record Sale" icon="cash-outline" variant="secondary" onPress={() => openRecordSale(item)} />
          </>
        )}
      </View>
      <View style={styles.actionRow}>
        <Button
          label={archivePolicy.allowed ? 'Archive' : 'Archive locked'}
          icon={archivePolicy.allowed ? 'archive-outline' : 'lock-closed-outline'}
          variant={archivePolicy.allowed ? 'danger' : 'secondary'}
          fullWidth
          onPress={confirmArchiveItem}
        />
      </View>

      <SectionHeader title="Next operations" />
      <Card>
        {orderManaged ? (
          <View style={styles.nextActionStack}>
            <Text variant="caption">This item is tied to an order. Continue fulfillment from Orders.</Text>
            <Button label="Open Orders" icon="receipt-outline" variant="secondary" onPress={() => router.push('/(tabs)/orders')} />
          </View>
        ) : nextStatuses.length > 0 ? (
          <View style={styles.nextActionStack}>
            {nextStatuses.slice(0, 2).map((status) => (
              <Button
                key={status}
                label={status === 'Sold' ? 'Record sale' : `Move to ${inventoryStatusLabel(status)}`}
                icon={status === 'Sold' ? 'cash-outline' : 'arrow-forward-circle-outline'}
                variant={status === 'Sold' ? 'primary' : 'secondary'}
                onPress={() => void setStatus(status)}
              />
            ))}
          </View>
        ) : (
          <Text variant="caption">No recommended next inventory action for this stage.</Text>
        )}
      </Card>

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
                accessibilityLabel={
                  saleManagedStatuses.has(stage.status) ? `Open order workspace for ${stage.label}` : `Set status to ${stage.label}`
                }
                accessibilityState={{ selected: isCurrent }}
                style={[styles.workflowStep, isCurrent && { backgroundColor: c.primaryLight }]}
              >
                <Ionicons
                  name={stage.icon}
                  size={20}
                  color={tone === 'success' ? c.success : tone === 'primary' ? c.primary : c.textMuted}
                />
                <Text variant="label" color={tone === 'success' ? c.success : tone === 'primary' ? c.primary : c.textMuted}>
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
            onAction={() =>
              router.push({
                pathname: '/comp-capture',
                params: { inventoryItemId: item.id, title: item.title },
              })
            }
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

      <SectionHeader title={`Issues (${issues.length})`} />
      {issues.length === 0 ? (
        <Card>
          <Text variant="caption">No documented damage or missing parts.</Text>
        </Card>
      ) : (
        issues.map((issue) => (
          <Card key={issue.id}>
            <View style={styles.compHeader}>
              <Text variant="bodyBold" numberOfLines={1} style={styles.flex}>
                {issue.issueType}
              </Text>
              <StatusPill label={issue.severity} tone={issue.isResolved ? 'success' : 'warning'} size="sm" />
            </View>
            {issue.note ? <Text variant="caption">{issue.note}</Text> : null}
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
      <Text variant="bodyBold" color={valueColor}>
        {value}
      </Text>
    </View>
  );
}

function parseAttributes(value?: string | null): [string, string][] {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return [];
    return Object.entries(parsed as Record<string, unknown>)
      .filter(([, entry]) => typeof entry === 'string' && entry.trim().length > 0)
      .map(([key, entry]) => [key, entry as string]);
  } catch {
    return [];
  }
}

function formatAttributeLabel(key: string): string {
  return key
    .replace(/([A-Z])/g, ' $1')
    .replace(/^./, (letter) => letter.toUpperCase())
    .trim();
}

function formatDimensions(item: InventoryItem): string | null {
  const parts: string[] = [];
  if (item.length != null) parts.push(`L ${formatCompactNumber(item.length)}`);
  if (item.width != null) parts.push(`W ${formatCompactNumber(item.width)}`);
  if (item.height != null) parts.push(`H ${formatCompactNumber(item.height)}`);
  if (parts.length === 0) return null;
  return `${parts.join(' × ')} ${item.dimensionUnit === 'Centimeters' ? 'cm' : 'in'}`;
}

function formatWeight(item: InventoryItem): string | null {
  if (item.weight == null) return null;
  return `${formatCompactNumber(item.weight)} ${weightUnitLabel(item.weightUnit)}`;
}

function formatCompactNumber(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, '').replace(/\.$/, '');
}

function weightUnitLabel(unit: InventoryItem['weightUnit']): string {
  switch (unit) {
    case 'Pounds':
      return 'lb';
    case 'Grams':
      return 'g';
    case 'Kilograms':
      return 'kg';
    case 'Ounces':
    default:
      return 'oz';
  }
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
  nextActionStack: {
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
