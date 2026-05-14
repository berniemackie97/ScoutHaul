import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
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
import { getPendingSyncCount, getWorkspaceProfile, listItemDrafts, processSyncQueue, saveWorkspaceProfile, type AppWorkspaceProfile } from '../src/db/localStore';
import type { LocalItemDraft } from '../src/domain/types';
import { useConnectivity } from '../src/hooks/useConnectivity';
import { formatMoney } from '../src/utils/money';
import { signOut } from '../src/services/auth';

type ThemeMode = 'system' | 'light' | 'dark';
const themeModes: { value: ThemeMode; label: string; icon: keyof typeof Ionicons.glyphMap }[] = [
  { value: 'system', label: 'System', icon: 'phone-portrait-outline' },
  { value: 'light', label: 'Light', icon: 'sunny-outline' },
  { value: 'dark', label: 'Dark', icon: 'moon-outline' },
];

export default function SettingsScreen() {
  const { colors: c, mode, setMode } = useAppTheme();
  const connectivity = useConnectivity();
  const [drafts, setDrafts] = useState<LocalItemDraft[]>([]);
  const [pendingSync, setPendingSync] = useState(0);
  const [message, setMessage] = useState<string | null>(null);
  const [isSyncing, setIsSyncing] = useState(false);
  const [workspaceProfile, setWorkspaceProfile] = useState<AppWorkspaceProfile | null>(null);

  async function load() {
    setWorkspaceProfile(await getWorkspaceProfile());
    setDrafts(await listItemDrafts());
    setPendingSync(await getPendingSyncCount());
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  async function sync() {
    if (!connectivity.isOnline) {
      setMessage('Offline. Sync requires internet.');
      return;
    }
    setIsSyncing(true);
    const result = await processSyncQueue();
    setMessage(`Synced ${result.synced}, failed ${result.failed}.`);
    setIsSyncing(false);
    await load();
  }

  async function disconnectAccount() {
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
              {workspaceProfile?.usageMode === 'team' ? 'Team workspace' : 'Solo workspace'} · {workspaceProfile?.role ?? 'owner'}
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
            label={pendingSync > 0 ? 'Needs sync' : 'Clear'}
            tone={pendingSync > 0 ? 'warning' : 'success'}
            size="sm"
          />
        </View>
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

      <SectionHeader title={`Local Drafts (${drafts.length})`} />
      {drafts.length === 0 ? (
        <Card>
          <Text variant="caption">No local drafts.</Text>
        </Card>
      ) : (
        drafts.map((draft) => (
          <Card key={draft.id}>
            <View style={styles.draftRow}>
              <Text variant="bodyBold" numberOfLines={1} style={styles.flex}>
                {draft.title}
              </Text>
              <StatusPill label={draft.syncStatus} size="sm" />
            </View>
            <Text variant="caption">
              {formatMoney(draft.sellerAskingPrice)} · {new Date(draft.updatedAt).toLocaleDateString()}
            </Text>
          </Card>
        ))
      )}

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
