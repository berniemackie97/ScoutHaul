import { Ionicons } from '@expo/vector-icons';
import { StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';

type OfflineBannerProps = {
  message?: string;
};

export function OfflineBanner({ message = 'You are offline. Some features require internet.' }: OfflineBannerProps) {
  const c = useColors();
  return (
    <View style={[styles.banner, { backgroundColor: c.warningLight }]}>
      <Ionicons name="cloud-offline-outline" size={18} color={c.warningDark} />
      <Text variant="caption" color={c.warningDark} style={styles.text}>
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
