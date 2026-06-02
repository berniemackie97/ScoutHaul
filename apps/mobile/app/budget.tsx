import { Ionicons } from '@expo/vector-icons';
import { useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Divider } from '../src/components/Divider';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { ProgressBar } from '../src/components/ProgressBar';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import {
  getBudgetPlanBreakdown,
  getWorkspaceProfile,
  saveBudgetPlanBreakdown,
  type BudgetPlanBreakdown,
} from '../src/db/localStore';
import type { Budget, BudgetSummary, Expense, ExpenseCategory } from '../src/domain/types';
import { formatMoney, parseOptionalNumber } from '../src/utils/money';

type PlanKey = keyof Omit<BudgetPlanBreakdown, 'updatedAt'>;

const categoryOptions: { value: ExpenseCategory; label: string; icon: keyof typeof Ionicons.glyphMap }[] = [
  { value: 'InventoryPurchase', label: 'Inventory', icon: 'cube-outline' },
  { value: 'Fuel', label: 'Fuel', icon: 'car-outline' },
  { value: 'Supplies', label: 'Supplies', icon: 'construct-outline' },
  { value: 'Shipping', label: 'Shipping', icon: 'paper-plane-outline' },
  { value: 'PlatformFee', label: 'Fees', icon: 'card-outline' },
  { value: 'Other', label: 'Other', icon: 'ellipsis-horizontal-outline' },
];

const planFields: { key: PlanKey; label: string; category?: ExpenseCategory }[] = [
  { key: 'inventory', label: 'Inventory buys', category: 'InventoryPurchase' },
  { key: 'fuel', label: 'Fuel & travel', category: 'Fuel' },
  { key: 'supplies', label: 'Supplies', category: 'Supplies' },
  { key: 'shipping', label: 'Shipping', category: 'Shipping' },
  { key: 'fees', label: 'Platform fees', category: 'PlatformFee' },
  { key: 'reserve', label: 'Reserve' },
];

const emptyPlan: Record<PlanKey, string> = {
  inventory: '',
  fuel: '',
  supplies: '',
  shipping: '',
  fees: '',
  reserve: '',
};

