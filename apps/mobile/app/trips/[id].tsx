import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { Divider } from '../../src/components/Divider';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { ProgressBar } from '../../src/components/ProgressBar';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme, useColors } from '../../src/context/ThemeContext';
import { priorityOptions, sourceTypeLabel, sourceTypeOptions } from '../../src/domain/routes';
import type {
  SourcingRouteDetail,
  SourcingStopPriority,
  SourcingStopSourceType,
  SourcingTripStop,
} from '../../src/domain/types';
import { formatMoney, parseOptionalNumber } from '../../src/utils/money';

export default function TripDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { colors: c } = useAppTheme();
  const [route, setRoute] = useState<SourcingRouteDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [showStopForm, setShowStopForm] = useState(false);
  const [stopName, setStopName] = useState('');
  const [address, setAddress] = useState('');
  const [sourceType, setSourceType] = useState<SourcingStopSourceType>('GarageSale');
  const [duration, setDuration] = useState('30');
  const [budgetCap, setBudgetCap] = useState('');
  const [priority, setPriority] = useState<SourcingStopPriority>('Medium');
  const [plannedArrival, setPlannedArrival] = useState('');
  const [windowEnd, setWindowEnd] = useState('');
  const [isTimeSensitive, setIsTimeSensitive] = useState(false);
  const [canBeSkipped, setCanBeSkipped] = useState(true);
  const [notes, setNotes] = useState('');

  async function load() {
    if (!id) return;
    try {
      setRoute(await api.route(id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Route unavailable.');
    } finally {
      setIsLoading(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, [id]));

  async function createStop() {
    if (!id || !route) return;
    if (!stopName.trim() || !address.trim()) {
      setError('Stop name and address are required.');
      return;
    }

    const estimatedDurationMinutes = Number(duration);
    if (!Number.isFinite(estimatedDurationMinutes) || estimatedDurationMinutes <= 0) {
      setError('Estimated duration must be a positive number of minutes.');
      return;
    }

    try {
      await api.createRouteStop(id, {
        name: stopName,
        sourceType,
        address,
        notes: notes || null,
        phone: null,
        url: null,
        plannedOrder: null,
        plannedArrivalAt: parseRouteTime(route, plannedArrival),
        estimatedDurationMinutes,
        priority,
        isTimeSensitive,
        timeWindowStartAt: null,
        timeWindowEndAt: parseRouteTime(route, windowEnd),
        canBeSkipped,
        budgetCap: parseOptionalNumber(budgetCap),
      });
      resetStopForm();
      setShowStopForm(false);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to add stop.');
    }
  }

  function resetStopForm() {
    setStopName('');
    setAddress('');
    setSourceType('GarageSale');
    setDuration('30');
    setBudgetCap('');
    setPriority('Medium');
    setPlannedArrival('');
    setWindowEnd('');
    setIsTimeSensitive(false);
    setCanBeSkipped(true);
    setNotes('');
  }

  async function reorder(stop: SourcingTripStop, direction: -1 | 1) {
    if (!route || !id) return;
    const stops = [...route.stops].sort((a, b) => a.plannedOrder - b.plannedOrder);
    const index = stops.findIndex((item) => item.id === stop.id);
    const target = index + direction;
    if (target < 0 || target >= stops.length) return;
    const next = [...stops];
    [next[index], next[target]] = [next[target], next[index]];

    try {
      setRoute(await api.reorderRouteStops(id, next.map((item) => item.id)));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to reorder stops.');
    }
  }

  async function applyTemplate(stopId: string) {
    if (!id) return;
    try {
      await api.applyStopChecklistTemplate(id, stopId);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to apply checklist template.');
    }
  }

  async function startRoute() {
    if (!id) return;
    try {
      await api.startRoute(id);
      router.push({ pathname: '/active-route', params: { tripId: id } });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to start route.');
    }
  }

  if (isLoading) {
    return (
      <Screen>
        <LoadingState message="Loading route..." />
      </Screen>
    );
  }

  if (!route) {
    return (
      <Screen>
        {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}
        <Text variant="body">Route not found.</Text>
      </Screen>
    );
  }

  const hasBudget = route.summary.cashBudget !== null && route.summary.cashBudget !== undefined && route.summary.cashBudget > 0;
  const budgetRatio = hasBudget ? route.summary.totalPurchaseCost / route.summary.cashBudget! : 0;
  const isOverBudget = route.summary.remainingBudget < 0;
  const sortedStops = [...route.stops].sort((a, b) => a.plannedOrder - b.plannedOrder);

  return (
    <Screen>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text variant="title">{route.trip.name}</Text>
          <Text variant="caption" color={c.textMuted}>
            {new Date(route.trip.plannedStartAt ?? route.trip.startedAt).toLocaleString()}
          </Text>
        </View>
        <StatusPill label={route.trip.status} tone={route.trip.status === 'Active' ? 'success' : 'neutral'} />
      </View>

      {hasBudget ? (
        <Card>
          <View style={styles.budgetRow}>
            <View>
              <Text variant="label">Route Budget</Text>
              <Text
                variant="hero"
                color={isOverBudget ? c.danger : route.summary.remainingBudget < (route.summary.cashBudget ?? 0) * 0.2 ? c.warning : c.text}
              >
                {formatMoney(route.summary.remainingBudget)}
              </Text>
              <Text variant="caption">remaining</Text>
            </View>
            <View style={styles.budgetRight}>
              <Text variant="bodyBold">{formatMoney(route.summary.totalPurchaseCost)}</Text>
              <Text variant="caption" color={c.textMuted}>spent</Text>
            </View>
          </View>
          <ProgressBar progress={budgetRatio} color={isOverBudget ? c.danger : budgetRatio > 0.8 ? c.warning : c.primary} height={8} />
        </Card>
      ) : null}

      <Card>
        <SectionHeader
          title="Stops"
          trailing={
            <Pressable onPress={() => setShowStopForm((value) => !value)} hitSlop={8}>
              <Text variant="caption" color={c.primary}>{showStopForm ? 'Close' : 'Add stop'}</Text>
            </Pressable>
          }
        />
        {showStopForm ? (
          <View style={styles.stopForm}>
            <TextField label="Stop name" value={stopName} onChangeText={setStopName} placeholder="Main Street garage sale" />
            <TextField label="Address" value={address} onChangeText={setAddress} placeholder="123 Main St, Springfield" />
            <SegmentedOptions
              label="Source type"
              options={sourceTypeOptions}
              value={sourceType}
              onChange={setSourceType}
            />
            <View style={styles.row}>
              <TextField label="Minutes" value={duration} onChangeText={setDuration} keyboardType="number-pad" containerStyle={styles.flex} />
              <TextField label="Budget" value={budgetCap} onChangeText={setBudgetCap} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
            </View>
            <View style={styles.row}>
              <TextField label="Arrival HH:MM" value={plannedArrival} onChangeText={setPlannedArrival} placeholder="10:15" containerStyle={styles.flex} />
              <TextField label="Window end" value={windowEnd} onChangeText={setWindowEnd} placeholder="11:00" containerStyle={styles.flex} />
            </View>
            <SegmentedOptions label="Priority" options={priorityOptions} value={priority} onChange={setPriority} />
            <View style={styles.toggleRow}>
              <ToggleChip label="Time-sensitive" selected={isTimeSensitive} onPress={() => setIsTimeSensitive((value) => !value)} />
              <ToggleChip label="Can skip" selected={canBeSkipped} onPress={() => setCanBeSkipped((value) => !value)} />
            </View>
            <TextField label="Notes" value={notes} onChangeText={setNotes} multiline placeholder="Seller details, parking, items to ask about" />
            <Button label="Add Stop" icon="add-outline" onPress={() => void createStop()} />
          </View>
        ) : null}

        {sortedStops.length === 0 ? (
          <Text variant="body" color={c.textMuted}>Add at least one sourcing stop before starting the route.</Text>
        ) : (
          sortedStops.map((stop, index) => (
            <View key={stop.id}>
              {index > 0 ? <Divider /> : null}
              <View style={styles.stopRow}>
                <View style={styles.orderBadge}>
                  <Text variant="bodyBold" color={c.primary}>{stop.plannedOrder}</Text>
                </View>
                <View style={styles.stopInfo}>
                  <View style={styles.stopTitleRow}>
                    <Text variant="bodyBold" numberOfLines={1} style={styles.stopName}>{stop.name}</Text>
                    <StatusPill label={stop.priority} tone={stop.priority === 'Critical' || stop.priority === 'High' ? 'warning' : 'neutral'} size="sm" />
                  </View>
                  <Text variant="caption" color={c.textMuted} numberOfLines={1}>{stop.address}</Text>
                  <View style={styles.stopMeta}>
                    <Text variant="caption">{sourceTypeLabel(stop.sourceType)}</Text>
                    <Text variant="caption">{stop.estimatedDurationMinutes} min</Text>
                    {stop.isTimeSensitive ? <Text variant="caption" color={c.warning}>Time-sensitive</Text> : null}
                    {stop.budgetCap ? <Text variant="caption">{formatMoney(stop.budgetCap)}</Text> : null}
                  </View>
                  <Text variant="caption" color={c.textMuted}>
                    {stop.checklistItems.length} checklist items
                  </Text>
                </View>
                <View style={styles.stopActions}>
                  <Pressable onPress={() => void reorder(stop, -1)} hitSlop={8} disabled={index === 0}>
                    <Ionicons name="chevron-up" size={20} color={index === 0 ? c.border : c.textSecondary} />
                  </Pressable>
                  <Pressable onPress={() => void reorder(stop, 1)} hitSlop={8} disabled={index === sortedStops.length - 1}>
                    <Ionicons name="chevron-down" size={20} color={index === sortedStops.length - 1 ? c.border : c.textSecondary} />
                  </Pressable>
                  <Pressable onPress={() => void applyTemplate(stop.id)} hitSlop={8}>
                    <Ionicons name="checkbox-outline" size={20} color={c.primary} />
                  </Pressable>
                </View>
              </View>
            </View>
          ))
        )}
      </Card>

      <Card>
        <SectionHeader title="Route Summary" />
        <DetailRow label="Stops" value={`${route.summary.stopCount}`} />
        <DetailRow label="Planned time" value={`${route.summary.plannedMinutes} min`} />
        <DetailRow label="Purchases" value={`${route.summary.purchaseCount}`} />
        <DetailRow label="Total purchase cost" value={formatMoney(route.summary.totalPurchaseCost)} />
      </Card>

      <View style={styles.actions}>
        <Button
          label={route.trip.status === 'Active' || route.trip.status === 'Paused' ? 'Open Active Route' : 'Start Route'}
          icon="navigate-outline"
          size="lg"
          fullWidth
          disabled={sortedStops.length === 0}
          onPress={route.trip.status === 'Active' || route.trip.status === 'Paused'
            ? () => router.push({ pathname: '/active-route', params: { tripId: route.trip.id } })
            : () => void startRoute()}
        />
      </View>
    </Screen>
  );
}

function SegmentedOptions<T extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
}) {
  const c = useColors();
  return (
    <View style={styles.segmentGroup}>
      <Text variant="label">{label}</Text>
      <View style={styles.chips}>
        {options.map((option) => (
          <Pressable
            key={option.value}
            accessibilityRole="button"
            accessibilityLabel={`${label} ${option.label}`}
            onPress={() => onChange(option.value)}
            style={[
              styles.typeChip,
              {
                backgroundColor: value === option.value ? c.primary : c.surfaceAlt,
                borderColor: value === option.value ? c.primary : c.border,
              },
            ]}
          >
            <Text variant="caption" color={value === option.value ? c.textInverse : c.textSecondary}>
              {option.label}
            </Text>
          </Pressable>
        ))}
      </View>
    </View>
  );
}

