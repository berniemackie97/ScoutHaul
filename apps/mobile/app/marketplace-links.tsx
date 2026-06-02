import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { Linking, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { OfflineBanner } from '../src/components/OfflineBanner';
import { Screen } from '../src/components/Screen';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { MarketplaceSearchLink } from '../src/domain/types';
import { useConnectivity } from '../src/hooks/useConnectivity';

const providerIcons: Record<string, keyof typeof Ionicons.glyphMap> = {
  eBay: 'cart-outline',
  Amazon: 'logo-amazon',
  Google: 'logo-google',
  Mercari: 'pricetag-outline',
  Poshmark: 'shirt-outline',
};

export default function MarketplaceLinksScreen() {
  const params = useLocalSearchParams<{ query?: string }>();
  const { colors: c, shadows: s } = useAppTheme();
  const connectivity = useConnectivity();
  const [query, setQuery] = useState(params.query ?? '');
  const [links, setLinks] = useState<MarketplaceSearchLink[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  async function loadLinks(q = query) {
    if (!q.trim()) {
      setError('Enter search terms.');
      return;
    }
    try {
      setIsLoading(true);
      setError(null);
      const result = await api.marketplaceLinks(q);
      setLinks(result.links);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Marketplace links unavailable.');
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => {
    if (params.query) {
      setQuery(params.query);
      void loadLinks(params.query);
    }
  }, [params.query]);

  return (
    <Screen>
      {!connectivity.isOnline ? <OfflineBanner message="Links require internet to generate." /> : null}

      <Card>
        <TextField
          label="Search query"
          value={query}
          onChangeText={setQuery}
          placeholder="Brand model size color"
        />
        <Button
          label={isLoading ? 'Loading...' : 'Generate Links'}
          icon="link-outline"
          fullWidth
          disabled={isLoading}
          onPress={() => void loadLinks()}
        />
      </Card>

      <Card variant="flat">
        <View style={styles.dataBoundary}>
          <Ionicons name="information-circle-outline" size={16} color={c.textMuted} />
          <Text variant="caption">
            These open external marketplace searches. Active listing prices are not sold data.
          </Text>
        </View>
      </Card>

      {error ? <ErrorBanner message={error} /> : null}

      {isLoading ? (
        <LoadingState compact message="Generating links..." />
      ) : (
        links.map((link) => (
          <Pressable
            key={`${link.provider}-${link.label}`}
            onPress={() => void Linking.openURL(link.url)}
            accessibilityRole="button"
            style={({ pressed }) => [
              styles.linkCard,
              { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm },
              pressed && { backgroundColor: c.surfaceAlt, opacity: 0.9 },
            ]}
          >
            <View style={[styles.linkIcon, { backgroundColor: c.primaryLight }]}>
              <Ionicons
                name={providerIcons[link.provider] ?? 'storefront-outline'}
                size={22}
                color={c.primary}
              />
            </View>
            <View style={styles.linkContent}>
              <Text variant="bodyBold">{link.label}</Text>
              <Text variant="caption">{link.provider}</Text>
            </View>
            <Ionicons name="open-outline" size={18} color={c.textMuted} />
          </Pressable>
        ))
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  dataBoundary: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
  linkCard: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
    borderWidth: 1,
  },
  linkIcon: {
    width: 40,
    height: 40,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  linkContent: {
    flex: 1,
    gap: spacing.xxs,
  },
});
