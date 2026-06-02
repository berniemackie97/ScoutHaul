import { StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing, type ToneKey } from './theme';
import { useTones } from '../context/ThemeContext';

type StatusPillProps = {
  label: string;
  tone?: ToneKey;
  size?: 'sm' | 'md';
};

export function StatusPill({ label, tone = 'neutral', size = 'md' }: StatusPillProps) {
  const tones = useTones();
  const t = tones[tone];
  return (
    <View
      style={[
        styles.pill,
        size === 'sm' && styles.sm,
        { backgroundColor: t.bg, borderColor: t.border },
      ]}
      accessibilityLabel={`${label} status`}
      accessibilityRole="text"
    >
      <View style={[styles.dot, { backgroundColor: t.border }]} />
      <Text variant={size === 'sm' ? 'label' : 'caption'} color={t.text} style={styles.label}>
        {label}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  pill: {
    alignSelf: 'flex-start',
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: radius.full,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
    gap: spacing.xs,
    borderWidth: 1,
  },
  sm: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
  },
  dot: {
    width: 6,
    height: 6,
    borderRadius: 3,
  },
  label: {},
});
