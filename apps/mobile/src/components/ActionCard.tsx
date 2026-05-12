import { Ionicons } from '@expo/vector-icons';
import { Pressable, StyleSheet, View } from 'react-native';
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

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole={onPress ? 'button' : undefined}
      accessibilityLabel={title}
      style={({ pressed }) => [
        styles.card,
        { backgroundColor: col.surface, borderColor: col.borderLight, ...s.sm },
        pressed && onPress && { opacity: 0.85, backgroundColor: col.surfaceAlt },
      ]}
    >
      <View style={[styles.iconWrap, { backgroundColor: t.bg }]}>
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
    alignItems: 'center',
    justifyContent: 'center',
  },
  content: {
    flex: 1,
    gap: spacing.xxs,
  },
});