export default function BudgetScreen() {
  const c = useColors();
  const [summary, setSummary] = useState<BudgetSummary | null>(null);
  const [currentBudget, setCurrentBudget] = useState<Budget | null>(null);
  const [expenses, setExpenses] = useState<Expense[]>([]);
  const [budgetName, setBudgetName] = useState('');
  const [budgetAmount, setBudgetAmount] = useState('');
  const [plan, setPlan] = useState<Record<PlanKey, string>>(emptyPlan);
  const [category, setCategory] = useState<ExpenseCategory>('Other');
  const [description, setDescription] = useState('');
  const [amount, setAmount] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      const [summaryResult, currentBudgetResult, expensesResult, planResult, profile] = await Promise.all([
        api.budgetSummary(),
        api.currentBudget().catch(() => null),
        api.expenses().catch(() => []),
        getBudgetPlanBreakdown(),
        getWorkspaceProfile(),
      ]);

      setSummary(summaryResult);
      setCurrentBudget(currentBudgetResult);
      setExpenses(expensesResult);
      setBudgetName(currentBudgetResult?.name ?? 'Monthly operating budget');
      setBudgetAmount(String(currentBudgetResult?.startingAmount ?? profile?.monthlySourcingBudget ?? ''));
      setPlan(formatPlanForInputs(planResult));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Budget unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  async function saveBudgetPlan() {
    const parsedAmount = parseOptionalNumber(budgetAmount);
    if (!budgetName.trim() || parsedAmount === null || parsedAmount < 0) {
      setError('Budget name and non-negative amount are required.');
      return;
    }

    try {
      await api.createBudget({
        name: budgetName,
        startingAmount: parsedAmount,
        startsAt: new Date().toISOString(),
        endsAt: endOfMonthIso(),
      });
      await saveBudgetPlanBreakdown(parsePlan(plan));
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to save budget plan.');
    }
  }

  async function addExpense() {
    const parsed = parseOptionalNumber(amount);
    if (!description.trim() || parsed === null || parsed <= 0) {
      setError('Description and positive amount required.');
      return;
    }

    try {
      await api.addExpense({
        inventoryItemId: null,
        sourcingTripId: null,
        sourcingTripStopId: null,
        category,
        description,
        amount: parsed,
        occurredAt: new Date().toISOString(),
      });
      setDescription('');
      setAmount('');
      setCategory('Other');
      setError(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to save expense.');
    }
  }

  async function deleteCurrentBudget() {
    if (!currentBudget) return;
    try {
      await api.deleteBudget(currentBudget.id);
      setCurrentBudget(null);
      setError(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to delete budget.');
    }
  }

  function confirmDeleteCurrentBudget() {
    if (!currentBudget) return;
    Alert.alert('Delete active budget?', currentBudget.name, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Delete', style: 'destructive', onPress: () => void deleteCurrentBudget() },
    ]);
  }

  async function deleteExpense(expense: Expense) {
    try {
      await api.deleteExpense(expense.id);
      setExpenses((current) => current.filter((item) => item.id !== expense.id));
      setError(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to delete expense.');
    }
  }

  function confirmDeleteExpense(expense: Expense) {
    Alert.alert('Delete expense?', expense.description, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Delete', style: 'destructive', onPress: () => void deleteExpense(expense) },
    ]);
  }

  const starting = summary?.startingAmount ?? 0;
  const remaining = summary?.remainingAmount ?? summary?.currentAmount ?? 0;
  const spent = Math.max(0, starting - remaining);
  const spentRatio = starting > 0 ? spent / starting : 0;
  const isOverBudget = remaining < 0;
  const parsedPlan = useMemo(() => parsePlan(plan), [plan]);
  const plannedTotal = Object.values(parsedPlan).reduce((sum, value) => sum + value, 0);
  const plannedRatio = starting > 0 ? plannedTotal / starting : 0;
  const spendByCategory = useMemo(() => {
    const totals = new Map<ExpenseCategory, number>();
    for (const expense of expenses) {
      totals.set(expense.category, (totals.get(expense.category) ?? 0) + expense.amount);
    }
    return totals;
  }, [expenses]);

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      {isLoading ? (
        <LoadingState message="Loading budget..." />
      ) : (
        <>
          <View style={styles.header}>
            <View style={styles.headerText}>
              <Text variant="title">Budget Planning</Text>
              <Text variant="caption">Operating budget, allocation, spend, and runway.</Text>
            </View>
            <StatusPill label={currentBudget ? 'Active' : 'Not set'} tone={currentBudget ? 'success' : 'warning'} />
          </View>

          <Card variant="elevated">
            <Text variant="label">{currentBudget?.name ?? 'No active budget'}</Text>
            <Text
              variant="hero"
              color={isOverBudget ? c.danger : remaining < starting * 0.2 ? c.warning : c.text}
            >
              {formatMoney(remaining)}
            </Text>
            <Text variant="caption">remaining from {formatMoney(starting)}</Text>
            {starting > 0 ? (
              <ProgressBar progress={spentRatio} color={isOverBudget ? c.danger : spentRatio > 0.8 ? c.warning : c.primary} height={10} />
            ) : null}
            <View style={styles.healthGrid}>
              <HealthMetric label="Spent" value={formatMoney(spent)} />
              <HealthMetric label="Allocated" value={formatMoney(plannedTotal)} />
              <HealthMetric label="Unplanned" value={formatMoney(Math.max(0, starting - plannedTotal))} />
            </View>
          </Card>

          <SectionHeader title="Plan" />
          <Card>
            <TextField label="Budget name" value={budgetName} onChangeText={setBudgetName} placeholder="May operating budget" />
            <TextField label="Total budget" value={budgetAmount} onChangeText={setBudgetAmount} keyboardType="decimal-pad" prefix="$" placeholder="0.00" />
            <Divider />
            <View style={styles.planHeader}>
              <View>
                <Text variant="bodyBold">Allocation Breakdown</Text>
                <Text variant="caption" color={plannedRatio > 1 ? c.danger : c.textMuted}>
                  {formatMoney(plannedTotal)} planned {starting > 0 ? `of ${formatMoney(starting)}` : ''}
                </Text>
              </View>
              <StatusPill label={plannedRatio > 1 ? 'Overplanned' : 'Balanced'} tone={plannedRatio > 1 ? 'danger' : 'success'} size="sm" />
            </View>
            {planFields.map((field) => (
              <TextField
                key={field.key}
                label={field.label}
                value={plan[field.key]}
                onChangeText={(value) => setPlan((current) => ({ ...current, [field.key]: value }))}
                keyboardType="decimal-pad"
                prefix="$"
                placeholder="0.00"
              />
            ))}
            <View style={styles.buttonRow}>
              <Button label="Save Active Budget" icon="save-outline" onPress={() => void saveBudgetPlan()} />
              {currentBudget ? (
                <Button label="Delete" icon="trash-outline" variant="danger" onPress={confirmDeleteCurrentBudget} />
              ) : null}
            </View>
          </Card>

          <SectionHeader title="Spend Breakdown" />
          <Card>
            {planFields.map((field, index) => {
              const planned = parsedPlan[field.key];
              const actual = field.category ? spendByCategory.get(field.category) ?? 0 : 0;
              const ratio = planned > 0 ? actual / planned : 0;
              return (
                <View key={field.key}>
                  {index > 0 ? <Divider spacing={spacing.sm} /> : null}
                  <View style={styles.breakdownRow}>
                    <View style={styles.breakdownText}>
                      <Text variant="bodyBold">{field.label}</Text>
                      <Text variant="caption">{formatMoney(actual)} spent of {formatMoney(planned)}</Text>
                    </View>
                    <Text variant="caption" color={ratio > 1 ? c.danger : c.textMuted}>{Math.round(ratio * 100)}%</Text>
                  </View>
                  <ProgressBar progress={ratio} color={ratio > 1 ? c.danger : ratio > 0.8 ? c.warning : c.primary} height={6} />
                </View>
              );
            })}
          </Card>

          <SectionHeader title="Add Expense" />
          <Card>
            <View style={styles.categoryGrid}>
              {categoryOptions.map((option) => {
                const selected = option.value === category;
                return (
                  <Pressable
                    key={option.value}
                    accessibilityRole="button"
                    accessibilityState={{ selected }}
                    accessibilityLabel={`Expense category ${option.label}`}
                    onPress={() => setCategory(option.value)}
                    style={[
                      styles.categoryChip,
                      {
                        backgroundColor: selected ? c.primaryLight : c.surfaceAlt,
                        borderColor: selected ? c.primary : c.border,
                      },
                    ]}
                  >
                    <Ionicons name={option.icon} size={16} color={selected ? c.primary : c.textMuted} />
                    <Text variant="caption" color={selected ? c.primary : c.textSecondary}>{option.label}</Text>
                  </Pressable>
                );
              })}
            </View>
            <TextField label="Description" value={description} onChangeText={setDescription} placeholder="Gas, supplies, parking, etc." />
            <TextField label="Amount" value={amount} onChangeText={setAmount} keyboardType="decimal-pad" prefix="$" placeholder="0.00" />
            <Button label="Save Expense" icon="add-outline" fullWidth onPress={() => void addExpense()} />
          </Card>

          <SectionHeader title="Recent Expenses" />
          {expenses.slice(0, 8).length === 0 ? (
            <Card>
              <Text variant="caption">No expenses recorded yet.</Text>
            </Card>
          ) : (
            expenses.slice(0, 8).map((expense) => (
              <Card key={expense.id}>
                <View style={styles.expenseRow}>
                  <View style={styles.expenseText}>
                    <Text variant="bodyBold">{expense.description}</Text>
                    <Text variant="caption">{expense.category} · {new Date(expense.occurredAt).toLocaleDateString()}</Text>
                  </View>
                  <View style={styles.expenseActions}>
                    <Text variant="bodyBold">{formatMoney(expense.amount)}</Text>
                    <Pressable
                      accessibilityRole="button"
                      accessibilityLabel={`Delete ${expense.description}`}
                      hitSlop={8}
                      onPress={() => confirmDeleteExpense(expense)}
                    >
                      <Ionicons name="trash-outline" size={18} color={c.danger} />
                    </Pressable>
                  </View>
                </View>
              </Card>
            ))
          )}
        </>
      )}
    </Screen>
  );
}

