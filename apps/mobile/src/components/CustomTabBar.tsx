import { Ionicons } from '@expo/vector-icons';
import type { BottomTabBarProps } from '@react-navigation/bottom-tabs';
import { Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useAppTheme } from '../context/ThemeContext';
import { useHaptics } from '../hooks/useHaptics';

type BadgeCounts = {
  pendingSync?: number;
  tasksDue?: number;
};

export function CustomTabBar({ state, descriptors, navigation, badgeCounts }: BottomTabBarProps & { badgeCounts?: BadgeCounts }) {
  const { colors: c, shadows: s } = useAppTheme();
  const haptics = useHaptics();
  const insets = useSafeAreaInsets();

  return (
    <View style={[styles.container, { backgroundColor: c.surface, borderTopColor: c.borderLight, paddingBottom: Math.max(insets.bottom, 8), ...s.sm }]}>
      {state.routes.map((route, index) => {
        const { options } = descriptors[route.key];
        const isFocused = state.index === index;
        const label = options.title ?? route.name;
        const tintColor = isFocused ? c.primary : c.textMuted;

        const badge = getBadgeForRoute(route.name, badgeCounts);

        function onPress() {
          const event = navigation.emit({
            type: 'tabPress',
            target: route.key,
            canPreventDefault: true,
          });

          if (!isFocused && !event.defaultPrevented) {
            haptics.light();
            navigation.navigate(route.name, route.params);
          }
        }

        function onLongPress() {
          navigation.emit({
            type: 'tabLongPress',
            target: route.key,
          });
        }

        const iconName = getTabIcon(route.name, isFocused);

        return (
          <Pressable
            key={route.key}
            onPress={onPress}
            onLongPress={onLongPress}
            accessibilityRole="tab"
            accessibilityState={{ selected: isFocused }}
            accessibilityLabel={options.tabBarAccessibilityLabel ?? label}
            style={styles.tab}
          >
            <View style={styles.iconWrap}>
              <Ionicons name={iconName} size={24} color={tintColor} />
              {badge > 0 ? (
                <View style={[styles.badge, { backgroundColor: c.danger }]}>
                  <Text variant="label" color={c.textInverse} style={styles.badgeText}>
                    {badge > 99 ? '99+' : String(badge)}
                  </Text>
                </View>
              ) : null}
            </View>
            <Text
              variant="label"
              color={tintColor}
              style={styles.label}
            >
              {label}
            </Text>
            {isFocused ? (
              <View style={[styles.activeIndicator, { backgroundColor: c.primary }]} />
            ) : null}
          </Pressable>
        );
      })}
    </View>
  );
}

function getTabIcon(routeName: string, focused: boolean): keyof typeof Ionicons.glyphMap {
  switch (routeName) {
    case 'index': return focused ? 'grid' : 'grid-outline';
    case 'scout': return focused ? 'scan' : 'scan-outline';
    case 'inventory': return focused ? 'cube' : 'cube-outline';
    case 'trips': return focused ? 'navigate' : 'navigate-outline';
    case 'more': return focused ? 'menu' : 'menu-outline';
    default: return 'ellipse-outline';
  }
}

function getBadgeForRoute(routeName: string, counts?: BadgeCounts): number {
  if (!counts) return 0;
  if (routeName === 'inventory' && counts.pendingSync) return counts.pendingSync;
  if (routeName === 'more' && counts.tasksDue) return counts.tasksDue;
  return 0;
}

const styles = StyleSheet.create({
  container: {
    flexDirection: 'row',
    borderTopWidth: 1,
    paddingTop: 6,
  },
  tab: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 2,
  },
  iconWrap: {
    position: 'relative',
  },
  badge: {
    position: 'absolute',
    top: -4,
    right: -10,
    minWidth: 18,
    height: 18,
    borderRadius: 9,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 4,
  },
  badgeText: {
    fontSize: 9,
    lineHeight: 12,
    letterSpacing: 0,
  },
  label: {
    fontSize: 10,
    letterSpacing: 0.3,
  },
  activeIndicator: {
    position: 'absolute',
    top: 0,
    width: 20,
    height: 3,
    borderRadius: 1.5,
  },
});
