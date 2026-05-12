import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useState } from 'react';
import { Linking, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { ProductIdentity } from '../src/domain/types';

export default function ManualSearchScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const [query, setQuery] = useState('');
  const [products, setProducts] = useState<ProductIdentity[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isSearching, setIsSearching] = useState(false);
  const [hasSearched, setHasSearched] = useState(false);

  async function search() {
    if (!query.trim()) {
      setError('Enter search terms or a barcode.');
      return;
    }
    try {
      setIsSearching(true);
      setError(null);
      setProducts(await api.searchProducts(query));
      setHasSearched(true);
    } catch (e) {
      setProducts([]);
      setError(e instanceof Error ? e.message : 'Search unavailable.');
    } finally {
      setIsSearching(false);
    }
  }

  return (
    <Screen>
      <Card>
        <TextField
          label="Search products"
          value={query}
          onChangeText={setQuery}
          placeholder="Brand, model, SKU, barcode..."
          hint="Search your product database"
        />
        <View style={styles.actions}>
          <Button
            label={isSearching ? 'Searching...' : 'Search'}
            icon="search-outline"
            fullWidth
            disabled={isSearching}
            onPress={() => void search()}
          />
        </View>
      </Card>

      <View style={styles.secondaryActions}>
        <Button
          label="Start Draft"
          icon="document-outline"
          variant="secondary"
          onPress={() => router.push({ pathname: '/(tabs)/scout', params: { query } })}
        />
        <Button
          label="Marketplace Links"
          icon="open-outline"
          variant="secondary"
          onPress={() => router.push({ pathname: '/marketplace-links', params: { query } })}
        />
      </View>

      {error ? <ErrorBanner message={error} /> : null}

      {isSearching ? (
        <LoadingState compact message="Searching..." />
      ) : hasSearched && products.length === 0 ? (
        <EmptyState
          icon="search-outline"
          title="No results"
          message="No matching products found. Try different search terms or create a draft directly."
          actionLabel="Create Draft"
          onAction={() => router.push({ pathname: '/(tabs)/scout', params: { query } })}
        />
      ) : (
        products.map((product) => (
          <Pressable
            key={product.id}
            onPress={() => router.push({ pathname: '/(tabs)/scout', params: { query: product.searchQuery ?? product.title } })}
            accessibilityRole="button"
            style={({ pressed }) => [
              styles.productCard,
              { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm },
              pressed && { backgroundColor: c.surfaceAlt, opacity: 0.9 },
            ]}
          >
            <Text variant="bodyBold" numberOfLines={2}>
              {product.title}
            </Text>
            <Text variant="caption">
              {[product.brand, product.model, product.category].filter(Boolean).join(' · ') || 'No additional details'}
            </Text>
            <View style={styles.productActions}>
              <StatusPill label="Scout" tone="primary" size="sm" />
              <Pressable
                onPress={() => void Linking.openURL(`https://www.google.com/search?q=${encodeURIComponent(product.searchQuery ?? product.title)}`)}
                hitSlop={8}
              >
                <Ionicons name="globe-outline" size={20} color={c.textSecondary} />
              </Pressable>
            </View>
          </Pressable>
        ))
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  actions: {
    gap: spacing.sm,
  },
  secondaryActions: {
    flexDirection: 'row',
    gap: spacing.md,
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
  },
});
