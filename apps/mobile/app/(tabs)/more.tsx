import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { ActionCard } from '../../src/components/ActionCard';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { authStore } from '../../src/services/auth';

export default function MoreScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const [isAuthed, setIsAuthed] = useState(authStore.isAuthenticated);
  const accountInfo = authStore.getAccount();

  useEffect(() => {
    return authStore.subscribe((authed) => setIsAuthed(authed));
  }, []);

  return (
    <Screen edges={['top', 'left', 'right']}>
      <Text variant="title">More</Text>

      {/* Account Summary Card */}
      <Pressable
        onPress={() => router.push('/account')}
        style={({ pressed }) => [
          styles.accountCard,
          { backgroundColor: c.surface, borderColor: c.borderLight, ...s.md },
          pressed && { backgroundColor: c.surfaceAlt },
        ]}
      >
        <View style={[styles.avatar, { backgroundColor: isAuthed ? c.primaryLight : c.surfaceAlt }]}>
          <Ionicons
            name={isAuthed ? 'person' : 'person-outline'}
            size={24}
            color={isAuthed ? c.primary : c.textMuted}
          />
        </View>
        <View style={styles.accountInfo}>
          <Text variant="bodyBold">
            {isAuthed
              ? accountInfo?.displayName ?? accountInfo?.email ?? 'Signed In'
              : 'Local Mode'}
          </Text>
          <Text variant="caption" numberOfLines={1}>
            {isAuthed
              ? accountInfo?.email ?? 'Cloud sync active'
              : 'Tap to sign in or create account'}
          </Text>
        </View>
        <StatusPill
          label={isAuthed ? 'Connected' : 'Local'}
          tone={isAuthed ? 'success' : 'neutral'}
          size="sm"
        />
      </Pressable>

      <SectionHeader title="Workflow" />
      <ActionCard
        icon="wallet-outline"
        title="Budget"
        subtitle="Track spending and remaining budget"
        onPress={() => router.push('/budget')}
      />
      <ActionCard
        icon="receipt-outline"
        title="Purchases"
        subtitle="Inspect acquisition lots, landed cost, and item allocation"
        onPress={() => router.push('/purchases')}
      />
      <ActionCard
        icon="pricetags-outline"
        title="Listing Queue"
        subtitle="Manage items through the listing workflow"
        onPress={() => router.push('/listing-queue')}
      />
      <ActionCard
        icon="receipt-outline"
        title="Orders & Sales"
        subtitle="Record sales, fulfill orders, handle refunds and returns"
        onPress={() => router.push('/(tabs)/orders')}
      />
      <ActionCard
        icon="checkbox-outline"
        title="Tasks"
        subtitle="To-do list for resale work"
        onPress={() => router.push('/tasks')}
      />
      <ActionCard
        icon="navigate-outline"
        title="Routes"
        subtitle="Sourcing trips and stops"
        onPress={() => router.push('/trips')}
      />

      <SectionHeader title="Workspace" />
      <ActionCard
        icon="business-outline"
        title="Workspace Admin"
        subtitle="Teams, members, roles, and permissions"
        onPress={() => router.push('/workspace-admin')}
      />
      <ActionCard
        icon="settings-outline"
        title="Settings"
        subtitle="Theme, sync, API, and local data"
        onPress={() => router.push('/settings')}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  accountCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.lg,
    borderRadius: radius.xl,
    borderWidth: 1,
  },
  avatar: {
    width: 48,
    height: 48,
    borderRadius: 24,
    alignItems: 'center',
    justifyContent: 'center',
  },
  accountInfo: {
    flex: 1,
    gap: spacing.xxs,
  },
});
