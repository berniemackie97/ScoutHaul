import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { FilterChips } from '../src/components/FilterChips';
import { SkeletonList } from '../src/components/Skeleton';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { InventoryItem, InventoryStatus, Listing, ListingStatus } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';
import { listingReadinessTone, listingStatusTone } from '../src/utils/statusTone';

type ListingFilter = 'All' | 'Ready' | ListingStatus;

const filterOptions: { value: ListingFilter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Ready', label: 'Ready' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Active', label: 'Active' },
  { value: 'Sold', label: 'Sold' },
  { value: 'Ended', label: 'Ended' },
];

const unlistedStatuses: InventoryStatus[] = [
  'Purchased',
  'NeedsCleaning',
  'NeedsTesting',
  'NeedsPhotos',
  'ReadyToList',
];

export default function ListingQueueScreen() {
  const { colors: c } = useAppTheme();
  const [listings, setListings] = useState<Listing[]>([]);
  const [inventory, setInventory] = useState<InventoryItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [filter, setFilter] = useState<ListingFilter>('All');

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      const [listingRows, inventoryRows] = await Promise.all([api.listings(), api.inventory()]);
      setListings(listingRows);
      setInventory(inventoryRows);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Listings unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  const readyItems = useMemo(() => {
    const listedItemIds = new Set(listings.map((listing) => listing.inventoryItemId));
    return inventory.filter((item) => unlistedStatuses.includes(item.status) && !listedItemIds.has(item.id));
  }, [inventory, listings]);

  const filteredListings = useMemo(() => {
    if (filter === 'All' || filter === 'Ready') return listings;
    return listings.filter((listing) => listing.status === filter);
  }, [listings, filter]);

  const showReadyItems = filter === 'All' || filter === 'Ready';
  const grouped = useMemo(() => {
    const groups: Record<string, Listing[]> = {};
    for (const listing of filteredListings) {
      (groups[listing.status] ??= []).push(listing);
    }
    return groups;
  }, [filteredListings]);

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
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to create listing draft.');
    }
  }

  async function markListed(listing: Listing) {
    try {
      await api.markListingListed(listing.id);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to mark listing active.');
    }
  }

  async function endListing(listing: Listing) {
    try {
      await api.updateListing(listing.id, {
        platform: listing.platform,
        listingUrl: listing.listingUrl,
        listPrice: listing.listPrice,
        status: 'Ended',
        notes: listing.notes ?? null,
      });
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to end listing.');
    }
  }

  async function deleteListing(id: string) {
    try {
      await api.deleteListing(id);
      setListings((current) => current.filter((listing) => listing.id !== id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to delete listing.');
    }
  }

  function confirmDeleteListing(listing: Listing) {
    Alert.alert(
      'Delete listing?',
      listing.inventoryItemTitle || listing.platform,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Delete', style: 'destructive', onPress: () => void deleteListing(listing.id) },
      ],
    );
  }

  const statusOrder: ListingStatus[] = ['Draft', 'Active', 'Sold', 'Ended', 'Cancelled'];
  const hasContent = (showReadyItems && readyItems.length > 0) || filteredListings.length > 0;

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      <View style={styles.header}>
        <Text variant="title">Listing Queue</Text>
        <Button label="Inventory" icon="cube-outline" size="sm" variant="secondary" onPress={() => router.push('/(tabs)/inventory')} />
      </View>

      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      <FilterChips options={filterOptions} selected={filter} onSelect={setFilter} />

      {isLoading ? (
        <SkeletonList count={3} />
      ) : !hasContent ? (
        <EmptyState
          icon="pricetags-outline"
          title="No listing work"
          message="Items with unlisted inventory statuses and listing drafts appear here."
          actionLabel="View Inventory"
          onAction={() => router.push('/(tabs)/inventory')}
        />
      ) : (
        <>
          {showReadyItems && readyItems.length > 0 ? (
            <View>
              <SectionHeader
                title={`Ready to list (${readyItems.length})`}
                trailing={<StatusPill label="Inventory" tone="primary" size="sm" />}
              />
              {readyItems.map((item) => (
                <Card key={item.id} style={styles.listingCard}>
                  <View style={styles.listingHeader}>
                    <View style={styles.listingInfo}>
                      <Text variant="bodyBold" numberOfLines={1}>{item.title}</Text>
                      <Text variant="caption" color={c.textMuted}>
                        Cost {formatMoney(item.purchasePrice ?? item.sellerAskingPrice)} · Value {formatMoney(item.estimatedSalePrice)}
                      </Text>
                    </View>
                    <StatusPill label={item.status} tone={listingReadinessTone(item.status)} size="sm" />
                  </View>
                  <View style={styles.actions}>
                    <QueueAction icon="create-outline" label="Open" color={c.primary} onPress={() => router.push(`/inventory/${item.id}`)} />
                    <QueueAction icon="pricetag-outline" label="Draft" color={c.info} onPress={() => void createDraftListing(item)} />
                  </View>
                </Card>
              ))}
            </View>
          ) : null}

          {statusOrder
            .filter((status) => grouped[status]?.length)
            .map((status) => (
              <View key={status}>
                <SectionHeader
                  title={`${status} listings (${grouped[status]!.length})`}
                  trailing={<StatusPill label={status} tone={listingStatusTone(status)} size="sm" />}
                />
                {grouped[status]!.map((listing) => (
                  <Card key={listing.id} style={styles.listingCard}>
                    <View style={styles.listingHeader}>
                      <View style={styles.listingInfo}>
                        <Text variant="bodyBold" numberOfLines={1}>
                          {listing.inventoryItemTitle || listing.platform}
                        </Text>
                        <Text variant="caption" color={c.textMuted}>{listing.platform}</Text>
                      </View>
                      <View style={styles.priceBlock}>
                        <Text variant="metricSmall">{formatMoney(listing.listPrice)}</Text>
                        <StatusPill label={listing.status} tone={listingStatusTone(listing.status)} size="sm" />
                      </View>
                    </View>
                    {listing.listingUrl ? (
                      <Text variant="caption" numberOfLines={1}>{listing.listingUrl}</Text>
                    ) : (
                      <Text variant="caption" color={c.textMuted}>No listing URL set</Text>
                    )}
                    <View style={styles.actions}>
                      <QueueAction icon="cube-outline" label="Item" color={c.primary} onPress={() => router.push(`/inventory/${listing.inventoryItemId}`)} />
                      {listing.status === 'Draft' ? (
                        <QueueAction icon="cloud-upload-outline" label="Active" color={c.success} onPress={() => void markListed(listing)} />
                      ) : null}
                      {listing.status === 'Active' ? (
                        <QueueAction icon="stop-circle-outline" label="End" color={c.warning} onPress={() => void endListing(listing)} />
                      ) : null}
                      {listing.status === 'Active' || listing.status === 'Draft' ? (
                        <QueueAction
                          icon="cash-outline"
                          label="Sold"
                          color={c.success}
                          onPress={() => router.push({
                            pathname: '/record-sale',
                            params: {
                              inventoryItemId: listing.inventoryItemId,
                              listingId: listing.id,
                              title: listing.inventoryItemTitle,
                              platform: listing.platform,
                              listPrice: String(listing.listPrice),
                            },
                          })}
                        />
                      ) : null}
                      <QueueAction icon="trash-outline" label="Delete" color={c.danger} onPress={() => confirmDeleteListing(listing)} />
                    </View>
                  </Card>
                ))}
              </View>
            ))}
        </>
      )}
    </Screen>
  );
}

function QueueAction({ icon, label, color, onPress }: {
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
      style={styles.queueAction}
    >
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
  listingCard: {
    marginTop: spacing.sm,
  },
  listingHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  listingInfo: {
    flex: 1,
    gap: spacing.xxs,
  },
  priceBlock: {
    alignItems: 'flex-end',
    gap: spacing.xs,
  },
  actions: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    paddingTop: spacing.sm,
  },
  queueAction: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xxs,
    paddingVertical: spacing.xs,
    paddingHorizontal: spacing.sm,
  },
});
