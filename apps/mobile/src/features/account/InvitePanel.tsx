import { Ionicons } from '@expo/vector-icons';
import { ActivityIndicator, Pressable, StyleSheet, TextInput, View } from 'react-native';
import { Text } from '../../components/Text';
import { radius, spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';

type InvitePanelProps = {
  inviteCode: string;
  onChangeCode: (code: string) => void;
  isJoining: boolean;
  isAuthenticated: boolean;
  onJoin: () => void;
};

export function InvitePanel({ inviteCode, onChangeCode, isJoining, isAuthenticated, onJoin }: InvitePanelProps) {
  const c = useColors();

  return (
    <View style={[styles.card, { backgroundColor: c.surface, borderColor: c.border }]}>
      <View style={styles.header}>
        <View style={styles.icon}>
          <Ionicons name="enter-outline" size={18} color={c.primary} />
        </View>
        <View style={styles.headerText}>
          <Text variant="bodyBold">Join a team</Text>
          <Text variant="caption" color={c.textMuted}>
            Paste an invite code from your owner or manager.
          </Text>
        </View>
      </View>

      <View style={styles.fieldBlock}>
        <Text variant="caption" color={c.textSecondary} style={styles.fieldLabel}>
          Team invite code
        </Text>
        <View style={[styles.inputContainer, { borderColor: c.border, backgroundColor: c.surfaceAlt }]}>
          <Ionicons name="key-outline" size={18} color={c.textMuted} style={styles.inputIcon} />
          <TextInput
            value={inviteCode}
            onChangeText={onChangeCode}
            placeholder="SH-ABCD-1234-EF56"
            placeholderTextColor={c.textMuted}
            autoCapitalize="characters"
            autoCorrect={false}
            style={[styles.textInput, { color: c.text }]}
          />
        </View>
      </View>

      <Pressable
        accessibilityRole="button"
        disabled={isJoining}
        onPress={onJoin}
        style={({ pressed }) => [
          styles.joinButton,
          { backgroundColor: c.primary },
          pressed && { backgroundColor: c.primaryPressed },
          isJoining && styles.disabled,
        ]}
      >
        {isJoining ? (
          <ActivityIndicator size="small" color={c.textInverse} />
        ) : (
          <View style={styles.inner}>
            <Text variant="bodyBold" color={c.textInverse}>
              {isAuthenticated ? 'Join Team' : 'Save Code & Sign In'}
            </Text>
            <Ionicons name="arrow-forward" size={18} color={c.textInverse} />
          </View>
        )}
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderWidth: 1,
    borderRadius: radius.xl,
    padding: spacing.lg,
    gap: spacing.md,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  icon: {
    width: 36,
    height: 36,
    borderRadius: radius.full,
    backgroundColor: 'rgba(11, 107, 91, 0.12)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  headerText: {
    flex: 1,
    gap: spacing.xs,
  },
  fieldBlock: {
    gap: spacing.xs,
  },
  fieldLabel: {
    fontSize: 12,
    fontWeight: '600',
    letterSpacing: 0.3,
    marginLeft: spacing.xs,
  },
  inputContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: 50,
    borderWidth: 1.5,
    borderRadius: radius.md,
  },
  inputIcon: {
    marginLeft: spacing.md,
  },
  textInput: {
    flex: 1,
    minHeight: 50,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    fontSize: 15,
  },
  joinButton: {
    minHeight: 48,
    borderRadius: radius.lg,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.lg,
  },
  inner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  disabled: {
    opacity: 0.5,
  },
});
