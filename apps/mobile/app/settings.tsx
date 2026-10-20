import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { appConfig } from '../src/config';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Divider } from '../src/components/Divider';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import { discardOperation, getPendingSyncCount, getWorkspaceProfile, listStuckOperations, processSyncQueue, retryStuckOperation, saveWorkspaceProfile, type AppWorkspaceProfile, type SyncQueueRow } from '../src/db/localStore';
import { useConnectivity } from '../src/hooks/useConnectivity';
import { signOut } from '../src/services/auth';
import { confirmSignOut } from '../src/services/confirmSignOut';

type ThemeMode = 'system' | 'light' | 'dark';
const themeModes: { value: ThemeMode; label: string; icon: keyof typeof Ionicons.glyphMap }[] = [
  { value: 'system', label: 'System', icon: 'phone-portrait-outline' },
  { value: 'light', label: 'Light', icon: 'sunny-outline' },
  { value: 'dark', label: 'Dark', icon: 'moon-outline' },
];

export default function SettingsScreen() {
  const { colors: c, mode, setMode } = useAppTheme();
  const connectivity = useConnectivity();
  const [pendingSync, setPendingSync] = useState(0);
  const [stuck, setStuck] = useState<SyncQueueRow[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [isSyncing, setIsSyncing] = useState(false);
  const [workspaceProfile, setWorkspaceProfile] = useState<AppWorkspaceProfile | null>(null);

  async function load() {
    setWorkspaceProfile(await getWorkspaceProfile());
    setPendingSync(await getPendingSyncCount());
    setStuck(await listStuckOperations());
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  async function sync() {
    if (!connectivity.isOnline) {
      setMessage('Offline. Sync requires internet.');
      return;
    }
    setIsSyncing(true);
    const result = await processSyncQueue();
    setMessage(
      result.stuck > 0
        ? `Synced ${result.synced}, retrying ${result.failed}, ${result.stuck} need attention.`
        : `Synced ${result.synced}, retrying ${result.failed}.`,
    );
    setIsSyncing(false);
    await load();
  }

  async function retryStuck(id: string) {
    await retryStuckOperation(id);
    await load();
    setMessage('Queued to try again on the next sync.');
  }

  function confirmDiscard(row: SyncQueueRow) {
    // Discarding is the one action here that loses work for good, so it asks first.
    Alert.alert(
      'Discard this change?',
      `${row.operationType} will be deleted and never sent. This cannot be undone.`,
      [
        { text: 'Keep', style: 'cancel' },
        {
          text: 'Discard',
          style: 'destructive',
          onPress: () => {
            void (async () => {
              await discardOperation(row.id);
              await load();
              setMessage('Discarded.');
            })();
          },
        },
      ],
    );
  }

  async function disconnectAccount() {
    if (!(await confirmSignOut())) return;
    await signOut();
    await saveWorkspaceProfile({ accountMode: 'local', accountEmail: null, displayName: null });
    await load();
  }

  return (
    <Screen>
      <SectionHeader title="Workspace" />
      <Card>
        <View style={styles.rowBetween}>
          <View style={styles.flex}>
            <Text variant="bodyBold">{workspaceProfile?.workspaceName ?? 'My resale workspace'}</Text>
            <Text variant="caption">
              {workspaceProfile?.usageMode === 'team'
                ? 'Team workspace'
                : workspaceProfile?.usageMode === 'join'
                  ? 'Joined team workspace'
                  : 'Solo workspace'} · {workspaceProfile?.role ?? 'owner'}
            </Text>
          </View>
          <StatusPill label={workspaceProfile?.planIntent ?? 'starter'} tone="primary" size="sm" />
        </View>
        <Button
          label="Run Setup Again"
          icon="compass-outline"
          variant="secondary"
          fullWidth
          onPress={() => router.push('/onboarding')}
        />
      </Card>

      <SectionHeader title="Account" />
      <Card>
        <View style={styles.rowBetween}>
          <View style={styles.flex}>
            <Text variant="bodyBold">
              {workspaceProfile?.accountMode === 'signedIn' ? workspaceProfile.accountEmail ?? 'Signed in' : 'Local workspace'}
            </Text>
            <Text variant="caption">
              {workspaceProfile?.accountMode === 'signedIn'
                ? 'Cloud auth token is stored securely on this device.'
                : 'No account is connected. Local/offline workflows stay available.'}
            </Text>
          </View>
          <StatusPill label={workspaceProfile?.accountMode ?? 'local'} tone={workspaceProfile?.accountMode === 'signedIn' ? 'success' : 'neutral'} size="sm" />
        </View>
        <View style={styles.buttonRow}>
          {workspaceProfile?.accountMode !== 'signedIn' ? (
            <Button label="Sign In / Up" icon="person-circle-outline" variant="secondary" onPress={() => router.push('/account')} />
          ) : (
            <Button label="Manage Account" icon="person-circle-outline" variant="secondary" onPress={() => router.push('/account')} />
          )}
          {workspaceProfile?.accountMode === 'signedIn' ? (
            <Button label="Sign Out" icon="log-out-outline" variant="danger" onPress={() => void disconnectAccount()} />
          ) : null}
        </View>
      </Card>

      <SectionHeader title="Appearance" />
      <Card>
        <View style={styles.themeRow}>
          {themeModes.map((t) => {
            const active = t.value === mode;
            return (
              <Pressable
                key={t.value}
                onPress={() => setMode(t.value)}
                accessibilityRole="button"
                accessibilityState={{ selected: active }}
                style={[styles.themeOption, active && { backgroundColor: c.primaryLight, borderColor: c.primary }]}
              >
                <Ionicons name={t.icon} size={20} color={active ? c.primary : c.textMuted} />
                <Text variant="caption" color={active ? c.primary : c.textSecondary}>{t.label}</Text>
              </Pressable>
            );
          })}
        </View>
      </Card>

      <SectionHeader title="Connection" />
      <Card>
        <View style={styles.row}>
          <Ionicons
            name={connectivity.isOnline ? 'cloud-done-outline' : 'cloud-offline-outline'}
            size={20}
            color={connectivity.isOnline ? c.success : c.warning}
          />
          <Text variant="bodyBold">{connectivity.label}</Text>
        </View>
        <Text variant="caption" selectable>API: {appConfig.apiBaseUrl}</Text>
      </Card>

      <SectionHeader title="Sync Queue" />
      <Card>
        <View style={styles.row}>
          <Text variant="bodyBold">{pendingSync} pending</Text>
          <StatusPill
            label={pendingSync > 0 ? 'Not safe yet' : 'Safe to update'}
            tone={pendingSync > 0 ? 'warning' : 'success'}
            size="sm"
          />
        </View>
        <Text variant="caption" color={pendingSync > 0 ? c.warningDark : c.successDark}>
          {pendingSync > 0
            ? 'Sync before installing a new APK so queued local work reaches the server.'
            : 'Signed-in server data should restore after reinstalling or updating the APK.'}
        </Text>
        <Button
          label={isSyncing ? 'Syncing...' : 'Sync Now'}
          icon="cloud-upload-outline"
          fullWidth
          disabled={isSyncing || pendingSync === 0}
          onPress={() => void sync()}
        />
        {message ? (
          <Text variant="caption" color={c.textSecondary}>{message}</Text>
        ) : null}
      </Card>

      {stuck.length > 0 ? (
        <>
          <SectionHeader title={`Needs Attention (${stuck.length})`} />
          {stuck.map((row) => (
            <Card key={row.id}>
              <View style={styles.row}>
                <Text variant="bodyBold" numberOfLines={1} style={styles.flex}>
                  {row.operationType}
                </Text>
                <StatusPill label={`${row.attempts} tries`} tone="danger" size="sm" />
              </View>
              {/* The reason was recorded all along and never shown, so a write that could not be
                  delivered just sat in the pending count with no way to find out why. */}
              <Text variant="caption" color={c.dangerDark}>
                {row.lastError ?? 'No reason recorded.'}
              </Text>
              <Text variant="caption" color={c.textSecondary}>
                Saved {new Date(row.createdAt).toLocaleString()}
              </Text>
              <View style={styles.stuckActions}>
                <Button
                  label="Try Again"
                  icon="refresh-outline"
                  variant="secondary"
                  onPress={() => void retryStuck(row.id)}
                />
                <Button
                  label="Discard"
                  icon="trash-outline"
                  variant="secondary"
                  onPress={() => void confirmDiscard(row)}
                />
              </View>
            </Card>
          ))}
        </>
      ) : null}


      <SectionHeader title="About" />
      <Card>
        <Text variant="caption">
          HaulScout. Supabase handles cloud identity when configured; local and stub-auth workflows remain available for offline-first development and demos.
        </Text>
        <Divider />
        <Text variant="caption">
          Active listings are never treated as sold prices. Offline mode never fabricates live market pricing.
        </Text>
      </Card>
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  rowBetween: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  draftRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.sm,
  },
  stuckActions: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  flex: {
    flex: 1,
  },
  themeRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  themeOption: {
    flex: 1,
    alignItems: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.md,
    borderRadius: 10,
    borderWidth: 1.5,
    borderColor: 'transparent',
  },
  buttonRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
});
