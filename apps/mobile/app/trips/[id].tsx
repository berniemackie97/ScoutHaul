import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { Divider } from '../../src/components/Divider';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { ProgressBar } from '../../src/components/ProgressBar';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { Text } from '../../src/components/Text';
import { spacing } from '../../src/components/theme';
import { useColors } from '../../src/context/ThemeContext';
import type { SourcingTripSummary } from '../../src/domain/types';
import { formatMoney } from '../../src/utils/money';

export default function TripDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const c = useColors();
  const [summary, setSummary] = useState<SourcingTripSummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  async function load() {
    if (!id) return;
    try {
      setSummary(await api.tripSummary(id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Trip unavailable.');
    } finally {
      setIsLoading(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, [id]));

  if (isLoading) {
    return (
      <Screen>
        <LoadingState message="Loading trip..." />
      </Screen>
    );
  }

  if (!summary) {
    return (
      <Screen>
        {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}
        <Text variant="body">Trip not found.</Text>
      </Screen>
    );
  }

  const hasBudget = summary.cashBudget !== null && summary.cashBudget !== undefined && summary.cashBudget > 0;
  const budgetSpent = hasBudget ? summary.totalPurchaseCost : 0;
  const budgetRatio = hasBudget ? budgetSpent / summary.cashBudget! : 0;
  const isOverBudget = summary.remainingBudget < 0;

  return (
    <Screen>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      <Text variant="title">{summary.name}</Text>

      {hasBudget ? (
        <Card>
          <Text variant="label">Trip Budget</Text>
          <Text
            variant="hero"
            color={isOverBudget ? c.danger : summary.remainingBudget < (summary.cashBudget ?? 0) * 0.2 ? c.warning : c.text}
          >
            {formatMoney(summary.remainingBudget)}
          </Text>
          <Text variant="caption">remaining</Text>
          <ProgressBar
            progress={budgetRatio}
            color={isOverBudget ? c.danger : budgetRatio > 0.8 ? c.warning : c.primary}
            height={8}
          />
          <View style={styles.budgetMeta}>
            <Text variant="caption">{formatMoney(budgetSpent)} spent of {formatMoney(summary.cashBudget)}</Text>
          </View>
        </Card>
      ) : null}

      <Card>
        <SectionHeader title="Trip Summary" />
        <DetailRow label="Items scouted" value={String(summary.itemCount)} />
        <DetailRow label="Total purchase cost" value={formatMoney(summary.totalPurchaseCost)} />
        <DetailRow label="Total asking price" value={formatMoney(summary.totalAskingPrice)} />
        {hasBudget ? (
          <DetailRow label="Cash budget" value={formatMoney(summary.cashBudget)} />
        ) : null}
      </Card>

      <View style={styles.actions}>
        <Button
          label="Scout Item"
          icon="scan-outline"
          fullWidth
          onPress={() => router.push('/(tabs)/scout')}
        />
        <Button
          label="View Inventory"
          icon="cube-outline"
          variant="secondary"
          fullWidth
          onPress={() => router.push('/(tabs)/inventory')}
        />
      </View>
    </Screen>
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

const styles = StyleSheet.create({
  budgetMeta: {
    flexDirection: 'row',
    justifyContent: 'space-between',
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
