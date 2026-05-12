import { Ionicons } from '@expo/vector-icons';
import { useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Divider } from '../src/components/Divider';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import { getPendingSyncCount, listItemDrafts, processSyncQueue } from '../src/db/localStore';
import type { LocalItemDraft } from '../src/domain/types';
import { useConnectivity } from '../src/hooks/useConnectivity';
import { formatMoney } from '../src/utils/money';

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

  async function load() {
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

  return (
    <Screen>
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
        <Text variant="caption" selectable>API: {api.baseUrl}</Text>
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
          ScoutHaul MVP. Authentication is stubbed via headers for local development. Marketplace integrations use external search launchers and manual comp capture.
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
});
