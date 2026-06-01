import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { FilterChips } from '../../src/components/FilterChips';
import { PhotoThumbnail } from '../../src/components/PhotoThumbnail';
import { SkeletonList } from '../../src/components/Skeleton';
import { SwipeableRow } from '../../src/components/SwipeableRow';
import { Screen } from '../../src/components/Screen';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing, type ToneKey } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import type { InventoryItem, InventoryStatus } from '../../src/domain/types';
import { formatMoney } from '../../src/utils/money';
import { getHeroPhoto } from '../../src/utils/photos';
import { resolvePhotoUrl } from '../../src/utils/photoUrl';

type StatusFilter = 'All' | 'Active' | 'NeedsWork' | 'Listed' | 'Sold';

const filterOptions: { value: StatusFilter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Active', label: 'Active' },
  { value: 'NeedsWork', label: 'Needs Work' },
  { value: 'Listed', label: 'Listed' },
  { value: 'Sold', label: 'Sold' },
];

const needsWorkStatuses: InventoryStatus[] = [
  'NeedsCleaning', 'NeedsTesting', 'NeedsPhotos', 'ReadyToList', 'Researching',
];

function statusTone(status: InventoryStatus): ToneKey {
  if (status === 'Sold' || status === 'Delivered') return 'success';
  if (status === 'Listed') return 'info';
  if (needsWorkStatuses.includes(status)) return 'warning';
  if (status === 'Draft' || status === 'Watchlist') return 'neutral';
  return 'neutral';
}

function formatStatus(status: InventoryStatus): string {
  const map: Partial<Record<InventoryStatus, string>> = {
    NeedsCleaning: 'Needs Cleaning',
    NeedsTesting: 'Needs Testing',
    NeedsPhotos: 'Needs Photos',
    ReadyToList: 'Ready to List',
  };
  return map[status] ?? status;
}

function useHeroUri(item: InventoryItem) {
  const [uri, setUri] = useState<string | null>(null);
  useEffect(() => {
    const typedHero = item.photos?.find((photo) => photo.isHero) ?? item.photos?.[0];
    const remoteHero = resolvePhotoUrl(
      item.heroPhotoThumbnailUrl ?? typedHero?.thumbnailUrl ?? item.photoUrls?.[0],
    );
    if (remoteHero) {
      setUri(remoteHero);
      return;
    }

    void getHeroPhoto(item.id).then(setUri);
  }, [item.id, item.heroPhotoThumbnailUrl, item.photoUrls, item.photos]);
  return uri;
}

function ItemCard({
  item,
  onPress,
  onAnalyze,
  onCreateListing,
  onArchive,
}: {
  item: InventoryItem;
  onPress: () => void;
  onAnalyze: () => void;
  onCreateListing: () => void;
  onArchive: () => void;
}) {
  const { colors: c, shadows: s } = useAppTheme();
  const heroUri = useHeroUri(item);
  return (
    <View style={[styles.itemCard, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm }]}>
      <Pressable
        onPress={onPress}
        accessibilityRole="button"
        accessibilityLabel={`${item.title}, ${formatStatus(item.status)}`}
        style={({ pressed }) => [pressed && { opacity: 0.85 }]}
      >
        <View style={styles.itemRow}>
          <PhotoThumbnail uri={heroUri} size={48} />
          <View style={styles.itemContent}>
            <View style={styles.itemHeader}>
              <Text variant="bodyBold" numberOfLines={1} style={styles.itemTitle}>
                {item.title}
              </Text>
              <StatusPill
                label={formatStatus(item.status)}
                tone={statusTone(item.status)}
                size="sm"
              />
            </View>
            <View style={styles.itemMeta}>
              <Text variant="caption">
                {item.condition ?? 'Condition unknown'}
              </Text>
              <Text variant="caption">
                Cost {formatMoney(item.purchasePrice ?? item.sellerAskingPrice)}
                {item.estimatedSalePrice ? ` · Value ${formatMoney(item.estimatedSalePrice)}` : ''}
              </Text>
            </View>
          </View>
        </View>
      </Pressable>
      <View style={styles.itemActions}>
        <InlineAction icon="calculator-outline" label="Analyze" color={c.primary} onPress={onAnalyze} />
        <InlineAction icon="pricetag-outline" label="List" color={c.info} onPress={onCreateListing} />
        <InlineAction icon="archive-outline" label="Archive" color={c.danger} onPress={onArchive} />
      </View>
    </View>
  );
}

