import { Pressable, ScrollView, StyleSheet } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';
import { useHaptics } from '../hooks/useHaptics';

type FilterChipsProps<T extends string> = {
  options: { value: T; label: string }[];
  selected: T;
  onSelect: (value: T) => void;
};

export function FilterChips<T extends string>({ options, selected, onSelect }: FilterChipsProps<T>) {
  const c = useColors();
  const haptics = useHaptics();
  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      style={styles.scrollOuter}
      contentContainerStyle={styles.scroll}
    >
      {options.map((opt) => {
        const isActive = opt.value === selected;
        return (
          <Pressable
            key={opt.value}
            onPress={() => { haptics.selection(); onSelect(opt.value); }}
            accessibilityRole="button"
            accessibilityState={{ selected: isActive }}
            style={[
              styles.chip,
              { backgroundColor: isActive ? c.primary : c.surfaceAlt, borderColor: isActive ? c.primary : c.border },
            ]}
          >
            <Text
              variant="caption"
              color={isActive ? c.textInverse : c.textSecondary}
              style={styles.chipText}
            >
              {opt.label}
            </Text>
          </Pressable>
        );
      })}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scrollOuter: {
    flexGrow: 0,
    flexShrink: 0,
  },
  scroll: {
    gap: spacing.sm,
    paddingVertical: spacing.xs,
    alignItems: 'center',
  },
  chip: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.full,
    borderWidth: 1,
  },
  chipText: {
    fontWeight: '600',
  },
});
