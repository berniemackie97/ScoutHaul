import { Ionicons } from '@expo/vector-icons';
import { useRef } from 'react';
import { Animated, Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing, type ToneKey } from './theme';
import { useAppTheme, useTones } from '../context/ThemeContext';

type MetricCardProps = {
  label: string;
  value: string;
  icon?: keyof typeof Ionicons.glyphMap;
  tone?: ToneKey;
  subtitle?: string;
  onPress?: () => void;
};

export function MetricCard({ label, value, icon, tone, subtitle, onPress }: MetricCardProps) {
  const { colors: col, shadows: s } = useAppTheme();
  const tones = useTones();
  const t = tone ? tones[tone] : null;
  const scale = useRef(new Animated.Value(1)).current;

  function handlePressIn() {
    if (!onPress) return;
    Animated.spring(scale, { toValue: 0.97, useNativeDriver: true, speed: 50, bounciness: 0 }).start();
  }

  function handlePressOut() {
    Animated.spring(scale, { toValue: 1, useNativeDriver: true, speed: 30, bounciness: 4 }).start();
  }

  const cardStyle = [styles.card, { backgroundColor: col.surface, borderColor: col.borderLight, ...s.sm }];

  const content = (
    <>
      <View style={styles.top}>
        {icon ? (
          <View style={[styles.iconWrap, { backgroundColor: t?.bg ?? col.surfaceAlt }]}>
            <Ionicons name={icon} size={16} color={t?.text ?? col.textSecondary} />
          </View>
        ) : null}
        <Text variant="label">{label}</Text>
      </View>
      <Text variant="metric" color={t?.text}>{value}</Text>
      {subtitle ? <Text variant="caption">{subtitle}</Text> : null}
      {t ? <View style={[styles.accentStripe, { backgroundColor: t.border }]} /> : null}
    </>
  );

  if (onPress) {
    return (
      <Animated.View style={[styles.flex, { transform: [{ scale }] }]}>
        <Pressable
          onPress={onPress}
          onPressIn={handlePressIn}
          onPressOut={handlePressOut}
          accessibilityRole="button"
          accessibilityLabel={`${label}: ${value}`}
          style={({ pressed }) => [...cardStyle, pressed && { backgroundColor: col.surfaceAlt }]}
        >
          {content}
        </Pressable>
      </Animated.View>
    );
  }

  return (
    <View style={cardStyle} accessibilityLabel={`${label}: ${value}`}>
      {content}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  card: {
    flex: 1,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.xs,
    minHeight: 100,
    borderWidth: 1,
    overflow: 'hidden',
  },
  top: {
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
  accentStripe: {
    position: 'absolute',
    bottom: 0,
    left: 0,
    right: 0,
    height: 3,
  },
});
