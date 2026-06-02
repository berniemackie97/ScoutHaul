import { Ionicons } from '@expo/vector-icons';
import { PropsWithChildren, useCallback, useState } from 'react';
import { LayoutAnimation, Platform, Pressable, StyleSheet, UIManager, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useAppTheme } from '../context/ThemeContext';

if (Platform.OS === 'android' && UIManager.setLayoutAnimationEnabledExperimental) {
  UIManager.setLayoutAnimationEnabledExperimental(true);
}

type ExpandableSectionProps = PropsWithChildren<{
  title: string;
  icon?: keyof typeof Ionicons.glyphMap;
  defaultExpanded?: boolean;
  badge?: string | number;
  filledCount?: number;
  totalCount?: number;
}>;

export function ExpandableSection({
  title,
  icon,
  defaultExpanded = false,
  badge,
  filledCount,
  totalCount,
  children,
}: ExpandableSectionProps) {
  const { colors: c, shadows: s } = useAppTheme();
  const [expanded, setExpanded] = useState(defaultExpanded);

  const toggle = useCallback(() => {
    LayoutAnimation.configureNext(LayoutAnimation.Presets.easeInEaseOut);
    setExpanded((v) => !v);
  }, []);

  const hasFillInfo = filledCount !== undefined && totalCount !== undefined && totalCount > 0;
  const fillRatio = hasFillInfo ? filledCount / totalCount : 0;

  return (
    <View style={[styles.container, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm }]}>
      <Pressable
        onPress={toggle}
        style={styles.header}
        accessibilityRole="button"
        accessibilityState={{ expanded }}
        accessibilityLabel={`${title} section, ${expanded ? 'expanded' : 'collapsed'}`}
      >
        <View style={styles.headerLeft}>
          {icon ? (
            <View style={[styles.iconWrap, { backgroundColor: c.primaryLight }]}>
              <Ionicons name={icon} size={16} color={c.primary} />
            </View>
          ) : null}
          <Text variant="bodyBold">{title}</Text>
        </View>
        <View style={styles.headerRight}>
          {!expanded && hasFillInfo ? (
            <View style={[styles.fillBadge, { backgroundColor: fillRatio > 0 ? c.primaryLight : c.surfaceAlt }]}>
              <Text variant="label" color={fillRatio > 0 ? c.primary : c.textMuted}>
                {filledCount}/{totalCount}
              </Text>
            </View>
          ) : null}
          {!expanded && badge ? (
            <View style={[styles.badge, { backgroundColor: c.primaryLight }]}>
              <Text variant="label" color={c.primary}>{badge}</Text>
            </View>
          ) : null}
          <Ionicons
            name={expanded ? 'chevron-up' : 'chevron-down'}
            size={18}
            color={c.textMuted}
          />
        </View>
      </Pressable>
      {expanded ? (
        <View style={styles.content}>
          {children}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    borderRadius: radius.lg,
    borderWidth: 1,
    overflow: 'hidden',
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: spacing.lg,
    gap: spacing.md,
  },
  headerLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    flex: 1,
  },
  headerRight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  iconWrap: {
    width: 28,
    height: 28,
    borderRadius: radius.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  badge: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
    borderRadius: radius.full,
  },
  fillBadge: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
    borderRadius: radius.full,
  },
  content: {
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.lg,
    gap: spacing.md,
  },
});
