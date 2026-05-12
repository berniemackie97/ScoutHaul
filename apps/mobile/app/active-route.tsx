import { Ionicons } from '@expo/vector-icons';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { ProgressBar } from '../src/components/ProgressBar';
import { Screen } from '../src/components/Screen';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import type { SourcingTripSummary } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

export default function ActiveRouteScreen() {
  const { tripId } = useLocalSearchParams<{ tripId: string }>();
  const c = useColors();
  const [summary, setSummary] = useState<SourcingTripSummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    if (!tripId) return;
    void (async () => {
      try {
        setSummary(await api.tripSummary(tripId));
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Trip unavailable.');
      } finally {
        setIsLoading(false);
      }
    })();
  }, [tripId]);

  if (isLoading) {
    return <Screen scroll={false}><LoadingState message="Loading route..." /></Screen>;
  }

  if (!summary) {
    return (
      <Screen scroll={false}>
        {error ? <ErrorBanner message={error} /> : null}
        <Text variant="body">Trip not found.</Text>
      </Screen>
    );
  }

  const hasBudget = (summary.cashBudget ?? 0) > 0;
  const budgetSpent = summary.totalPurchaseCost;
  const budgetRatio = hasBudget ? budgetSpent / summary.cashBudget! : 0;
  const remaining = summary.remainingBudget;
  const isOverBudget = remaining < 0;

  return (
    <Screen scroll={false} edges={['top', 'left', 'right', 'bottom']}>
      <View style={styles.driverContainer}>
        {error ? <ErrorBanner message={error} /> : null}

        <View style={styles.topSection}>
          <Text variant="label" color={c.textMuted}>ACTIVE ROUTE</Text>
          <Text variant="title">{summary.name}</Text>
          <Text variant="caption">{summary.itemCount} items scouted</Text>
        </View>

        {hasBudget ? (
          <Card>
            <View style={styles.budgetRow}>
              <View style={styles.budgetCol}>
                <Text variant="label">Remaining</Text>
                <Text
                  variant="metric"
                  color={isOverBudget ? c.danger : remaining < (summary.cashBudget ?? 0) * 0.2 ? c.warning : c.success}
                >
                  {formatMoney(remaining)}
                </Text>
              </View>
              <View style={styles.budgetCol}>
                <Text variant="label">Spent</Text>
                <Text variant="metricSmall">{formatMoney(budgetSpent)}</Text>
              </View>
            </View>
            <ProgressBar
              progress={budgetRatio}
              color={isOverBudget ? c.danger : budgetRatio > 0.8 ? c.warning : c.primary}
              height={10}
            />
            {isOverBudget ? (
              <Text variant="caption" color={c.danger}>Over budget by {formatMoney(Math.abs(remaining))}</Text>
            ) : null}
          </Card>
        ) : null}

        <View style={styles.driverActions}>
          <DriverAction
            icon="scan-outline"
            label="Scout Item"
            color={c.primary}
            bg={c.primaryLight}
            onPress={() => router.push('/(tabs)/scout')}
          />
          <DriverAction
            icon="add-circle-outline"
            label="Add Purchase"
            color={c.success}
            bg={c.successLight}
            onPress={() => router.push('/inventory/edit')}
          />
          <DriverAction
            icon="calculator-outline"
            label="Deal Check"
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

        <View style={styles.bottomBar}>
          <Button
            label="End Route"
            icon="stop-circle-outline"
            variant="danger"
            size="lg"
            fullWidth
            onPress={() => router.back()}
          />
        </View>
      </View>
    </Screen>
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
        <Ionicons name={icon} size={28} color={color} />
      </View>
      <Text variant="bodyBold">{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  driverContainer: {
    flex: 1,
    gap: spacing.lg,
  },
  topSection: {
    gap: spacing.xs,
    paddingTop: spacing.md,
  },
  budgetRow: {
    flexDirection: 'row',
    gap: spacing.xl,
  },
  budgetCol: {
    gap: spacing.xs,
  },
  driverActions: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.md,
  },
  actionTile: {
    width: '47%',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.lg,
  },
  actionIcon: {
    width: 56,
    height: 56,
    borderRadius: 28,
    alignItems: 'center',
    justifyContent: 'center',
  },
  bottomBar: {
    marginTop: 'auto',
    paddingBottom: spacing.lg,
  },
});
