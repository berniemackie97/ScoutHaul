import { useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { ProgressBar } from '../src/components/ProgressBar';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import type { BudgetSummary } from '../src/domain/types';
import { formatMoney, parseOptionalNumber } from '../src/utils/money';

export default function BudgetScreen() {
  const c = useColors();
  const [summary, setSummary] = useState<BudgetSummary | null>(null);
  const [description, setDescription] = useState('');
  const [amount, setAmount] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      setSummary(await api.budgetSummary());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Budget unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  async function addExpense() {
    const parsed = parseOptionalNumber(amount);
    if (!description.trim() || parsed === null || parsed <= 0) {
      setError('Description and positive amount required.');
      return;
    }

    try {
      await api.addExpense({
        category: 'Other',
        description,
        amount: parsed,
        occurredAt: new Date().toISOString(),
      });
      setDescription('');
      setAmount('');
      setError(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to save expense.');
    }
  }

  const starting = summary?.startingAmount ?? 0;
  const remaining = summary?.remainingAmount ?? summary?.currentAmount ?? 0;
  const spent = starting > 0 ? starting - remaining : 0;
  const spentRatio = starting > 0 ? spent / starting : 0;
  const isOverBudget = remaining < 0;

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      {isLoading ? (
        <LoadingState message="Loading budget..." />
      ) : (
        <>
          <Card>
            <Text variant="label">Budget Remaining</Text>
            <Text
              variant="hero"
              color={isOverBudget ? c.danger : remaining < starting * 0.2 ? c.warning : c.text}
            >
              {formatMoney(remaining)}
            </Text>
            {starting > 0 ? (
              <>
                <ProgressBar
                  progress={spentRatio}
                  color={isOverBudget ? c.danger : spentRatio > 0.8 ? c.warning : c.primary}
                  height={10}
                />
                <View style={styles.budgetMeta}>
                  <Text variant="caption">
                    {formatMoney(spent)} of {formatMoney(starting)} spent
                  </Text>
                </View>
              </>
            ) : null}
            {isOverBudget ? (
              <Text variant="caption" color={c.danger}>
                Over budget by {formatMoney(Math.abs(remaining))}
              </Text>
            ) : null}
          </Card>

          <View style={styles.summaryRow}>
            <SummaryItem label="Total Expenses" value={formatMoney(summary?.totalExpenses)} />
            <SummaryItem label="Inventory Cost" value={formatMoney(summary?.inventoryPurchases)} />
          </View>

          <SectionHeader title="Add Expense" />
          <Card>
            <TextField
              label="Description"
              value={description}
              onChangeText={setDescription}
              placeholder="Gas, supplies, parking, etc."
            />
            <TextField
              label="Amount"
              value={amount}
              onChangeText={setAmount}
              keyboardType="decimal-pad"
              prefix="$"
              placeholder="0.00"
            />
            <Button
              label="Save Expense"
              icon="add-outline"
              fullWidth
              onPress={() => void addExpense()}
            />
          </Card>
        </>
      )}
    </Screen>
  );
}

function SummaryItem({ label, value }: { label: string; value: string }) {
  return (
    <Card style={styles.summaryCard}>
      <Text variant="label">{label}</Text>
      <Text variant="metricSmall">{value}</Text>
    </Card>
  );
}

const styles = StyleSheet.create({
  budgetMeta: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  summaryRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  summaryCard: {
    flex: 1,
  },
});
