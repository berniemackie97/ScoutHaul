import { PropsWithChildren } from 'react';
import { StyleSheet, View, ViewProps } from 'react-native';
import { radius, spacing } from './theme';
import { useAppTheme } from '../context/ThemeContext';

type CardVariant = 'default' | 'flat' | 'elevated';

type CardProps = PropsWithChildren<
  ViewProps & {
    variant?: CardVariant;
    noPadding?: boolean;
  }
>;

export function Card({ children, variant = 'default', noPadding, style, ...props }: CardProps) {
  const { colors: c, shadows: s } = useAppTheme();

  const variantStyle =
    variant === 'elevated'
      ? s.md
      : variant === 'flat'
        ? { borderWidth: 1, borderColor: c.border }
        : { ...s.sm, borderWidth: 1, borderColor: c.borderLight };

  return (
    <View
      style={[
        styles.base,
        { backgroundColor: c.surface },
        variantStyle,
        noPadding && styles.noPadding,
        style,
      ]}
      {...props}
    >
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  base: {
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  noPadding: {
    padding: 0,
  },
});
