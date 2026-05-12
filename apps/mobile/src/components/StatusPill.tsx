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
      style={[styles.pill, size === 'sm' && styles.sm, { backgroundColor: t.bg }]}
      accessibilityLabel={`${label} status`}
      accessibilityRole="text"
    >
      <Text variant={size === 'sm' ? 'label' : 'caption'} color={t.text} style={styles.label}>
        {label}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  pill: {
    alignSelf: 'flex-start',
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  sm: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
  },
  label: {
    fontWeight: '700',
  },
});
