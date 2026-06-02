import { Ionicons } from '@expo/vector-icons';
import * as Linking from 'expo-linking';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Divider } from '../src/components/Divider';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { ProgressBar } from '../src/components/ProgressBar';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import {
  enqueueOperation,
  getRouteSnapshot,
  saveRouteSnapshot,
  updateRouteSnapshot,
} from '../src/db/localStore';
import {
  formatMinutes,
  getBudgetGuidance,
  getCurrentStop,
  getScheduleInsight,
  isResolvedStop,
  orderedStops,
  sourceTypeLabel,
} from '../src/domain/routes';
import type { SourcingRouteDetail, SourcingTripStop } from '../src/domain/types';
import { useConnectivity } from '../src/hooks/useConnectivity';
import { formatMoney } from '../src/utils/money';

type MapProvider = 'google' | 'apple';

export default function ActiveRouteScreen() {
  const { tripId } = useLocalSearchParams<{ tripId: string }>();
  const c = useColors();
  const connectivity = useConnectivity();
  const [route, setRoute] = useState<SourcingRouteDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [now, setNow] = useState(() => new Date());
  const [completionWarningStopId, setCompletionWarningStopId] = useState<string | null>(null);

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    if (!tripId) return;
    void load();
  }, [tripId]);

  async function load() {
    if (!tripId) return;
    try {
      const detail = await api.route(tripId);
      setRoute(detail);
      await saveRouteSnapshot(detail);
      setError(null);
    } catch (e) {
      const cached = await getRouteSnapshot(tripId);
      if (cached) {
        setRoute(cached);
        setError('Offline route snapshot loaded. Changes will sync when service returns.');
      } else {
        setError(e instanceof Error ? e.message : 'Route unavailable.');
      }
    } finally {
      setIsLoading(false);
    }
  }

  async function startRoute() {
    if (!tripId) return;
    await routeAction(
      () => api.startRoute(tripId),
      `/api/sourcing-trips/${tripId}/start`,
      'POST',
      {},
      (current) => ({ ...current, trip: { ...current.trip, status: 'Active', startedAt: new Date().toISOString() } }),
    );
  }

  async function pauseRoute() {
    if (!tripId) return;
    await routeAction(
      () => api.pauseRoute(tripId),
      `/api/sourcing-trips/${tripId}/pause`,
      'POST',
      {},
      (current) => ({ ...current, trip: { ...current.trip, status: 'Paused' } }),
    );
  }

  async function completeRoute() {
    if (!tripId) return;
    await routeAction(
      () => api.completeRoute(tripId),
      `/api/sourcing-trips/${tripId}/complete-route`,
      'POST',
      {},
      (current) => recomputeSummary({
        ...current,
        trip: { ...current.trip, status: 'Completed', endedAt: new Date().toISOString() },
      }),
    );
  }

  async function openNavigation(stop: SourcingTripStop, provider: MapProvider) {
    if (!tripId) return;
    await routeAction(
      () => api.navigateStop(tripId, stop.id),
      `/api/sourcing-trips/${tripId}/stops/${stop.id}/navigate`,
      'POST',
      {},
      (current) => updateStop(current, stop.id, { status: 'Navigating' }),
    );
    const destination = encodeURIComponent(stop.address);
    const url = provider === 'google'
      ? `https://www.google.com/maps/dir/?api=1&destination=${destination}`
      : `http://maps.apple.com/?daddr=${destination}`;
    await Linking.openURL(url);
    setNotice('Navigation opened. When you arrive, return here and tap I\'m Here.');
  }

  async function markArrived(stop: SourcingTripStop) {
    if (!tripId) return;
    const arrivedAt = new Date().toISOString();
    await routeAction(
      () => api.arriveAtStop(tripId, stop.id),
      `/api/sourcing-trips/${tripId}/stops/${stop.id}/arrive`,
      'POST',
      {},
      (current) => updateStop(current, stop.id, {
        status: 'InProgress',
        actualArrivalAt: stop.actualArrivalAt ?? arrivedAt,
      }),
    );
    setNotice(null);
  }

  async function completeStop(stop: SourcingTripStop, force = false) {
    if (!tripId) return;
    const hasIncompleteRequired = stop.checklistItems.some((item) => item.isRequired && !item.isCompleted);
    if (hasIncompleteRequired && !force) {
      setCompletionWarningStopId(stop.id);
      return;
    }

    const departedAt = new Date().toISOString();
    await routeAction(
      () => api.completeStop(tripId, stop.id, force),
      `/api/sourcing-trips/${tripId}/stops/${stop.id}/complete`,
      'POST',
      { force },
      (current) => {
        const next = updateStop(current, stop.id, {
          status: 'Completed',
          actualArrivalAt: stop.actualArrivalAt ?? departedAt,
          actualDepartureAt: departedAt,
        });
        return recomputeSummary(next);
      },
    );
    setCompletionWarningStopId(null);
  }

  async function skipStop(stop: SourcingTripStop) {
    if (!tripId || !stop.canBeSkipped) return;
    await routeAction(
      () => api.skipStop(tripId, stop.id),
      `/api/sourcing-trips/${tripId}/stops/${stop.id}/skip`,
      'POST',
      { reason: 'Skipped from active route workflow' },
      (current) => recomputeSummary(updateStop(current, stop.id, { status: 'Skipped' })),
    );
  }

  async function toggleChecklist(stop: SourcingTripStop, checklistItemId: string, isCompleted: boolean) {
    if (!tripId) return;
    await routeAction(
      () => api.updateChecklistItem(tripId, stop.id, checklistItemId, isCompleted),
      `/api/sourcing-trips/${tripId}/stops/${stop.id}/checklist/${checklistItemId}`,
      'PATCH',
      { isCompleted },
      (current) => updateStopChecklist(current, stop.id, checklistItemId, isCompleted),
    );
  }

  async function routeAction(
    onlineAction: () => Promise<SourcingRouteDetail>,
    endpoint: string,
    method: 'POST' | 'PATCH',
    body: unknown,
    offlineUpdate: (route: SourcingRouteDetail) => SourcingRouteDetail,
  ) {
    if (!tripId) return;

    if (connectivity.isOnline) {
      try {
        const detail = await onlineAction();
        setRoute(detail);
        await saveRouteSnapshot(detail);
        setError(null);
        return;
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Route action failed. Saved locally instead.');
      }
    }

    const updated = await updateRouteSnapshot(tripId, (current) => offlineUpdate(current));
    if (updated) {
      setRoute(updated);
      await enqueueOperation('RouteAction', endpoint, method, body);
      setError('Offline change queued for sync.');
    }
  }

  const stops = useMemo(() => route ? orderedStops(route.stops) : [], [route]);
  const currentStop = route ? getCurrentStop(route) : null;
  const currentIndex = currentStop ? stops.findIndex((stop) => stop.id === currentStop.id) : -1;
  const nextStop = currentIndex >= 0 ? stops.slice(currentIndex + 1).find((stop) => !isResolvedStop(stop)) ?? null : null;
  const insight = route ? getScheduleInsight(route, now) : null;
  const budget = route ? getBudgetGuidance(route) : null;

  if (isLoading) {
    return <Screen scroll={false}><LoadingState message="Loading route..." /></Screen>;
  }

  if (!route) {
    return (
      <Screen scroll={false}>
        {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}
        <Text variant="body">Route not found.</Text>
      </Screen>
    );
  }

  const completedStops = route.summary.completedStopCount;
  const progress = route.summary.stopCount > 0 ? (completedStops + route.summary.skippedStopCount) / route.summary.stopCount : 0;

  return (
    <Screen edges={['top', 'left', 'right', 'bottom']}>
      <View style={styles.driverContainer}>
        {error ? <ErrorBanner message={error} /> : null}
        {notice ? <InlineNotice icon="navigate-outline" text={notice} /> : null}

        <View style={styles.topSection}>
          <View style={styles.topRow}>
            <View style={styles.titleBlock}>
              <Text variant="label" color={c.textMuted}>ACTIVE ROUTE</Text>
              <Text variant="title" numberOfLines={1}>{route.trip.name}</Text>
            </View>
            <StatusPill label={connectivity.label} tone={connectivity.isOnline ? 'success' : 'warning'} size="sm" />
          </View>
          <ProgressBar progress={progress} height={8} color={c.primary} />
          <Text variant="caption" color={c.textMuted}>
            {completedStops} of {route.summary.stopCount} stops complete
          </Text>
        </View>

        {route.trip.status !== 'Active' && route.trip.status !== 'Completed' ? (
          <Card>
            <Text variant="bodyBold">Route is {route.trip.status.toLowerCase()}.</Text>
            <Button label="Start Route" icon="play-outline" onPress={() => void startRoute()} />
          </Card>
        ) : null}

        {currentStop ? (
          <Card variant="elevated" style={styles.currentStopCard}>
            <View style={styles.currentHeader}>
              <View style={[styles.stopNumber, { backgroundColor: c.info }]}>
                <Text variant="bodyBold" color={c.textInverse}>{currentStop.plannedOrder}</Text>
              </View>
              <View style={styles.currentTitle}>
                <Text variant="label" color={c.textMuted}>CURRENT STOP</Text>
                <Text variant="subtitle" numberOfLines={2}>{currentStop.name}</Text>
                <Text variant="caption" color={c.textMuted} numberOfLines={2}>{currentStop.address}</Text>
              </View>
              <StatusPill label={currentStop.status} tone={currentStop.status === 'InProgress' ? 'success' : 'primary'} size="sm" />
            </View>

            <View style={styles.stopMeta}>
              <MetaItem icon="pricetag-outline" label={sourceTypeLabel(currentStop.sourceType)} />
              <MetaItem icon="timer-outline" label={`${currentStop.estimatedDurationMinutes} min`} />
              <MetaItem icon="flag-outline" label={currentStop.priority} />
              {currentStop.isTimeSensitive ? <MetaItem icon="alarm-outline" label="Time-sensitive" tone={c.warning} /> : null}
            </View>

            {currentStop.status === 'InProgress' ? (
              <TimerBlock stop={currentStop} now={now} />
            ) : null}

            <View style={styles.primaryActions}>
              {currentStop.status === 'InProgress' ? (
                <Button label="Complete Stop" icon="checkmark-circle-outline" size="lg" fullWidth onPress={() => void completeStop(currentStop)} />
              ) : currentStop.status === 'Navigating' ? (
                <Button label="I'm Here" icon="location-outline" size="lg" fullWidth onPress={() => void markArrived(currentStop)} />
              ) : (
                <>
                  <Button label="Navigate" icon="navigate-outline" size="lg" fullWidth onPress={() => void openNavigation(currentStop, 'google')} />
                  <View style={styles.mapRow}>
                    <Button label="Google Maps" icon="map-outline" variant="secondary" onPress={() => void openNavigation(currentStop, 'google')} />
                    <Button label="Apple Maps" icon="map-outline" variant="secondary" onPress={() => void openNavigation(currentStop, 'apple')} />
                  </View>
                </>
              )}
              {currentStop.canBeSkipped && currentStop.status !== 'InProgress' ? (
                <Button label="Skip Optional Stop" icon="play-skip-forward-outline" variant="ghost" onPress={() => void skipStop(currentStop)} />
              ) : null}
            </View>
          </Card>
        ) : (
          <RouteSummaryCard route={route} onComplete={() => void completeRoute()} />
        )}

        {completionWarningStopId && currentStop?.id === completionWarningStopId ? (
          <Card>
            <Text variant="bodyBold" color={c.warning}>Required checklist items are still open.</Text>
            <Text variant="caption" color={c.textMuted}>You can finish them now or complete the stop anyway.</Text>
            <Button label="Complete Anyway" icon="warning-outline" variant="secondary" onPress={() => void completeStop(currentStop, true)} />
          </Card>
        ) : null}

        {insight && budget ? (
          <View style={styles.statusGrid}>
            <Card style={styles.statusCard}>
              <Text variant="label">Schedule</Text>
              <Text variant="bodyBold" color={toneColor(insight.statusTone, c)}>{insight.statusText}</Text>
              <Text variant="caption" color={insight.atRiskStop ? c.danger : c.textMuted}>{insight.guidance}</Text>
            </Card>
            <Card style={styles.statusCard}>
              <Text variant="label">Budget</Text>
              <Text variant="bodyBold" color={toneColor(budget.tone, c)}>{formatMoney(route.summary.remainingBudget)}</Text>
              <ProgressBar progress={budget.ratio} color={toneColor(budget.tone, c)} height={6} />
              <Text variant="caption" color={c.textMuted}>{budget.text}</Text>
            </Card>
          </View>
        ) : null}

        {currentStop?.status === 'InProgress' ? (
          <ChecklistCard stop={currentStop} onToggle={(itemId, value) => void toggleChecklist(currentStop, itemId, value)} />
        ) : null}

        {currentStop ? (
          <View style={styles.shortcutGrid}>
            <DriverAction
              icon="scan-outline"
              label="Scout"
              color={c.primary}
              bg={c.primaryLight}
              onPress={() => router.push({
                pathname: '/scanner',
                params: { sourcingTripId: route.trip.id, sourcingTripStopId: currentStop.id },
              })}
            />
            <DriverAction
              icon="add-circle-outline"
              label="Purchase"
              color={c.success}
              bg={c.successLight}
              onPress={() => router.push({
                pathname: '/inventory/edit',
                params: { sourcingTripId: route.trip.id, sourcingTripStopId: currentStop.id },
              })}
            />
            <DriverAction
              icon="calculator-outline"
              label="Deal"
              color={c.info}
              bg={c.infoLight}
              onPress={() => router.push('/deal-analyzer')}
            />
            <DriverAction
              icon="checkbox-outline"
              label="Tasks"
              color={c.accent}
              bg={c.accentLight}
              onPress={() => router.push('/tasks')}
            />
          </View>
        ) : null}

        {nextStop ? (
          <Card>
            <Text variant="label">Next Stop</Text>
            <Text variant="bodyBold">{nextStop.plannedOrder}. {nextStop.name}</Text>
            <Text variant="caption" color={c.textMuted}>{nextStop.estimatedDurationMinutes} min · {sourceTypeLabel(nextStop.sourceType)}</Text>
          </Card>
        ) : null}

        <View style={styles.bottomBar}>
          {route.trip.status === 'Active' ? (
            <Button label="Pause Route" icon="pause-circle-outline" variant="secondary" fullWidth onPress={() => void pauseRoute()} />
          ) : null}
          <Button label="End Route" icon="stop-circle-outline" variant="danger" fullWidth onPress={() => void completeRoute()} />
        </View>
      </View>
    </Screen>
  );
}

