import { Ionicons } from '@expo/vector-icons';
import { LinearGradient } from 'expo-linear-gradient';
import { StyleSheet, View } from 'react-native';
import { Text } from '../../components/Text';
import { spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';

type AccountHeaderProps = {
  title: string;
  topInset: number;
};

export function AccountHeader({ title, topInset }: AccountHeaderProps) {
  const c = useColors();

  return (
    <LinearGradient
      colors={[c.primary, c.primaryPressed]}
      start={{ x: 0, y: 0 }}
      end={{ x: 1, y: 1 }}
      style={[styles.gradient, { paddingTop: topInset + spacing.lg }]}
    >
      <View style={[styles.decorCircle, styles.decorCircle1]} />
      <View style={[styles.decorCircle, styles.decorCircle2]} />
      <View style={styles.content}>
        <View style={styles.logoRow}>
          <View style={styles.logoMark}>
            <Ionicons name="compass" size={20} color={c.primary} />
          </View>
          <Text variant="bodyBold" color={c.textInverse} style={styles.logoText}>
            ScoutHaul
          </Text>
        </View>
        <Text variant="title" color={c.textInverse} style={styles.heroTitle}>
          {title}
        </Text>
      </View>
    </LinearGradient>
  );
}

const styles = StyleSheet.create({
  gradient: {
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.xl,
    overflow: 'hidden',
  },
  decorCircle: {
    position: 'absolute',
    borderRadius: 999,
    backgroundColor: 'rgba(255,255,255,0.06)',
  },
  decorCircle1: {
    width: 180,
    height: 180,
    top: -50,
    right: -50,
  },
  decorCircle2: {
    width: 100,
    height: 100,
    bottom: -20,
    left: -20,
  },
  content: {
    gap: spacing.sm,
  },
  logoRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  logoMark: {
    width: 32,
    height: 32,
    borderRadius: 8,
    backgroundColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
  },
  logoText: {
    fontSize: 16,
    letterSpacing: 0.5,
  },
  heroTitle: {
    fontSize: 24,
    lineHeight: 30,
    fontWeight: '800',
    letterSpacing: -0.3,
  },
});
