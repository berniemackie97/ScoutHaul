import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { SkeletonList } from '../../src/components/Skeleton';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import type { SourcingTrip } from '../../src/domain/types';
import { formatMoney, parseOptionalNumber } from '../../src/utils/money';

export default function TripsScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const [trips, setTrips] = useState<SourcingTrip[]>([]);
  const [name, setName] = useState('');
  const [cashBudget, setCashBudget] = useState('');
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
    try {
      await api.createTrip({
        name,
        cashBudget: parseOptionalNumber(cashBudget),
        notes: null,
      });
      setName('');
      setCashBudget('');
      setShowForm(false);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to create trip.');
    }
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
            label="Trip name"
            value={name}
            onChangeText={setName}
            placeholder="Saturday thrift route"
          />
          <TextField
            label="Cash budget"
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
          const isActive = !trip.endedAt;
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
                {isActive ? <StatusPill label="Active" tone="success" size="sm" /> : null}
              </View>
              <View style={styles.tripMeta}>
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
                  <Text variant="caption">{new Date(trip.startedAt).toLocaleDateString()}</Text>
                </View>
              </View>
            </Pressable>
          );
        })
      )}
    </Screen>
  );
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
