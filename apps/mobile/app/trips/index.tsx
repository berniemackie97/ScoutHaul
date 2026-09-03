import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { SkeletonList } from '../../src/components/Skeleton';
import { Screen } from '../../src/components/Screen';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import type { SourcingRouteType, SourcingTrip } from '../../src/domain/types';
import { formatMoney, parseOptionalNumber } from '../../src/utils/money';

const routeTypeOptions: { value: SourcingRouteType; label: string }[] = [
  { value: 'Mixed', label: 'Mixed' },
  { value: 'ThriftRun', label: 'Thrift' },
  { value: 'GarageSales', label: 'Garage' },
  { value: 'PickupRun', label: 'Pickup' },
  { value: 'AuctionPickup', label: 'Auction' },
];

export default function TripsScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const [trips, setTrips] = useState<SourcingTrip[]>([]);
  const [name, setName] = useState('');
  const [cashBudget, setCashBudget] = useState('');
  const [routeDate, setRouteDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [plannedStartTime, setPlannedStartTime] = useState('09:00');
  const [routeType, setRouteType] = useState<SourcingRouteType>('Mixed');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [showForm, setShowForm] = useState(false);

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      setTrips(await api.trips());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Trips unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  async function createTrip() {
    if (!name.trim()) {
      setError('Trip name is required.');
      return;
    }

    const plannedStartAt = parseDateTime(routeDate, plannedStartTime);
    if (!plannedStartAt) {
      setError('Use route date YYYY-MM-DD and start time HH:MM.');
      return;
    }

    try {
      await api.createTrip({
        name,
        startedAt: plannedStartAt.toISOString(),
        routeDate,
        plannedStartAt: plannedStartAt.toISOString(),
        routeType,
        cashBudget: parseOptionalNumber(cashBudget),
        notes: null,
      });
      setName('');
      setCashBudget('');
      setRouteDate(new Date().toISOString().slice(0, 10));
      setPlannedStartTime('09:00');
      setRouteType('Mixed');
      setShowForm(false);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to create trip.');
    }
  }

  async function deleteTrip(id: string) {
    try {
      await api.deleteTrip(id);
      setTrips((current) => current.filter((trip) => trip.id !== id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to delete route.');
    }
  }

  function confirmDeleteTrip(trip: SourcingTrip) {
    Alert.alert(
      'Delete route?',
      trip.name,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Delete', style: 'destructive', onPress: () => void deleteTrip(trip.id) },
      ],
    );
  }

  return (
    <Screen
      refreshing={refreshing}
      onRefresh={() => void load(true)}
      edges={['top', 'left', 'right']}
    >
      <View style={styles.header}>
        <Text variant="title">Sourcing Trips</Text>
        <Button
          label="New Trip"
          icon="add-outline"
          size="sm"
          onPress={() => setShowForm(!showForm)}
        />
      </View>

      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      {showForm ? (
        <Card>
          <Text variant="subtitle">New Trip</Text>
          <TextField
            label="Route name"
            value={name}
            onChangeText={setName}
            placeholder="Saturday thrift route"
          />
          <View style={styles.row}>
            <TextField
              label="Date"
              value={routeDate}
              onChangeText={setRouteDate}
              placeholder="2026-05-12"
              containerStyle={styles.flex}
            />
            <TextField
              label="Start"
              value={plannedStartTime}
              onChangeText={setPlannedStartTime}
              placeholder="09:00"
              containerStyle={styles.flex}
            />
          </View>
          <View style={styles.chips}>
            {routeTypeOptions.map((option) => (
              <Pressable
                key={option.value}
                accessibilityRole="button"
                accessibilityLabel={`Route type ${option.label}`}
                onPress={() => setRouteType(option.value)}
                style={[
                  styles.typeChip,
                  {
                    backgroundColor: routeType === option.value ? c.primary : c.surfaceAlt,
                    borderColor: routeType === option.value ? c.primary : c.border,
                  },
                ]}
              >
                <Text variant="caption" color={routeType === option.value ? c.textInverse : c.textSecondary}>
                  {option.label}
                </Text>
              </Pressable>
            ))}
          </View>
          <TextField
            label="Route budget"
            value={cashBudget}
            onChangeText={setCashBudget}
            keyboardType="decimal-pad"
            prefix="$"
            placeholder="0.00"
          />
          <View style={styles.formActions}>
            <Button label="Cancel" variant="ghost" size="sm" onPress={() => setShowForm(false)} />
            <Button label="Create Trip" size="sm" onPress={() => void createTrip()} />
          </View>
        </Card>
      ) : null}

      {isLoading ? (
        <SkeletonList count={3} />
      ) : trips.length === 0 ? (
        <EmptyState
          icon="map-outline"
          title="No trips yet"
          message="Create a sourcing trip for your next garage sale or thrift run."
          actionLabel="New Trip"
          onAction={() => setShowForm(true)}
        />
      ) : (
        trips.map((trip) => {
          const isActive = trip.status === 'Active';
          return (
            <Pressable
              key={trip.id}
              onPress={() => router.push(`/trips/${trip.id}`)}
              accessibilityRole="button"
              style={({ pressed }) => [
                styles.tripCard,
                { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm },
                pressed && { backgroundColor: c.surfaceAlt, opacity: 0.9 },
              ]}
            >
              <View style={styles.tripHeader}>
                <Text variant="bodyBold" numberOfLines={1} style={styles.tripName}>
                  {trip.name}
                </Text>
                <View style={styles.tripHeaderActions}>
                  <StatusPill label={trip.status} tone={isActive ? 'success' : trip.status === 'Paused' ? 'warning' : 'neutral'} size="sm" />
                  <Pressable
                    accessibilityRole="button"
                    accessibilityLabel={`Delete ${trip.name}`}
                    hitSlop={8}
                    onPress={() => confirmDeleteTrip(trip)}
                  >
                    <Ionicons name="trash-outline" size={18} color={c.danger} />
                  </Pressable>
                </View>
              </View>
              <View style={styles.tripMeta}>
                <View style={styles.tripStat}>
                  <Ionicons name="location-outline" size={14} color={c.textMuted} />
                  <Text variant="caption">{trip.stopCount} stops</Text>
                </View>
                <View style={styles.tripStat}>
                  <Ionicons name="cube-outline" size={14} color={c.textMuted} />
                  <Text variant="caption">{trip.itemCount} items</Text>
                </View>
                {trip.cashBudget ? (
                  <View style={styles.tripStat}>
                    <Ionicons name="wallet-outline" size={14} color={c.textMuted} />
                    <Text variant="caption">{formatMoney(trip.cashBudget)} budget</Text>
                  </View>
                ) : null}
                <View style={styles.tripStat}>
                  <Ionicons name="calendar-outline" size={14} color={c.textMuted} />
                  <Text variant="caption">{new Date(trip.plannedStartAt ?? trip.startedAt).toLocaleDateString()}</Text>
                </View>
              </View>
            </Pressable>
          );
        })
      )}
    </Screen>
  );
}

function parseDateTime(date: string, time: string): Date | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || !/^\d{1,2}:\d{2}$/.test(time)) {
    return null;
  }

  const parsed = new Date(`${date}T${time.padStart(5, '0')}:00`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  formActions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
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
  tripCard: {
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
    borderWidth: 1,
  },
  tripHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.sm,
  },
  tripName: {
    flex: 1,
  },
  tripHeaderActions: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  tripMeta: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.md,
  },
  tripStat: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
});