export default function InventoryListScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const [items, setItems] = useState<InventoryItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [filter, setFilter] = useState<StatusFilter>('All');
  const [search, setSearch] = useState('');

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      setItems(await api.inventory());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Inventory unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  async function archiveItem(id: string) {
    try {
      await api.deleteInventory(id);
      setItems((current) => current.filter((item) => item.id !== id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to archive item.');
    }
  }

  function confirmArchiveItem(item: InventoryItem) {
    Alert.alert(
      'Archive item?',
      item.title,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Archive', style: 'destructive', onPress: () => void archiveItem(item.id) },
      ],
    );
  }

  async function createDraftListing(item: InventoryItem) {
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

  const filtered = useMemo(() => {
    let result = items;
    if (filter === 'Active') {
      result = result.filter((i) => !['Sold', 'Delivered', 'Shipped', 'Donated', 'Disposed', 'Returned', 'Listed'].includes(i.status));
    } else if (filter === 'NeedsWork') {
      result = result.filter((i) => needsWorkStatuses.includes(i.status));
    } else if (filter === 'Listed') {
      result = result.filter((i) => i.status === 'Listed');
    } else if (filter === 'Sold') {
      result = result.filter((i) => ['Sold', 'Delivered', 'Shipped', 'Packed'].includes(i.status));
    }
    if (search.trim()) {
      const q = search.toLowerCase();
      result = result.filter((i) =>
        i.title.toLowerCase().includes(q) ||
        i.barcode?.toLowerCase().includes(q) ||
        i.condition?.toLowerCase().includes(q),
      );
    }
    return result;
  }, [items, filter, search]);

  return (
    <Screen
      refreshing={refreshing}
      onRefresh={() => void load(true)}
      edges={['top', 'left', 'right']}
    >
      <View style={styles.header}>
        <Text variant="title">Inventory</Text>
        <Button
          label="Add"
          icon="add-outline"
          size="sm"
          onPress={() => router.push('/inventory/edit')}
        />
      </View>

      <TextField
        label=""
        value={search}
        onChangeText={setSearch}
        placeholder="Search by name, barcode, condition..."
      />

      <FilterChips options={filterOptions} selected={filter} onSelect={setFilter} />

      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      {isLoading ? (
        <SkeletonList count={4} />
      ) : filtered.length === 0 ? (
        items.length === 0 ? (
          <EmptyState
            icon="cube-outline"
            title="No inventory yet"
            message="Start by scouting an item or adding one manually."
            actionLabel="Add Item"
            onAction={() => router.push('/inventory/edit')}
          />
        ) : (
          <EmptyState
            icon="filter-outline"
            title="No matches"
            message="Try adjusting your search or filter."
          />
        )
      ) : (
        <>
          <Text variant="caption">{filtered.length} item{filtered.length !== 1 ? 's' : ''}</Text>
          {filtered.map((item) => (
            <SwipeableRow
              key={item.id}
              leftAction={{ icon: 'calculator-outline', label: 'Analyze', color: c.primary, onPress: () => router.push({ pathname: '/deal-analyzer', params: { inventoryItemId: item.id } }) }}
              rightAction={{ icon: 'archive-outline', label: 'Archive', color: c.danger, onPress: () => confirmArchiveItem(item) }}
            >
              <ItemCard
                item={item}
                onPress={() => router.push(`/inventory/${item.id}`)}
                onAnalyze={() => router.push({ pathname: '/deal-analyzer', params: { inventoryItemId: item.id } })}
                onCreateListing={() => void createDraftListing(item)}
                onArchive={() => confirmArchiveItem(item)}
              />
            </SwipeableRow>
          ))}
        </>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  itemCard: {
    borderRadius: radius.lg,
    padding: spacing.lg,
    borderWidth: 1,
  },
  itemRow: {
    flexDirection: 'row',
    gap: spacing.md,
    alignItems: 'center',
  },
  itemContent: {
    flex: 1,
    gap: spacing.sm,
  },
  itemHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.sm,
  },
  itemTitle: {
    flex: 1,
  },
  itemMeta: {
    gap: spacing.xxs,
  },
  itemActions: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    paddingTop: spacing.md,
    marginTop: spacing.md,
    borderTopWidth: 1,
    borderTopColor: 'rgba(148, 163, 184, 0.25)',
  },
  inlineAction: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xxs,
    paddingVertical: spacing.xs,
    paddingHorizontal: spacing.sm,
  },
});

function InlineAction({ icon, label, color, onPress }: {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  color: string;
  onPress: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={label}
      hitSlop={6}
      style={styles.inlineAction}
    >
      <Ionicons name={icon} size={16} color={color} />
      <Text variant="caption" color={color}>{label}</Text>
    </Pressable>
  );
}
