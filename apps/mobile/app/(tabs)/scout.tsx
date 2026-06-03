import { Ionicons } from '@expo/vector-icons';
import { LinearGradient } from 'expo-linear-gradient';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { api } from '../../src/api';
import { Card } from '../../src/components/Card';
import { Screen } from '../../src/components/Screen';
import { SearchBar } from '../../src/components/SearchBar';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import type { InventoryItem } from '../../src/domain/types';
import { useConnectivity } from '../../src/hooks/useConnectivity';
import { formatMoney } from '../../src/utils/money';

type ActionTile = {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  sub: string;
  color: string;
  bg: string;
  onPress: () => void;
};

export default function ScoutScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const connectivity = useConnectivity();
  const [searchQuery, setSearchQuery] = useState('');
  const [recentItems, setRecentItems] = useState<InventoryItem[]>([]);

  useFocusEffect(
    useCallback(() => {
      if (!connectivity.isOnline) return;
      api
        .inventory()
        .then((items) => setRecentItems(items.slice(0, 5)))
        .catch(() => {});
    }, [connectivity.isOnline]),
  );

  function handleSearch() {
    if (!searchQuery.trim()) return;
    router.push({ pathname: '/manual-search', params: { query: searchQuery.trim() } });
    setSearchQuery('');
  }

  const actions: ActionTile[] = [
    {
      icon: 'search-outline',
      label: 'Search',
      sub: 'Find by name',
      color: c.info,
      bg: c.infoLight,
      onPress: () => router.push('/manual-search'),
    },
    {
      icon: 'calculator-outline',
      label: 'Deal Check',
      sub: 'Run analysis',
      color: c.accent,
      bg: c.accentLight,
      onPress: () => router.push('/deal-analyzer'),
    },
    {
      icon: 'analytics-outline',
      label: 'Add Comp',
      sub: 'Log a price',
      color: c.success,
      bg: c.successLight,
      onPress: () => router.push('/comp-capture'),
    },
    {
      icon: 'cube-outline',
      label: 'Add Item',
      sub: 'To inventory',
      color: c.primary,
      bg: c.primaryLight,
      onPress: () => router.push('/inventory/edit'),
    },
  ];

  const insets = useSafeAreaInsets();

  return (
    <Screen edges={['left', 'right']}>
      <LinearGradient
        colors={[c.primary, c.primaryPressed]}
        start={{ x: 0, y: 0 }}
        end={{ x: 1, y: 1 }}
        style={[styles.heroGradient, { paddingTop: insets.top + spacing.md }]}
      >
        <View style={styles.heroDecor1} />
        <View style={styles.header}>
          <Text variant="title" color="#FFFFFF">Scout</Text>
          <StatusPill label={connectivity.label} tone={connectivity.isOnline ? 'success' : 'warning'} />
        </View>
      </LinearGradient>

      {/* Hero scan buttons — two distinct entry points */}
      <View style={styles.heroRow}>
        <Pressable
          onPress={() => router.push({ pathname: '/scanner', params: { mode: 'barcode' } })}
          style={({ pressed }) => [
            styles.scanHero,
            styles.scanHeroLeft,
            { backgroundColor: c.primary, ...s.lg },
            pressed && { backgroundColor: c.primaryPressed, transform: [{ scale: 0.98 }] },
          ]}
          accessibilityRole="button"
          accessibilityLabel="Scan barcode"
        >
          <View style={[styles.scanIconRing, { borderColor: 'rgba(255,255,255,0.25)' }]}>
            <Ionicons name="barcode-outline" size={28} color={c.textInverse} />
          </View>
          <Text variant="bodyBold" color={c.textInverse}>Scan Barcode</Text>
          <Text variant="caption" color="rgba(255,255,255,0.6)">UPC, EAN, ISBN, QR</Text>
        </Pressable>

        <Pressable
          onPress={() => router.push({ pathname: '/scanner', params: { mode: 'photo-identify' } })}
          style={({ pressed }) => [
            styles.scanHero,
            styles.scanHeroRight,
            { backgroundColor: c.accent, ...s.lg },
            pressed && { opacity: 0.9, transform: [{ scale: 0.98 }] },
          ]}
          accessibilityRole="button"
          accessibilityLabel="Photo identify"
        >
          <View style={[styles.scanIconRing, { borderColor: 'rgba(255,255,255,0.25)' }]}>
            <Ionicons name="camera-outline" size={28} color={c.textInverse} />
          </View>
          <Text variant="bodyBold" color={c.textInverse}>Photo ID</Text>
          <Text variant="caption" color="rgba(255,255,255,0.6)">AI visual search</Text>
        </Pressable>
      </View>

      {/* Quick search */}
      <SearchBar
        value={searchQuery}
        onChangeText={setSearchQuery}
        placeholder="Search marketplaces..."
        onSubmit={handleSearch}
      />

      {/* Action grid */}
      <View style={styles.actionGrid}>
        {actions.map((action) => (
          <Pressable
            key={action.label}
            onPress={action.onPress}
            accessibilityRole="button"
            accessibilityLabel={`${action.label}: ${action.sub}`}
            style={({ pressed }) => [
              styles.actionTile,
              { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm },
              pressed && { opacity: 0.8, backgroundColor: c.surfaceAlt },
            ]}
          >
            <View style={[styles.actionIcon, { backgroundColor: action.bg }]}>
              <Ionicons name={action.icon} size={22} color={action.color} />
            </View>
            <Text variant="bodyBold" numberOfLines={1}>{action.label}</Text>
            <Text variant="label" color={c.textMuted} numberOfLines={1}>{action.sub}</Text>
          </Pressable>
        ))}
      </View>

      {/* Recent inventory */}
      {recentItems.length > 0 ? (
        <>
          <View style={styles.sectionRow}>
            <Text variant="bodyBold" color={c.textSecondary}>Recent Items</Text>
            <Pressable onPress={() => router.push('/(tabs)/inventory')} hitSlop={8}>
              <Text variant="caption" color={c.primary}>See All</Text>
            </Pressable>
          </View>
          <Card>
            {recentItems.map((item, i) => (
              <Pressable
                key={item.id}
                onPress={() => router.push(`/inventory/${item.id}`)}
                style={({ pressed }) => [
                  styles.recentItem,
                  i < recentItems.length - 1 && { borderBottomWidth: 1, borderBottomColor: c.borderLight },
                  pressed && { backgroundColor: c.surfaceAlt },
                ]}
                accessibilityRole="button"
                accessibilityLabel={item.title}
              >
                <View style={styles.recentInfo}>
                  <Text variant="bodyBold" numberOfLines={1}>{item.title}</Text>
                  <View style={styles.recentMeta}>
                    {item.barcode ? (
                      <View style={styles.metaChip}>
                        <Ionicons name="barcode-outline" size={12} color={c.textMuted} />
                        <Text variant="label" color={c.textMuted}>{item.barcode}</Text>
                      </View>
                    ) : null}
                    <StatusPill label={item.status} tone={statusTone(item.status)} size="sm" />
                  </View>
                </View>
                <View style={styles.recentPrice}>
                  {item.estimatedSalePrice ? (
                    <Text variant="bodyBold" color={c.primary}>{formatMoney(item.estimatedSalePrice)}</Text>
                  ) : item.purchasePrice ? (
                    <Text variant="caption" color={c.textMuted}>{formatMoney(item.purchasePrice)}</Text>
                  ) : null}
                  <Ionicons name="chevron-forward" size={16} color={c.textMuted} />
                </View>
              </Pressable>
            ))}
          </Card>
        </>
      ) : null}
    </Screen>
  );
}