function ToggleChip({ label, selected, onPress }: { label: string; selected: boolean; onPress: () => void }) {
  const c = useColors();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      style={[
        styles.toggleChip,
        {
          backgroundColor: selected ? c.primaryLight : c.surfaceAlt,
          borderColor: selected ? c.primary : c.border,
        },
      ]}
    >
      <Ionicons name={selected ? 'checkmark-circle' : 'ellipse-outline'} size={16} color={selected ? c.primary : c.textMuted} />
      <Text variant="caption" color={selected ? c.primary : c.textSecondary}>{label}</Text>
    </Pressable>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.detailRow}>
      <Text variant="caption">{label}</Text>
      <Text variant="bodyBold">{value}</Text>
    </View>
  );
}

function parseRouteTime(route: SourcingRouteDetail, value: string): string | null {
  if (!value.trim()) return null;
  if (!/^\d{1,2}:\d{2}$/.test(value.trim())) return null;
  const date = route.trip.routeDate ?? new Date(route.trip.plannedStartAt ?? route.trip.startedAt).toISOString().slice(0, 10);
  const parsed = new Date(`${date}T${value.trim().padStart(5, '0')}:00`);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  headerText: {
    flex: 1,
    gap: spacing.xs,
  },
  budgetRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  budgetRight: {
    alignItems: 'flex-end',
    justifyContent: 'center',
  },
  stopForm: {
    gap: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
  segmentGroup: {
    gap: spacing.xs,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  typeChip: {
    borderWidth: 1,
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  toggleRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  toggleChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    borderWidth: 1,
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  stopRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.sm,
  },
  orderBadge: {
    width: 34,
    height: 34,
    borderRadius: 17,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: 'rgba(37, 99, 235, 0.1)',
  },
  stopInfo: {
    flex: 1,
    gap: spacing.xs,
  },
  stopTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  stopName: {
    flex: 1,
  },
  stopMeta: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  stopActions: {
    alignItems: 'center',
    gap: spacing.xs,
  },
  detailRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: spacing.xs,
  },
  actions: {
    gap: spacing.sm,
  },
});
