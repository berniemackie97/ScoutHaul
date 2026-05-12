import { Ionicons } from '@expo/vector-icons';
import { useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { FilterChips } from '../src/components/FilterChips';
import { SkeletonList } from '../src/components/Skeleton';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { spacing, type ToneKey } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import type { Listing, ListingStatus } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

type ListingFilter = 'All' | ListingStatus;

const filterOptions: { value: ListingFilter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Active', label: 'Active' },
  { value: 'Sold', label: 'Sold' },
  { value: 'Ended', label: 'Ended' },
];

function listingTone(status: ListingStatus): ToneKey {
  if (status === 'Active') return 'success';
  if (status === 'Sold') return 'info';
  if (status === 'Draft') return 'warning';
  return 'neutral';
}

export default function ListingQueueScreen() {
  const c = useColors();
  const [listings, setListings] = useState<Listing[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [filter, setFilter] = useState<ListingFilter>('All');

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      setListings(await api.listings());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Listings unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  const filtered = useMemo(() => {
    if (filter === 'All') return listings;
    return listings.filter((l) => l.status === filter);
  }, [listings, filter]);

  const grouped = useMemo(() => {
    const groups: Record<string, Listing[]> = {};
    for (const l of filtered) {
      (groups[l.status] ??= []).push(l);
    }
    return groups;
  }, [filtered]);

  const statusOrder: ListingStatus[] = ['Draft', 'Active', 'Sold', 'Ended', 'Cancelled'];

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      <FilterChips options={filterOptions} selected={filter} onSelect={setFilter} />

      {isLoading ? (
        <SkeletonList count={3} />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon="pricetags-outline"
          title="No listings yet"
          message="Items appear here when they become listing records. Scout and process items through inventory first."
        />
      ) : (
        statusOrder
          .filter((s) => grouped[s]?.length)
          .map((status) => (
            <View key={status}>
              <SectionHeader
                title={`${status} (${grouped[status]!.length})`}
                trailing={
                  <StatusPill label={status} tone={listingTone(status)} size="sm" />
                }
              />
              {grouped[status]!.map((listing) => (
                <Card key={listing.id} style={styles.listingCard}>
                  <View style={styles.listingHeader}>
                    <View style={styles.listingInfo}>
                      <Text variant="bodyBold">{listing.platform}</Text>
                      <Text variant="metricSmall">{formatMoney(listing.listPrice)}</Text>
                    </View>
                    <StatusPill label={listing.status} tone={listingTone(listing.status)} size="sm" />
                  </View>
                  {listing.listingUrl ? (
                    <Text variant="caption" numberOfLines={1}>{listing.listingUrl}</Text>
                  ) : (
                    <Text variant="caption" color={c.textMuted}>No listing URL set</Text>
                  )}
                  {listing.listedAt ? (
                    <Text variant="caption">Listed {new Date(listing.listedAt).toLocaleDateString()}</Text>
                  ) : null}
                </Card>
              ))}
            </View>
          ))
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  listingCard: {
    marginTop: spacing.sm,
  },
  listingHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  listingInfo: {
    gap: spacing.xxs,
  },
});
