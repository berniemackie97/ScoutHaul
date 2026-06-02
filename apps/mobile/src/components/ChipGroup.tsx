import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useAppTheme } from '../context/ThemeContext';

type ChipGroupProps = {
  label: string;
  value: string;
  options: readonly string[];
  onSelect: (value: string) => void;
  compact?: boolean;
};

export function ChipGroup({ label, value, options, onSelect, compact }: ChipGroupProps) {
  const { colors: c } = useAppTheme();
  return (
    <View style={styles.container}>
      <Text variant="caption" color={c.textSecondary}>{label}</Text>
      <View style={styles.wrap}>
        {options.map((option) => {
          const selected = option === value;
          return (
            <Pressable
              key={option}
              accessibilityRole="button"
              accessibilityState={{ selected }}
              onPress={() => onSelect(selected ? '' : option)}
              style={[
                styles.chip,
                compact && styles.chipCompact,
                {
                  backgroundColor: selected ? c.primary : c.surfaceAlt,
                  borderColor: selected ? c.primary : c.border,
                },
              ]}
            >
              <Text variant="caption" color={selected ? c.textInverse : c.textSecondary}>
                {option}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: spacing.xs,
  },
  wrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  chip: {
    minHeight: 36,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.full,
    borderWidth: 1,
    justifyContent: 'center',
  },
  chipCompact: {
    minHeight: 30,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
  },
});
