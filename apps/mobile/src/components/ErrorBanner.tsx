import { Ionicons } from '@expo/vector-icons';
import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';

type ErrorBannerProps = {
  message: string;
  onRetry?: () => void;
};

export function ErrorBanner({ message, onRetry }: ErrorBannerProps) {
  const c = useColors();
  return (
    <View style={[styles.banner, { backgroundColor: c.dangerLight }]}>
      <View style={styles.content}>
        <Ionicons name="alert-circle" size={20} color={c.dangerDark} />
        <Text variant="caption" color={c.dangerDark} style={styles.text}>
          {message}
        </Text>
      </View>
      {onRetry ? (
        <Pressable onPress={onRetry} hitSlop={8} style={styles.retry} accessibilityRole="button" accessibilityLabel="Retry">
          <Text variant="caption" color={c.dangerDark} style={styles.retryText}>
            Retry
          </Text>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  banner: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.sm,
  },
  content: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  text: {
    flex: 1,
  },
  retry: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  retryText: {
    fontWeight: '700',
    textDecorationLine: 'underline',
  },
});
