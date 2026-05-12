import { Ionicons } from '@expo/vector-icons';
import { Pressable, StyleSheet, View } from 'react-native';
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
      <Text variant="metric">{value}</Text>
      {subtitle ? <Text variant="caption">{subtitle}</Text> : null}
    </>
  );

  if (onPress) {
    return (
      <Pressable
        onPress={onPress}
        accessibilityRole="button"
        accessibilityLabel={`${label}: ${value}`}
        style={({ pressed }) => [...cardStyle, pressed && styles.pressed]}
      >
        {content}
      </Pressable>
    );
  }

  return <View style={cardStyle} accessibilityLabel={`${label}: ${value}`}>{content}</View>;
}

const styles = StyleSheet.create({
  card: {
    flex: 1,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.xs,
    minHeight: 100,
    borderWidth: 1,
  },
  pressed: {
    opacity: 0.85,
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
});