function TimerBlock({ stop, now }: { stop: SourcingTripStop; now: Date }) {
  const c = useColors();
  const arrived = stop.actualArrivalAt ? new Date(stop.actualArrivalAt) : now;
  const elapsedMinutes = Math.max(0, (now.getTime() - arrived.getTime()) / 60000);
  const remaining = stop.estimatedDurationMinutes - elapsedMinutes;
  const isOver = remaining < 0;
  return (
    <View style={[styles.timerBlock, { backgroundColor: c.surfaceAlt }]}>
      <View>
        <Text variant="label">Elapsed</Text>
        <Text variant="metricSmall">{formatMinutes(elapsedMinutes)}</Text>
      </View>
      <View style={styles.timerRight}>
        <Text variant="label">{isOver ? 'Over Time' : 'Remaining'}</Text>
        <Text variant="metricSmall" color={isOver ? c.danger : c.success}>{formatMinutes(remaining)}</Text>
      </View>
    </View>
  );
}

function ChecklistCard({ stop, onToggle }: { stop: SourcingTripStop; onToggle: (id: string, value: boolean) => void }) {
  const c = useColors();
  const completed = stop.checklistItems.filter((item) => item.isCompleted).length;
  const progress = stop.checklistItems.length > 0 ? completed / stop.checklistItems.length : 1;
  return (
    <Card>
      <View style={styles.checklistHeader}>
        <View>
          <Text variant="label">Checklist</Text>
          <Text variant="caption" color={c.textMuted}>{completed} of {stop.checklistItems.length} complete</Text>
        </View>
        <StatusPill label={`${Math.round(progress * 100)}%`} tone={progress === 1 ? 'success' : 'neutral'} size="sm" />
      </View>
      <ProgressBar progress={progress} color={progress === 1 ? c.success : c.primary} height={6} />
      {stop.checklistItems.map((item, index) => (
        <View key={item.id}>
          {index > 0 ? <Divider /> : null}
          <Pressable
            onPress={() => onToggle(item.id, !item.isCompleted)}
            accessibilityRole="checkbox"
            accessibilityState={{ checked: item.isCompleted }}
            style={styles.checklistRow}
          >
            <Ionicons
              name={item.isCompleted ? 'checkmark-circle' : 'ellipse-outline'}
              size={22}
              color={item.isCompleted ? c.success : c.textMuted}
            />
            <Text variant="body" style={styles.checkText}>{item.text}</Text>
            {item.isRequired ? <Text variant="label" color={c.warning}>Required</Text> : null}
          </Pressable>
        </View>
      ))}
    </Card>
  );
}

