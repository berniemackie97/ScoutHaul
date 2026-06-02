import { Ionicons } from '@expo/vector-icons';
import { useRef } from 'react';
import { Animated, Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';
import { useHaptics } from '../hooks/useHaptics';

type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
type ButtonSize = 'sm' | 'md' | 'lg';

type ButtonProps = {
  label: string;
  icon?: keyof typeof Ionicons.glyphMap;
  variant?: ButtonVariant;
  size?: ButtonSize;
  disabled?: boolean;
  fullWidth?: boolean;
  onPress: () => void;
};

const iconSizes: Record<ButtonSize, number> = { sm: 16, md: 18, lg: 22 };
const heights: Record<ButtonSize, number> = { sm: 36, md: 44, lg: 52 };

export function Button({
  label,
  icon,
  variant = 'primary',
  size = 'md',
  disabled,
  fullWidth,
  onPress,
}: ButtonProps) {
  const c = useColors();
  const haptics = useHaptics();
  const scale = useRef(new Animated.Value(1)).current;

  const isSolid = variant === 'primary' || variant === 'danger';
  const iconColor = isSolid ? c.textInverse : c.primary;
  const textColor = isSolid ? c.textInverse : c.primary;

  const bgMap = {
    primary: c.primary,
    secondary: c.primaryLight,
    danger: c.danger,
    ghost: 'transparent',
  };

  const borderMap = {
    primary: c.primary,
    secondary: c.primary,
    danger: c.danger,
    ghost: 'transparent',
  };

  function handlePressIn() {
    Animated.spring(scale, { toValue: 0.97, useNativeDriver: true, speed: 50, bounciness: 0 }).start();
  }

  function handlePressOut() {
    Animated.spring(scale, { toValue: 1, useNativeDriver: true, speed: 30, bounciness: 4 }).start();
  }

  return (
    <Animated.View style={[fullWidth && styles.fullWidth, { transform: [{ scale }] }]}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={label}
        disabled={disabled}
        onPressIn={handlePressIn}
        onPressOut={handlePressOut}
        onPress={() => { haptics.light(); onPress(); }}
        style={({ pressed }) => [
          styles.button,
          {
            minHeight: heights[size],
            backgroundColor: bgMap[variant],
            borderColor: variant === 'secondary' ? c.border : borderMap[variant],
            borderWidth: variant === 'ghost' ? 0 : variant === 'secondary' ? 1 : 0,
          },
          size === 'sm' && styles.smPadding,
          size === 'lg' && styles.lgPadding,
          disabled && styles.disabled,
          pressed && !disabled && { opacity: 0.9 },
        ]}
      >
        <View style={styles.inner}>
          {icon ? <Ionicons name={icon} size={iconSizes[size]} color={disabled ? c.textMuted : iconColor} /> : null}
          <Text
            variant={size === 'lg' ? 'bodyBold' : size === 'sm' ? 'caption' : 'bodyBold'}
            color={disabled ? c.textMuted : textColor}
          >
            {label}
          </Text>
        </View>
      </Pressable>
    </Animated.View>
  );
}

type IconButtonProps = {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  size?: number;
  color?: string;
  onPress: () => void;
};

export function IconButton({ icon, label, size = 22, color, onPress }: IconButtonProps) {
  const c = useColors();
  const haptics = useHaptics();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={() => { haptics.light(); onPress(); }}
      hitSlop={8}
      style={({ pressed }) => [styles.iconButton, pressed && { opacity: 0.7, transform: [{ scale: 0.95 }] }]}
    >
      <Ionicons name={icon} size={size} color={color ?? c.textSecondary} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.lg,
  },
  inner: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
  },
  smPadding: {
    paddingHorizontal: spacing.md,
  },
  lgPadding: {
    paddingHorizontal: spacing.xl,
  },
  fullWidth: {
    width: '100%',
  },
  disabled: {
    opacity: 0.4,
  },
  iconButton: {
    padding: spacing.sm,
    borderRadius: radius.full,
  },
});
