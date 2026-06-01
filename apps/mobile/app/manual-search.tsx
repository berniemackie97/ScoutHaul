import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { MarketplaceListing, ProductIdentity } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

export default function ManualSearchScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const params = useLocalSearchParams<{
    query?: string;
    barcode?: string;
    productName?: string;
    avgPrice?: string;
    sourcingTripId?: string;
    sourcingTripStopId?: string;
  }>();

  const [query, setQuery] = useState(params.query ?? params.barcode ?? '');
  const [barcode] = useState(params.barcode ?? '');
  const [products, setProducts] = useState<ProductIdentity[]>([]);
  const [marketResults, setMarketResults] = useState<MarketplaceListing[]>([]);
  const [marketAvg, setMarketAvg] = useState<number | null>(
    params.avgPrice ? Number(params.avgPrice) : null,
  );
  const [marketTotal, setMarketTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [isSearching, setIsSearching] = useState(false);
  const [hasSearched, setHasSearched] = useState(false);

  useEffect(() => {
    if (params.query || params.barcode) {
      void search();
    }
  }, []);

  async function search() {
    const q = query.trim();
    if (!q) {
      setError('Enter search terms or a barcode.');
      return;
    }
    try {
      setIsSearching(true);
      setError(null);

      // Search internal DB and marketplace providers in parallel
      const [dbResults, ebayResults] = await Promise.allSettled([
        api.searchProducts(q),
        api.lookupSearch(q),
      ]);

      if (dbResults.status === 'fulfilled') {
        setProducts(dbResults.value);
      }

      if (ebayResults.status === 'fulfilled') {
        setMarketResults(ebayResults.value.listings);
        setMarketAvg(ebayResults.value.averageSoldPrice ?? null);
        setMarketTotal(ebayResults.value.totalResults);
      }

      setHasSearched(true);
    } catch (e) {
      setProducts([]);
      setMarketResults([]);
      setError(e instanceof Error ? e.message : 'Search unavailable.');
    } finally {
      setIsSearching(false);
    }
  }

  async function deleteProductIdentity(product: ProductIdentity) {
    try {
      await api.deleteProductIdentity(product.id);
      setProducts((current) => current.filter((item) => item.id !== product.id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to delete product identity.');
    }
  }

  function confirmDeleteProductIdentity(product: ProductIdentity) {
    Alert.alert('Delete product identity?', product.title, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Delete', style: 'destructive', onPress: () => void deleteProductIdentity(product) },
    ]);
  }

  const noResults = hasSearched && products.length === 0 && marketResults.length === 0;

  return (
    <Screen>
      <Card>
        <TextField
          label="Search products & marketplaces"
          value={query}
          onChangeText={setQuery}
          placeholder="Brand, model, SKU, barcode..."
          hint="Searches your database + marketplace providers"
        />
        <Button
          label={isSearching ? 'Searching...' : 'Search'}
          icon="search-outline"
          fullWidth
          disabled={isSearching}
          onPress={() => void search()}
        />
      </Card>

      <View style={styles.quickActions}>
        <Button
          label="Add to Inventory"
          icon="add-circle-outline"
          variant="secondary"
          onPress={() =>
            router.push({
              pathname: '/inventory/edit',
              params: {
                title: params.productName ?? query,
                barcode,
                ...(params.sourcingTripId ? { sourcingTripId: params.sourcingTripId } : {}),
                ...(params.sourcingTripStopId ? { sourcingTripStopId: params.sourcingTripStopId } : {}),
                ...(marketAvg != null ? { estimatedSalePrice: String(marketAvg) } : {}),
              },
            })
          }
        />
        <Button
          label="Deal Check"
          icon="calculator-outline"
          variant="secondary"
          onPress={() =>
            router.push({
              pathname: '/deal-analyzer',
              params: {
                query: params.productName ?? query,
                barcode,
                ...(marketAvg != null ? { expectedSalePrice: String(marketAvg) } : {}),
              },
            })
          }
        />
      </View>

      {/* Marketplace deep links — opens external search on eBay, Google, FB, Amazon, etc. */}
      <Button
        label="Search Marketplaces"
        icon="open-outline"
        variant="secondary"
        fullWidth
        onPress={() =>
          router.push({
            pathname: '/marketplace-links',
            params: { query: params.productName ?? query },
          })
        }
      />

      {error ? <ErrorBanner message={error} /> : null}

      {isSearching ? (
        <LoadingState compact message="Searching..." />
      ) : noResults ? (
        <EmptyState
          icon="search-outline"
          title="No results"
          message="No matching products found. Try different terms or add manually."
          actionLabel="Add to Inventory"
          onAction={() =>
            router.push({
              pathname: '/inventory/edit',
              params: {
                title: params.productName ?? query,
                barcode,
                ...(params.sourcingTripId ? { sourcingTripId: params.sourcingTripId } : {}),
                ...(params.sourcingTripStopId ? { sourcingTripStopId: params.sourcingTripStopId } : {}),
              },
            })
          }
        />
      ) : (
        <>
          {/* Marketplace pricing results */}
          {marketResults.length > 0 ? (
            <>
              <SectionHeader title="Marketplace Results" />
              {marketAvg != null ? (
                <View style={[styles.avgBar, { backgroundColor: c.primaryLight }]}>
                  <Text variant="caption" color={c.textSecondary}>Avg sold price</Text>
                  <Text variant="metricSmall" color={c.primary}>{formatMoney(marketAvg)}</Text>
                  <Text variant="label" color={c.textMuted}>{marketTotal} results</Text>
                </View>
              ) : null}
              <ScrollView
                horizontal
                showsHorizontalScrollIndicator={false}
                contentContainerStyle={styles.marketScroll}
              >
                {marketResults.slice(0, 12).map((listing, i) => (
                  <Pressable
                    key={i}
                    onPress={() =>
                      router.push({
                        pathname: '/comp-capture',
                        params: {
                          title: listing.title,
                          price: String(listing.price),
                          condition: listing.condition,
                          source: listing.source,
                          sourceUrl: listing.itemUrl ?? '',
                        },
                      })
                    }
                    style={[
                      styles.marketCard,
                      { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm },
                    ]}
                  >
                    {listing.imageUrl ? (
                      <Image source={{ uri: listing.imageUrl }} style={styles.marketImage} contentFit="cover" />
                    ) : (
                      <View style={[styles.marketImage, styles.noImage, { backgroundColor: c.borderLight }]}>
                        <Ionicons name="image-outline" size={24} color={c.textMuted} />
                      </View>
                    )}
                    <View style={styles.marketInfo}>
                      <Text variant="caption" numberOfLines={2} style={styles.marketTitle}>
                        {listing.title}
                      </Text>
                      <View style={styles.marketPriceRow}>
                        <Text variant="bodyBold" color={c.primary}>{formatMoney(listing.price)}</Text>
                        {listing.isSold ? (
                          <StatusPill label="Sold" tone="success" size="sm" />
                        ) : null}
                      </View>
                      <Text variant="label" color={c.textMuted}>{listing.condition}</Text>
                    </View>
                  </Pressable>
                ))}
              </ScrollView>
            </>
          ) : null}

          {/* Internal DB results */}
          {products.length > 0 ? (
            <>
              <SectionHeader title="Your Products" />
              {products.map((product) => (
                <Pressable
                  key={product.id}
                  onPress={() =>
                    router.push({
                      pathname: '/inventory/edit',
                      params: {
                        title: product.title,
                        productIdentityId: product.id,
                        barcode: product.upc ?? product.ean ?? barcode,
                        ...(params.sourcingTripId ? { sourcingTripId: params.sourcingTripId } : {}),
                        ...(params.sourcingTripStopId ? { sourcingTripStopId: params.sourcingTripStopId } : {}),
                      },
                    })
                  }
                  accessibilityRole="button"
                  style={({ pressed }) => [
                    styles.productCard,
                    { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm },
                    pressed && { backgroundColor: c.surfaceAlt },
                  ]}
                >
                  <Text variant="bodyBold" numberOfLines={2}>{product.title}</Text>
                  <Text variant="caption" color={c.textSecondary}>
                    {[product.brand, product.model, product.category].filter(Boolean).join(' · ') ||
                      'No additional details'}
                  </Text>
                  <View style={styles.productActions}>
                    <StatusPill label="Add to Inventory" tone="primary" size="sm" />
                    <Pressable
                      accessibilityRole="button"
                      accessibilityLabel={`Delete ${product.title}`}
                      hitSlop={8}
                      onPress={() => confirmDeleteProductIdentity(product)}
                    >
                      <Ionicons name="trash-outline" size={18} color={c.danger} />
                    </Pressable>
                  </View>
                </Pressable>
              ))}
            </>
          ) : null}
        </>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  quickActions: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  avgBar: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    borderRadius: radius.lg,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
  },
  marketScroll: {
    gap: spacing.sm,
    paddingBottom: spacing.sm,
  },
  marketCard: {
    width: 160,
    borderRadius: radius.lg,
    borderWidth: 1,
    overflow: 'hidden',
  },
  marketImage: { width: '100%', height: 110 },
  noImage: { alignItems: 'center', justifyContent: 'center' },
  marketInfo: { padding: spacing.sm, gap: spacing.xxs },
  marketTitle: { lineHeight: 16 },
  marketPriceRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  productCard: {
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
    borderWidth: 1,
  },
  productActions: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
});
