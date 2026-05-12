import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { SearchBar } from '../src/components/SearchBar';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';

type SearchResult = {
  type: 'screen' | 'action';
  title: string;
  subtitle: string;
  icon: keyof typeof Ionicons.glyphMap;
  route: string;
  params?: Record<string, string>;
};

const allItems: SearchResult[] = [
  { type: 'screen', title: 'Dashboard', subtitle: 'Home screen', icon: 'grid-outline', route: '/(tabs)' },
  { type: 'screen', title: 'Scout', subtitle: 'Scan, search, evaluate', icon: 'scan-outline', route: '/(tabs)/scout' },
  { type: 'screen', title: 'Inventory', subtitle: 'Browse items', icon: 'cube-outline', route: '/(tabs)/inventory' },
  { type: 'screen', title: 'Trips', subtitle: 'Sourcing trips', icon: 'map-outline', route: '/(tabs)/trips' },
  { type: 'screen', title: 'Budget', subtitle: 'Spending & profit', icon: 'wallet-outline', route: '/budget' },
  { type: 'screen', title: 'Listing Queue', subtitle: 'Listing workflow', icon: 'pricetags-outline', route: '/listing-queue' },
  { type: 'screen', title: 'Tasks', subtitle: 'To-do list', icon: 'checkbox-outline', route: '/tasks' },
  { type: 'screen', title: 'Settings', subtitle: 'API, sync, theme', icon: 'settings-outline', route: '/settings' },
  { type: 'action', title: 'Scan Barcode', subtitle: 'Open barcode scanner', icon: 'barcode-outline', route: '/scanner' },
  { type: 'action', title: 'Add Item', subtitle: 'Create inventory item', icon: 'add-circle-outline', route: '/inventory/edit' },
  { type: 'action', title: 'Deal Analyzer', subtitle: 'Run deal check', icon: 'calculator-outline', route: '/deal-analyzer' },
  { type: 'action', title: 'Marketplace Search', subtitle: 'Open marketplace links', icon: 'open-outline', route: '/marketplace-links' },
];

export default function SearchScreen() {
  const c = useColors();
  const [query, setQuery] = useState('');

  const results = useMemo(() => {
    if (!query.trim()) return allItems;
    const q = query.toLowerCase();
    return allItems.filter(
      (item) => item.title.toLowerCase().includes(q) || item.subtitle.toLowerCase().includes(q),
    );
  }, [query]);

  const screens = results.filter((r) => r.type === 'screen');
  const actions = results.filter((r) => r.type === 'action');

  return (
    <Screen edges={['top', 'left', 'right']}>
      <SearchBar
        value={query}
        onChangeText={setQuery}
        placeholder="Search screens, actions..."
        autoFocus
      />

      {screens.length > 0 ? (
        <>
          <SectionHeader title="Screens" />
          {screens.map((item) => (
            <Pressable
              key={item.route}
              onPress={() => router.push(item.route as any)}
              accessibilityRole="button"
              style={({ pressed }) => [styles.row, pressed && { opacity: 0.7 }]}
            >
              <Ionicons name={item.icon} size={20} color={c.primary} />
              <View style={styles.rowText}>
                <Text variant="bodyBold">{item.title}</Text>
                <Text variant="caption">{item.subtitle}</Text>
              </View>
              <Ionicons name="chevron-forward" size={16} color={c.textMuted} />
            </Pressable>
          ))}
        </>
      ) : null}

      {actions.length > 0 ? (
        <>
          <SectionHeader title="Quick Actions" />
          {actions.map((item) => (
            <Pressable
              key={item.route}
              onPress={() => router.push(item.route as any)}
              accessibilityRole="button"
              style={({ pressed }) => [styles.row, pressed && { opacity: 0.7 }]}
            >
              <Ionicons name={item.icon} size={20} color={c.accent} />
              <View style={styles.rowText}>
                <Text variant="bodyBold">{item.title}</Text>
                <Text variant="caption">{item.subtitle}</Text>
              </View>
              <StatusPill label="Go" tone="primary" size="sm" />
            </Pressable>
          ))}
        </>
      ) : null}

      {results.length === 0 ? (
        <View style={styles.empty}>
          <Text variant="caption">No results for "{query}"</Text>
        </View>
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.md,
  },
  rowText: {
    flex: 1,
    gap: spacing.xxs,
  },
  empty: {
    alignItems: 'center',
    paddingVertical: spacing.xxxl,
  },
});