function HealthMetric({ label, value }: { label: string; value: string }) {
  const c = useColors();
  return (
    <View style={[styles.healthMetric, { backgroundColor: c.surfaceAlt }]}>
      <Text variant="label">{label}</Text>
      <Text variant="bodyBold">{value}</Text>
    </View>
  );
}

function parsePlan(plan: Record<PlanKey, string>): Omit<BudgetPlanBreakdown, 'updatedAt'> {
  return {
    inventory: parseOptionalNumber(plan.inventory) ?? 0,
    fuel: parseOptionalNumber(plan.fuel) ?? 0,
    supplies: parseOptionalNumber(plan.supplies) ?? 0,
    shipping: parseOptionalNumber(plan.shipping) ?? 0,
    fees: parseOptionalNumber(plan.fees) ?? 0,
    reserve: parseOptionalNumber(plan.reserve) ?? 0,
  };
}

function formatPlanForInputs(plan: BudgetPlanBreakdown | null): Record<PlanKey, string> {
  if (!plan) return emptyPlan;
  return {
    inventory: valueToInput(plan.inventory),
    fuel: valueToInput(plan.fuel),
    supplies: valueToInput(plan.supplies),
    shipping: valueToInput(plan.shipping),
    fees: valueToInput(plan.fees),
    reserve: valueToInput(plan.reserve),
  };
}

function valueToInput(value: number): string {
  return value > 0 ? String(value) : '';
}

function endOfMonthIso(): string {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth() + 1, 0, 23, 59, 59).toISOString();
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
  healthGrid: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  healthMetric: {
    flex: 1,
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.xs,
  },
  planHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.md,
  },
  breakdownRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.md,
  },
  breakdownText: {
    flex: 1,
    gap: spacing.xs,
  },
  categoryGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  categoryChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    borderWidth: 1,
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  expenseRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.md,
  },
  expenseText: {
    flex: 1,
    gap: spacing.xs,
  },
  buttonRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  expenseActions: {
    alignItems: 'flex-end',
    gap: spacing.xs,
  },
});
