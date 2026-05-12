import { StyleProp, StyleSheet, TextInput, TextInputProps, View, ViewStyle } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';

type TextFieldProps = TextInputProps & {
  label: string;
  prefix?: string;
  error?: string | null;
  hint?: string;
  containerStyle?: StyleProp<ViewStyle>;
};

export function TextField({ label, prefix, error, hint, style, containerStyle, ...props }: TextFieldProps) {
  const c = useColors();

  return (
    <View style={[styles.field, containerStyle]}>
      {label ? <Text variant="caption" color={c.textSecondary}>{label}</Text> : null}
      <View
        style={[styles.inputRow, { borderColor: error ? c.danger : c.border, backgroundColor: c.surface }]}
        accessibilityLabel={label || undefined}
      >
        {prefix ? <Text variant="body" color={c.textMuted} style={styles.prefix}>{prefix}</Text> : null}
        <TextInput
          placeholderTextColor={c.textMuted}
          style={[styles.input, { color: c.text }, prefix && styles.inputWithPrefix, style]}
          autoCapitalize="sentences"
          {...props}
        />
      </View>
      {error ? <Text variant="caption" color={c.danger}>{error}</Text> : null}
      {hint && !error ? <Text variant="caption" color={c.textMuted}>{hint}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  field: {
    gap: spacing.xs,
  },
  inputRow: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: 46,
    borderWidth: 1,
    borderRadius: radius.md,
  },
  prefix: {
    paddingLeft: spacing.md,
  },
  input: {
    flex: 1,
    minHeight: 46,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    fontSize: 15,
  },
  inputWithPrefix: {
    paddingLeft: spacing.xs,
  },
});
