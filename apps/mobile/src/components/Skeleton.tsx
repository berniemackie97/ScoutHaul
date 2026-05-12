import { useEffect, useRef } from 'react';
import { Animated, StyleSheet, View, ViewStyle } from 'react-native';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';

type SkeletonProps = {
  width?: number | string;
  height?: number;
  borderRadius?: number;
  style?: ViewStyle;
};

function SkeletonBlock({ width = '100%', height = 16, borderRadius: br = radius.sm, style }: SkeletonProps) {
  const c = useColors();
  const anim = useRef(new Animated.Value(0.3)).current;

  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(anim, { toValue: 1, duration: 800, useNativeDriver: true }),
        Animated.timing(anim, { toValue: 0.3, duration: 800, useNativeDriver: true }),
      ]),
    );
    loop.start();
    return () => loop.stop();
  }, [anim]);

  return (
    <Animated.View
      style={[
        { width: width as number, height, borderRadius: br, backgroundColor: c.border, opacity: anim },
        style,
      ]}
    />
  );
}

export function SkeletonCard() {
  const c = useColors();
  return (
    <View style={[styles.card, { backgroundColor: c.surface, borderColor: c.borderLight }]}>
      <SkeletonBlock width="60%" height={14} />
      <SkeletonBlock width="100%" height={12} />
      <SkeletonBlock width="40%" height={12} />
    </View>
  );
}

export function SkeletonMetric() {
  const c = useColors();
  return (
    <View style={[styles.metric, { backgroundColor: c.surface, borderColor: c.borderLight }]}>
      <SkeletonBlock width="50%" height={10} />
      <SkeletonBlock width="70%" height={24} />
    </View>
  );
}

export function SkeletonList({ count = 3 }: { count?: number }) {
  return (
    <View style={styles.list}>
      {Array.from({ length: count }, (_, i) => (
        <SkeletonCard key={i} />
      ))}
    </View>
  );
}

export { SkeletonBlock as Skeleton };

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
    borderWidth: 1,
  },
  metric: {
    flex: 1,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
    minHeight: 100,
    borderWidth: 1,
  },
  list: {
    gap: spacing.md,
  },
});
