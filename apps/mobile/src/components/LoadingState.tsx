import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { spacing } from './theme';
import { useColors } from '../context/ThemeContext';

type LoadingStateProps = {
  message?: string;
  compact?: boolean;
};

export function LoadingState({ message = 'Loading...', compact }: LoadingStateProps) {
  const c = useColors();
  return (
    <View style={[styles.container, compact && styles.compact]}>
      <ActivityIndicator size="large" color={c.primary} />
      <Text variant="caption">{message}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: spacing.xxxl,
    gap: spacing.md,
  },
  compact: {
    paddingVertical: spacing.xl,
  },
});
