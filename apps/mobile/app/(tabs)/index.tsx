import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { IconButton } from '../../src/components/Button';
import { authStore } from '../../src/services/auth';
import { ActionCard } from '../../src/components/ActionCard';
import { Card } from '../../src/components/Card';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { MetricCard } from '../../src/components/MetricCard';
import { OfflineBanner } from '../../src/components/OfflineBanner';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { spacing } from '../../src/components/theme';
import { useColors } from '../../src/context/ThemeContext';
import { getPendingSyncCount, getWorkspaceProfile, type AppWorkspaceProfile } from '../../src/db/localStore';
import type { DashboardSummary } from '../../src/domain/types';
import { useConnectivity } from '../../src/hooks/useConnectivity';
import { formatMoney } from '../../src/utils/money';

export default function DashboardScreen() {
  const c = useColors();
  const connectivity = useConnectivity();
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [pendingSync, setPendingSync] = useState(0);
  const [workspaceProfile, setWorkspaceProfile] = useState<AppWorkspaceProfile | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  const refresh = useCallback(async (showRefreshing = false) => {
    if (showRefreshing) setRefreshing(true);
    try {
      const syncCount = await getPendingSyncCount();
      setWorkspaceProfile(await getWorkspaceProfile());
      setPendingSync(syncCount);

      if (connectivity.isOnline && authStore.isAuthenticated) {
        setSummary(await api.dashboard());
        setError(null);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Dashboard unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }, [connectivity.isOnline]);

  useFocusEffect(
    useCallback(() => {
      void refresh();
    }, [refresh]),
  );

  const unlisted = summary?.unlistedInventoryCount ?? 0;
  const stale = summary?.staleInventoryCount ?? 0;

  return (
    <Screen
      refreshing={refreshing}
      onRefresh={() => void refresh(true)}
      edges={['top', 'left', 'right']}
    >
      <View style={styles.header}>
        <View style={{ flex: 1 }}>
          <Text variant="title">{workspaceProfile?.workspaceName ?? 'HaulScout'}</Text>
          <Text variant="caption">
            {workspaceProfile?.usageMode === 'team' ? 'Team resale command center' : 'Your resale command center'}
          </Text>
        </View>
        <IconButton icon="search-outline" label="Search" onPress={() => router.push('/search')} />
        <StatusPill
          label={connectivity.label}
          tone={connectivity.isOnline ? 'success' : 'warning'}
        />
      </View>

      {!connectivity.isOnline ? <OfflineBanner /> : null}
      {error ? <ErrorBanner message={error} onRetry={() => void refresh()} /> : null}

      {isLoading && !summary ? (
        <LoadingState message="Loading dashboard..." />
      ) : (
        <>
          <View style={styles.metricsRow}>
            <MetricCard
              label="Inventory"
              value={String(summary?.currentInventoryCount ?? 0)}
              icon="cube-outline"
              onPress={() => router.push('/(tabs)/inventory')}
            />
            <MetricCard
              label="Net Profit"
              value={formatMoney(summary?.netProfit)}
              icon="trending-up-outline"
              tone={summary?.netProfit && summary.netProfit > 0 ? 'success' : undefined}
            />
          </View>

          <View style={styles.metricsRow}>
            <MetricCard
              label="Cash Deployed"
              value={formatMoney(summary?.cashDeployed)}
              icon="wallet-outline"
              onPress={() => router.push('/budget')}
            />
            <MetricCard
              label="Active Listings"
              value={String(summary?.activeListingsCount ?? 0)}
              icon="pricetags-outline"
              onPress={() => router.push('/listing-queue')}
            />
          </View>

          <SectionHeader title="Needs Attention" />

          {workspaceProfile?.accountMode !== 'signedIn' ? (
            <ActionCard
              icon="person-circle-outline"
              title="Create an account to unlock sync later"
              subtitle="Keep using local mode, or connect auth when the provider is configured"
              tone="primary"
              onPress={() => router.push('/account')}
            />
          ) : null}

          {unlisted > 0 ? (
            <ActionCard
              icon="camera-outline"
              title={`${unlisted} item${unlisted > 1 ? 's' : ''} need listing`}
              subtitle="Research, photograph, and list unlisted items"
              tone="warning"
              onPress={() => router.push('/listing-queue')}
            />
          ) : null}

          {stale > 0 ? (
            <ActionCard
              icon="time-outline"
              title={`${stale} stale item${stale > 1 ? 's' : ''}`}
              subtitle="Items sitting too long without progress"
              tone="danger"
              onPress={() => router.push('/(tabs)/inventory')}
            />
          ) : null}

          {pendingSync > 0 ? (
            <ActionCard
              icon="cloud-upload-outline"
              title={`${pendingSync} item${pendingSync > 1 ? 's' : ''} waiting to sync`}
              subtitle="Offline changes ready to upload"
              tone="info"
              onPress={() => router.push('/settings')}
            />
          ) : null}

          {unlisted === 0 && stale === 0 && pendingSync === 0 ? (
            <Card>
              <View style={styles.allClear}>
                <Ionicons name="checkmark-circle" size={24} color={c.success} />
                <Text variant="bodyBold" color={c.success}>All clear</Text>
              </View>
              <Text variant="caption" style={styles.centered}>
                No pending items. Start scouting to find your next deal.
              </Text>
            </Card>
          ) : null}

          <SectionHeader title="Quick Actions" />

          <ActionCard
            icon="scan-outline"
            title="Scan Barcode"
            subtitle="Scan a barcode for instant marketplace pricing"
            tone="primary"
            onPress={() => router.push('/scanner')}
          />
          <ActionCard
            icon="checkbox-outline"
            title="Tasks"
            subtitle="View your to-do list"
            onPress={() => router.push('/tasks')}
          />
        </>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  metricsRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  allClear: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    justifyContent: 'center',
    paddingTop: spacing.sm,
  },
  centered: {
    textAlign: 'center',
  },
});