function RouteSummaryCard({ route, onComplete }: { route: SourcingRouteDetail; onComplete: () => void }) {
  const c = useColors();
  return (
    <Card variant="elevated">
      <Text variant="subtitle">Route Summary</Text>
      <SummaryRow label="Completed stops" value={`${route.summary.completedStopCount} of ${route.summary.stopCount}`} />
      <SummaryRow label="Skipped stops" value={`${route.summary.skippedStopCount}`} />
      <SummaryRow label="Planned vs actual" value={`${route.summary.plannedMinutes} min / ${route.summary.actualMinutes} min`} />
      <SummaryRow label="Schedule variance" value={`${route.summary.scheduleVarianceMinutes >= 0 ? '+' : ''}${route.summary.scheduleVarianceMinutes} min`} />
      <SummaryRow label="Total spend" value={formatMoney(route.summary.totalPurchaseCost)} />
      <SummaryRow label="Remaining budget" value={formatMoney(route.summary.remainingBudget)} />
      <SummaryRow label="Purchases" value={`${route.summary.purchaseCount}`} />
      <Text variant="caption" color={c.textMuted}>Follow-up tasks and inventory work remain available from Tasks and Inventory.</Text>
      {route.trip.status !== 'Completed' ? (
        <Button label="Complete Route" icon="checkmark-done-outline" onPress={onComplete} />
      ) : null}
    </Card>
  );
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.summaryRow}>
      <Text variant="caption">{label}</Text>
      <Text variant="bodyBold">{value}</Text>
    </View>
  );
}