function statusTone(status: string): 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'primary' {
  switch (status) {
    case 'Listed':
    case 'Sold':
    case 'Shipped':
    case 'Delivered':
      return 'success';
    case 'Researching':
    case 'NeedsCleaning':
    case 'NeedsTesting':
    case 'NeedsPhotos':
      return 'warning';
    case 'ReadyToList':
    case 'Purchased':
      return 'info';
    case 'Returned':
    case 'Disposed':
      return 'danger';
    default:
      return 'neutral';
  }
}

const styles = StyleSheet.create({
  heroGradient: {
    marginHorizontal: -spacing.lg,
    marginTop: -spacing.lg,
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.xl,
    borderBottomLeftRadius: 20,
    borderBottomRightRadius: 20,
    overflow: 'hidden',
  },
  heroDecor1: {
    position: 'absolute',
    width: 180,
    height: 180,
    borderRadius: 90,
    backgroundColor: 'rgba(255,255,255,0.05)',
    top: -50,
    right: -40,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.md,
  },
  heroRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  scanHero: {
    flex: 1,
    alignItems: 'center',
    borderRadius: radius.xl,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  scanHeroLeft: {},
  scanHeroRight: {},
  scanIconRing: {
    width: 52,
    height: 52,
    borderRadius: 26,
    borderWidth: 2,
    alignItems: 'center',
    justifyContent: 'center',
  },
  actionGrid: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  actionTile: {
    flex: 1,
    alignItems: 'center',
    borderRadius: radius.lg,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.xs,
    gap: spacing.xs,
    borderWidth: 1,
  },
  actionIcon: {
    width: 40,
    height: 40,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  sectionRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingTop: spacing.sm,
  },
  recentItem: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingVertical: spacing.md,
    gap: spacing.sm,
  },
  recentInfo: {
    flex: 1,
    gap: spacing.xs,
  },
  recentMeta: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  metaChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xxs,
  },
  recentPrice: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
});
