import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { api } from '../src/api';
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
import { CompSourcesPanel } from '../src/features/scanner/CompSourcesPanel';
import { identifyFromDetails, type IdentifyResult } from '../src/features/scanner/photoIdentify';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { MarketplaceListing, ProductIdentity } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

function visionErrorMessage(kind: NonNullable<IdentifyResult['errorKind']>): string {
  switch (kind) {
    case 'quota':
      return 'The daily AI request limit has been reached. Plain marketplace search still works.';
    case 'rate_limited':
      return 'Too many AI requests right now. Try the AI appraisal again in a moment.';
    case 'unavailable':
      return 'The AI appraisal service is busy. Plain marketplace search still works.';
    case 'timeout':
      return 'AI appraisal timed out. Try again or use plain marketplace search.';
    case 'offline':
      return 'Network unavailable. Connect to the internet to run AI appraisal.';
    case 'auth':
      return 'Sign in is required to run AI appraisal.';
    default:
      return 'AI appraisal unavailable. Plain marketplace search still works.';
  }
}

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
    params.avgPrice ? Number(params.avgPrice) : null
  );
  const [marketTotal, setMarketTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [isSearching, setIsSearching] = useState(false);
  const [isAiAppraising, setIsAiAppraising] = useState(false);
  const [aiResult, setAiResult] = useState<IdentifyResult | null>(null);
  const [hasSearched, setHasSearched] = useState(false);

  const aiLowPrice = aiResult?.estimatedResaleLow ?? null;
  const aiHighPrice = aiResult?.estimatedResaleHigh ?? null;
  const aiMidPrice =
    aiLowPrice != null || aiHighPrice != null
      ? Math.round(((aiLowPrice ?? aiHighPrice ?? 0) + (aiHighPrice ?? aiLowPrice ?? 0)) / 2)
      : null;
  const resolvedTitle = aiResult?.title ?? params.productName ?? query;
  const resolvedPrice = marketAvg ?? aiMidPrice;

  useEffect(() => {
    if (params.query || params.barcode) {
      void search();
    }
  }, []);

  async function search(searchTerm = query) {
    const q = searchTerm.trim();
    if (!q) {
      setError('Enter search terms or a barcode.');
      return;
    }
    try {
      setIsSearching(true);
      setError(null);
      setAiResult(null);
      setProducts([]);
      setMarketResults([]);
      setMarketAvg(null);
      setMarketTotal(0);

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

  async function appraiseTypedDetails() {
    const q = query.trim();
    if (!q) {
      setError('Enter item details first. Brand, model, condition, size, and defects help most.');
      return;
    }

    try {
      setIsAiAppraising(true);
      setError(null);
      setProducts([]);
      setMarketResults([]);
      setMarketAvg(null);
      setMarketTotal(0);

      const identified = await identifyFromDetails(q);
      setAiResult(identified);

      if (identified.errorKind) {
        setError(visionErrorMessage(identified.errorKind));
        setHasSearched(true);
        return;
      }

      const refinedQuery = identified.searchQuery || identified.title || q;
      const [dbResults, marketLookup] = await Promise.allSettled([
        api.searchProducts(refinedQuery),
        api.lookupSearch(refinedQuery),
      ]);

      if (dbResults.status === 'fulfilled') setProducts(dbResults.value);
      if (marketLookup.status === 'fulfilled') {
        setMarketResults(marketLookup.value.listings);
        setMarketAvg(marketLookup.value.averageSoldPrice ?? null);
        setMarketTotal(marketLookup.value.totalResults);
      }
      setHasSearched(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'AI text appraisal unavailable.');
    } finally {
      setIsAiAppraising(false);
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

  const noResults = hasSearched && products.length === 0 && marketResults.length === 0 && !aiResult;

  return (
    <Screen>
      <Card>
        <TextField
          label="Item details"
          value={query}
          onChangeText={setQuery}
          placeholder="Brand, model, SKU, condition, size, edition..."
          hint="Use plain search for grounded comps, or AI refine/appraise when details are messy or incomplete."
        />
        <Button
          label={isSearching ? 'Searching...' : 'Search comps from typed details'}
          icon="search-outline"
          fullWidth
          disabled={isSearching || isAiAppraising}
          onPress={() => void search()}
        />
        <Button
          label={isAiAppraising ? 'AI appraising...' : 'AI refine + appraise details'}
          icon="sparkles-outline"
          variant="secondary"
          fullWidth
          disabled={isSearching || isAiAppraising}
          onPress={() => void appraiseTypedDetails()}
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
                title: resolvedTitle,
                barcode,
                ...(params.sourcingTripId ? { sourcingTripId: params.sourcingTripId } : {}),
                ...(params.sourcingTripStopId
                  ? { sourcingTripStopId: params.sourcingTripStopId }
                  : {}),
                ...(aiResult?.brand ? { brand: aiResult.brand } : {}),
                ...(aiResult?.model ? { model: aiResult.model } : {}),
                ...(aiResult?.category ? { category: aiResult.category } : {}),
                ...(aiResult?.condition ? { condition: aiResult.condition } : {}),
                ...(resolvedPrice != null ? { estimatedSalePrice: String(resolvedPrice) } : {}),
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
                query: resolvedTitle,
                barcode,
                ...(resolvedPrice != null ? { expectedSalePrice: String(resolvedPrice) } : {}),
              },
            })
          }
        />
      </View>

      {aiResult && !aiResult.errorKind ? (
        <Card>
          <View style={styles.aiHeader}>
            <View style={styles.aiTitleBlock}>
              <Text variant="label" color={c.accent}>
                AI typed appraisal
              </Text>
              <Text variant="bodyBold" numberOfLines={2}>
                {aiResult.title ?? query}
              </Text>
              <Text variant="caption" color={c.textSecondary}>
                {[aiResult.brand, aiResult.model, aiResult.category, aiResult.condition]
                  .filter(Boolean)
                  .join(' · ') || 'Details refined from typed input'}
              </Text>
            </View>
            {aiResult.confidence ? (
              <StatusPill
                label={`${aiResult.confidence} ID`}
                tone={
                  aiResult.confidence === 'high'
                    ? 'success'
                    : aiResult.confidence === 'medium'
                      ? 'warning'
                      : 'neutral'
                }
                size="sm"
              />
            ) : null}
          </View>

          {aiResult.estimatedResaleLow != null || aiResult.estimatedResaleHigh != null ? (
            <View style={[styles.aiValueBox, { backgroundColor: c.accentLight }]}>
              <Text variant="caption" color={c.textSecondary}>
                Conservative used resale estimate
              </Text>
              <Text variant="metricSmall" color={c.accent}>
                {formatMoney(aiResult.estimatedResaleLow)} –{' '}
                {formatMoney(aiResult.estimatedResaleHigh)}
              </Text>
              <Text variant="label" color={c.textMuted}>
                {aiResult.valuationConfidence
                  ? `${aiResult.valuationConfidence} valuation confidence`
                  : 'Verify against sold comps before buying'}
              </Text>
            </View>
          ) : null}

          {aiResult.keyValueFactors.length > 0 ? (
            <View style={styles.aiChipWrap}>
              {aiResult.keyValueFactors.slice(0, 5).map((factor) => (
                <View key={factor} style={[styles.aiChip, { backgroundColor: c.surfaceAlt }]}>
                  <Text variant="label" color={c.textSecondary}>
                    {factor}
                  </Text>
                </View>
              ))}
            </View>
          ) : null}

          {aiResult.missingInformation.length > 0 ? (
            <View style={styles.aiList}>
              <Text variant="label" color={c.warning}>
                To tighten the value, confirm
              </Text>
              {aiResult.missingInformation.slice(0, 3).map((item) => (
                <Text key={item} variant="caption" color={c.textSecondary}>
                  • {item}
                </Text>
              ))}
            </View>
          ) : null}
        </Card>
      ) : null}

      {/* Real resale comps — tiered deep links (eBay Sold first), inline for speed */}
      {(aiResult?.searchQuery || resolvedTitle).trim() ? (
        <Card>
          <CompSourcesPanel query={aiResult?.searchQuery || resolvedTitle} />
        </Card>
      ) : null}

      {/* If we have a barcode, offer direct barcode search too */}
      {barcode && barcode !== query ? (
        <Button
          label={`Search by barcode: ${barcode}`}
          icon="barcode-outline"
          variant="ghost"
          fullWidth
          onPress={() => {
            setQuery(barcode);
            void search(barcode);
          }}
        />
      ) : null}

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
                ...(params.sourcingTripStopId
                  ? { sourcingTripStopId: params.sourcingTripStopId }
                  : {}),
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
                  <Text variant="caption" color={c.textSecondary}>
                    Avg sold price
                  </Text>
                  <Text variant="metricSmall" color={c.primary}>
                    {formatMoney(marketAvg)}
                  </Text>
                  <Text variant="label" color={c.textMuted}>
                    {marketTotal} results
                  </Text>
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
                      <Image
                        source={{ uri: listing.imageUrl }}
                        style={styles.marketImage}
                        contentFit="cover"
                      />
                    ) : (
                      <View
                        style={[
                          styles.marketImage,
                          styles.noImage,
                          { backgroundColor: c.borderLight },
                        ]}
                      >
                        <Ionicons name="image-outline" size={24} color={c.textMuted} />
                      </View>
                    )}
                    <View style={styles.marketInfo}>
                      <Text variant="caption" numberOfLines={2} style={styles.marketTitle}>
                        {listing.title}
                      </Text>
                      <View style={styles.marketPriceRow}>
                        <Text variant="bodyBold" color={c.primary}>
                          {formatMoney(listing.price)}
                        </Text>
                        {listing.isSold ? (
                          <StatusPill label="Sold" tone="success" size="sm" />
                        ) : null}
                      </View>
                      <Text variant="label" color={c.textMuted}>
                        {listing.condition}
                      </Text>
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
                        ...(params.sourcingTripStopId
                          ? { sourcingTripStopId: params.sourcingTripStopId }
                          : {}),
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
                  <Text variant="bodyBold" numberOfLines={2}>
                    {product.title}
                  </Text>
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
  aiHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  aiTitleBlock: {
    flex: 1,
    gap: spacing.xxs,
  },
  aiValueBox: {
    borderRadius: radius.lg,
    padding: spacing.md,
    gap: spacing.xxs,
  },
  aiChipWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
  },
  aiChip: {
    borderRadius: radius.full,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
  aiList: {
    gap: spacing.xxs,
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