function InlineNotice({ icon, text }: { icon: keyof typeof Ionicons.glyphMap; text: string }) {
  const c = useColors();
  return (
    <View style={[styles.inlineNotice, { backgroundColor: c.infoLight }]}>
      <Ionicons name={icon} size={18} color={c.info} />
      <Text variant="caption" color={c.info} style={styles.noticeText}>{text}</Text>
    </View>
  );
}

function MetaItem({ icon, label, tone }: { icon: keyof typeof Ionicons.glyphMap; label: string; tone?: string }) {
  const c = useColors();
  return (
    <View style={styles.metaItem}>
      <Ionicons name={icon} size={14} color={tone ?? c.textMuted} />
      <Text variant="caption" color={tone ?? c.textMuted}>{label}</Text>
    </View>
  );
}

function DriverAction({ icon, label, color, bg, onPress }: {
  icon: keyof typeof Ionicons.glyphMap; label: string; color: string; bg: string; onPress: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={label}
      style={({ pressed }) => [styles.actionTile, pressed && { opacity: 0.8 }]}
    >
      <View style={[styles.actionIcon, { backgroundColor: bg }]}>
        <Ionicons name={icon} size={24} color={color} />
      </View>
      <Text variant="caption">{label}</Text>
    </Pressable>
  );
}

