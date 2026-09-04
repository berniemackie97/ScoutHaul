import { Ionicons } from '@expo/vector-icons';
import { StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';

type QueuedBannerProps = {
  message: string;
};

/**
 * "Kept on this phone, not on the server yet."
 *
 * This is a success, and it needs its own banner because it was being shown through ErrorBanner —
 * red, with a Retry button. That read as a failure, and tapping Retry re-sent a write that had
 * already been queued, so the item was created twice. Nothing here is actionable: the queue syncs on
 * its own once there is a connection.
 */
export function QueuedBanner({ message }: QueuedBannerProps) {
  const c = useColors();
  return (
    <View style={[styles.banner, { backgroundColor: c.infoLight }]}>
      <Ionicons name="cloud-upload-outline" size={18} color={c.info} />
      <Text variant="caption" color={c.info} style={styles.text}>
        {message}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  banner: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.sm,
  },
  text: {
    flex: 1,
  },
});
