import { Ionicons } from '@expo/vector-icons';
import { Linking, Pressable, StyleSheet, View } from 'react-native';
import { Text } from '../../components/Text';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { useHaptics } from '../../hooks/useHaptics';
import { COMP_TIER_META, PRIMARY_COMP, compSourcesByTier, type CompSource } from './compSources';

/**
 * "Find Comps" — deep-link buttons to real resale sources for the given product.
 * eBay Sold is the hero (realized resale value); other secondhand + retail follow, tiered.
 * This is the reseller's live pricing workflow when no marketplace API is configured.
 */
export function CompSourcesPanel({ query, compact }: { query: string; compact?: boolean }) {
  const { colors: c } = useAppTheme();
  const haptics = useHaptics();
  const q = query.trim();
  if (!q) return null;

  const open = (source: CompSource) => {
    haptics.light();
    void Linking.openURL(source.build(q));
  };

  const tiers = compSourcesByTier();

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <Ionicons name="search-circle-outline" size={16} color={c.textSecondary} />
        <Text variant="bodyBold" color={c.textSecondary}>Find Comps</Text>
        <Text variant="label" color={c.textMuted} numberOfLines={1} style={{ flex: 1 }}>· {q}</Text>
      </View>

      {/* Hero: eBay Sold — the realized-value gold standard */}
      <Pressable
        onPress={() => open(PRIMARY_COMP)}
        style={({ pressed }) => [
          styles.primaryBtn,
          { backgroundColor: c.primary },
          pressed && { backgroundColor: c.primaryPressed, transform: [{ scale: 0.99 }] },
        ]}
        accessibilityRole="link"
        accessibilityLabel={`See ${q} sold listings on eBay, newest first`}
      >
        <Ionicons name="pricetag" size={20} color={c.textInverse} />
        <View style={{ flex: 1 }}>
          <Text variant="bodyBold" color={c.textInverse}>See eBay Sold Comps</Text>
          <Text variant="label" color="rgba(255,255,255,0.7)" style={styles.noCaps}>
            Newest realized sale prices — what it actually sold for
          </Text>
        </View>
        <Ionicons name="open-outline" size={18} color="rgba(255,255,255,0.8)" />
      </Pressable>

      {compact ? null : (
        tiers.map(({ tier, sources }) => {
          const rest = sources.filter((s) => s.id !== PRIMARY_COMP.id);
          if (rest.length === 0) return null;
          return (
            <View key={tier} style={styles.tierGroup}>
              <Text variant="label" color={c.textMuted}>
                {COMP_TIER_META[tier].label} · {COMP_TIER_META[tier].hint}
              </Text>
              <View style={styles.chipRow}>
                {rest.map((source) => (
                  <Pressable
                    key={source.id}
                    onPress={() => open(source)}
                    style={({ pressed }) => [
                      styles.chip,
                      { backgroundColor: c.surfaceAlt, borderColor: c.borderLight },
                      pressed && { backgroundColor: c.borderLight },
                    ]}
                    accessibilityRole="link"
                    accessibilityLabel={`Search ${q} on ${source.label}`}
                  >
                    <Ionicons name={source.icon} size={14} color={c.textSecondary} />
                    <Text variant="caption" color={c.textSecondary}>{source.label}</Text>
                  </Pressable>
                ))}
              </View>
            </View>
          );
        })
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.sm },
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  noCaps: { textTransform: 'none', letterSpacing: 0 },
  primaryBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.lg,
  },
  tierGroup: { gap: spacing.xs },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.full,
    borderWidth: 1,
  },
});
