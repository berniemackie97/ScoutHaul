import { Ionicons } from '@expo/vector-icons';
import { useRef } from 'react';
import { Animated, Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing, type ToneKey } from './theme';
import { useAppTheme, useTones } from '../context/ThemeContext';

type ActionCardProps = {
  icon: keyof typeof Ionicons.glyphMap;
  title: string;
  subtitle?: string;
  tone?: ToneKey;
  onPress?: () => void;
};

export function ActionCard({ icon, title, subtitle, tone = 'neutral', onPress }: ActionCardProps) {
  const { colors: col, shadows: s } = useAppTheme();
  const tones = useTones();
  const t = tones[tone];
  const scale = useRef(new Animated.Value(1)).current;

  function handlePressIn() {
    if (!onPress) return;
    Animated.spring(scale, { toValue: 0.98, useNativeDriver: true, speed: 50, bounciness: 0 }).start();
  }

  function handlePressOut() {
    Animated.spring(scale, { toValue: 1, useNativeDriver: true, speed: 30, bounciness: 4 }).start();
  }

  return (
    <Animated.View style={{ transform: [{ scale }] }}>
      <Pressable
        onPress={onPress}
        onPressIn={handlePressIn}
        onPressOut={handlePressOut}
        accessibilityRole={onPress ? 'button' : undefined}
        accessibilityLabel={title}
        style={({ pressed }) => [
          styles.card,
          { backgroundColor: col.surface, borderColor: col.borderLight, ...s.sm },
          pressed && onPress && { backgroundColor: col.surfaceAlt },
        ]}
      >
        <View style={[styles.iconWrap, { backgroundColor: t.bg, borderColor: t.border }]}>
          <Ionicons name={icon} size={20} color={t.text} />
        </View>
        <View style={styles.content}>
          <Text variant="bodyBold">{title}</Text>
          {subtitle ? <Text variant="caption">{subtitle}</Text> : null}
        </View>
        {onPress ? (
          <Ionicons name="chevron-forward" size={18} color={col.textMuted} />
        ) : null}
      </Pressable>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  card: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
    borderWidth: 1,
  },
  iconWrap: {
    width: 40,
    height: 40,
    borderRadius: radius.md,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  content: {
    flex: 1,
    gap: spacing.xxs,
  },
});
