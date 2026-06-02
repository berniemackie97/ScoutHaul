import { Ionicons } from '@expo/vector-icons';
import { useEffect, useRef } from 'react';
import { Animated, Easing, StyleSheet, View } from 'react-native';
import { Button } from './Button';
import { Text } from './Text';
import { spacing } from './theme';
import { useAppTheme } from '../context/ThemeContext';

type EmptyStateProps = {
  icon?: keyof typeof Ionicons.glyphMap;
  title: string;
  message: string;
  actionLabel?: string;
  onAction?: () => void;
};

export function EmptyState({ icon = 'file-tray-outline', title, message, actionLabel, onAction }: EmptyStateProps) {
  const { colors: c } = useAppTheme();
  const scale = useRef(new Animated.Value(0.9)).current;
  const opacity = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    Animated.parallel([
      Animated.timing(opacity, { toValue: 1, duration: 400, useNativeDriver: true }),
      Animated.timing(scale, { toValue: 1, duration: 500, easing: Easing.out(Easing.back(1.2)), useNativeDriver: true }),
    ]).start();
  }, []);

  return (
    <Animated.View style={[styles.container, { opacity, transform: [{ scale }] }]}>
      <View style={styles.iconStack}>
        <View style={[styles.outerRing, { backgroundColor: c.surfaceAlt }]} />
        <View style={[styles.innerRing, { backgroundColor: c.primaryLight }]} />
        <View style={[styles.iconCenter, { backgroundColor: c.surface }]}>
          <Ionicons name={icon} size={32} color={c.primary} />
        </View>
      </View>
      <Text variant="subtitle" style={styles.title}>
        {title}
      </Text>
      <Text variant="caption" style={styles.message}>
        {message}
      </Text>
      {actionLabel && onAction ? (
        <View style={styles.action}>
          <Button label={actionLabel} variant="secondary" size="sm" onPress={onAction} />
        </View>
      ) : null}
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  container: {
    alignItems: 'center',
    paddingVertical: spacing.xxxl,
    paddingHorizontal: spacing.xl,
    gap: spacing.sm,
  },
  iconStack: {
    width: 88,
    height: 88,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.md,
  },
  outerRing: {
    position: 'absolute',
    width: 88,
    height: 88,
    borderRadius: 44,
  },
  innerRing: {
    position: 'absolute',
    width: 64,
    height: 64,
    borderRadius: 32,
  },
  iconCenter: {
    width: 48,
    height: 48,
    borderRadius: 24,
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  title: {
    textAlign: 'center',
  },
  message: {
    textAlign: 'center',
    maxWidth: 280,
  },
  action: {
    marginTop: spacing.sm,
  },
});