function updateStop(route: SourcingRouteDetail, stopId: string, patch: Partial<SourcingTripStop>): SourcingRouteDetail {
  return {
    ...route,
    stops: route.stops.map((stop) => stop.id === stopId ? { ...stop, ...patch } : stop),
  };
}

function updateStopChecklist(route: SourcingRouteDetail, stopId: string, checklistItemId: string, isCompleted: boolean): SourcingRouteDetail {
  return {
    ...route,
    stops: route.stops.map((stop) => stop.id === stopId
      ? {
          ...stop,
          checklistItems: stop.checklistItems.map((item) => item.id === checklistItemId
            ? { ...item, isCompleted, completedAt: isCompleted ? new Date().toISOString() : null }
            : item),
        }
      : stop),
  };
}

function recomputeSummary(route: SourcingRouteDetail): SourcingRouteDetail {
  const activeStops = route.stops.filter((stop) => stop.status !== 'Cancelled');
  const completed = activeStops.filter((stop) => stop.status === 'Completed');
  const actualMinutes = completed.reduce((sum, stop) => {
    if (!stop.actualArrivalAt || !stop.actualDepartureAt) return sum;
    return sum + Math.max(0, Math.round((new Date(stop.actualDepartureAt).getTime() - new Date(stop.actualArrivalAt).getTime()) / 60000));
  }, 0);
  const completedPlanned = completed.reduce((sum, stop) => sum + stop.estimatedDurationMinutes, 0);
  const allResolved = activeStops.length > 0 && activeStops.every((stop) => isResolvedStop(stop));

  return {
    ...route,
    trip: allResolved ? { ...route.trip, status: 'Completed', endedAt: route.trip.endedAt ?? new Date().toISOString() } : route.trip,
    summary: {
      ...route.summary,
      completedStopCount: completed.length,
      skippedStopCount: activeStops.filter((stop) => stop.status === 'Skipped').length,
      plannedMinutes: activeStops.reduce((sum, stop) => sum + stop.estimatedDurationMinutes, 0),
      actualMinutes,
      scheduleVarianceMinutes: actualMinutes - completedPlanned,
      status: allResolved ? 'Completed' : route.summary.status,
    },
  };
}

