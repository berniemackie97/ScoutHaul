import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '../../components/Text';
import { radius, spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';
import { permissionLabel } from './permissionLabels';

type PermissionPickerProps = {
  permissions: string[];
  selected: string[];
  disabled?: boolean;
  onToggle: (permission: string) => void;
};

export function PermissionPicker({ permissions, selected, disabled, onToggle }: PermissionPickerProps) {
  const c = useColors();

  return (
    <View style={styles.wrap}>
      {permissions.map((permission) => {
        const isSelected = selected.includes(permission);
        return (
          <Pressable
            key={permission}
            accessibilityRole="checkbox"
            accessibilityState={{ checked: isSelected, disabled }}
            disabled={disabled}
            onPress={() => onToggle(permission)}
            style={[
              styles.chip,
              {
                backgroundColor: isSelected ? c.primary : c.surfaceAlt,
                borderColor: isSelected ? c.primary : c.border,
                opacity: disabled ? 0.55 : 1,
              },
            ]}
          >
            <Text variant="caption" color={isSelected ? c.textInverse : c.textSecondary}>
              {permissionLabel(permission)}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  chip: {
    borderWidth: 1,
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
});