function toneColor(tone: 'success' | 'warning' | 'danger' | 'neutral', c: ReturnType<typeof useColors>): string {
  switch (tone) {
    case 'success':
      return c.success;
    case 'warning':
      return c.warning;
    case 'danger':
      return c.danger;
    default:
      return c.text;
  }
}

const styles = StyleSheet.create({
  driverContainer: {
    flex: 1,
    gap: spacing.md,
  },
  topSection: {
    gap: spacing.xs,
    paddingTop: spacing.md,
  },
  topRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  titleBlock: {
    flex: 1,
    gap: spacing.xs,
  },
  inlineNotice: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    borderRadius: 8,
    padding: spacing.md,
  },
  noticeText: {
    flex: 1,
  },
  currentStopCard: {
    gap: spacing.md,
  },
  currentHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  stopNumber: {
    width: 40,
    height: 40,
    borderRadius: 20,
    alignItems: 'center',
    justifyContent: 'center',
  },
  currentTitle: {
    flex: 1,
    gap: spacing.xxs,
  },
  stopMeta: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.md,
  },
  metaItem: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xxs,
  },
  timerBlock: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    borderRadius: 8,
    padding: spacing.md,
  },
  timerRight: {
    alignItems: 'flex-end',
  },
  primaryActions: {
    gap: spacing.sm,
  },
  mapRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  statusGrid: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  statusCard: {
    flex: 1,
  },
  checklistHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  checklistRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.sm,
  },
  checkText: {
    flex: 1,
  },
  shortcutGrid: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  actionTile: {
    flex: 1,
    alignItems: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.sm,
  },
  actionIcon: {
    width: 44,
    height: 44,
    borderRadius: 22,
    alignItems: 'center',
    justifyContent: 'center',
  },
  summaryRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: spacing.xs,
  },
  bottomBar: {
    marginTop: 'auto',
    gap: spacing.sm,
    paddingBottom: spacing.md,
  },
});
