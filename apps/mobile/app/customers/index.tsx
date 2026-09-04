import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import { Card } from '../../src/components/Card';
import { EmptyState } from '../../src/components/EmptyState';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { MetricCard } from '../../src/components/MetricCard';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { spacing } from '../../src/components/theme';
import { useColors } from '../../src/context/ThemeContext';
import { useApiResource } from '../../src/hooks/useApiResource';
import type { CustomerSummary } from '../../src/domain/types';
import { formatMoney } from '../../src/utils/money';

const shortDate = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date(value)) : '—';

export default function CustomersScreen() {
  const c = useColors();
  const [search, setSearch] = useState('');
  const [submittedSearch, setSubmittedSearch] = useState('');

  // The search term is the key. Changing it makes a new fetcher, which is the one thing that
  // triggers a reload — the previous version both re-ran the focus effect and called load() by
  // hand, so every search fetched twice.
  const fetchCustomers = useCallback(
    (signal: AbortSignal) => api.customers({ search: submittedSearch }, { signal }),
    [submittedSearch],
  );
  const { data, error, isLoading, isRefreshing, refresh, reload } = useApiResource(fetchCustomers);
  // useMemo below keys on this, so it must not be a fresh array on every render.
  const customers = useMemo(() => data ?? [], [data]);

  const metrics = useMemo(() => {
    const marketing = customers.filter((customer) => customer.acceptsMarketing).length;
    const credit = customers.reduce((sum, customer) => sum + (customer.storeCreditBalance ?? 0), 0);
    const spend = customers.reduce((sum, customer) => sum + (customer.lifetimeSpend ?? 0), 0);
    return { marketing, credit, spend };
  }, [customers]);

  function applySearch() {
    setSubmittedSearch(search.trim());
  }

  function clearSearch() {
    setSearch('');
    setSubmittedSearch('');
  }

  return (
    <Screen refreshing={isRefreshing} onRefresh={refresh}>
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text variant="title">Customers</Text>
          <Text variant="caption">Buyers across storefront and marketplace channels.</Text>
        </View>
      </View>

      <View style={styles.metricRow}>
        <MetricCard label="Customers" value={String(customers.length)} icon="people-outline" tone="primary" />
        <MetricCard label="Lifetime" value={formatMoney(metrics.spend)} icon="wallet-outline" tone="success" />
      </View>
      <View style={styles.metricRow}>
        <MetricCard label="Opt-ins" value={String(metrics.marketing)} icon="mail-outline" tone="info" />
        <MetricCard label="Store Credit" value={formatMoney(metrics.credit)} icon="card-outline" tone={metrics.credit > 0 ? 'warning' : 'neutral'} />
      </View>

      <Card>
        <TextField
          label="Search"
          value={search}
          onChangeText={setSearch}
          placeholder="Name, email, username..."
          returnKeyType="search"
          onSubmitEditing={applySearch}
        />
        <View style={styles.searchActions}>
          <Pressable onPress={applySearch} style={[styles.searchButton, { backgroundColor: c.primary }]}>
            <Ionicons name="search-outline" size={16} color={c.textInverse} />
            <Text variant="bodyBold" color={c.textInverse}>Search</Text>
          </Pressable>
          {submittedSearch ? (
            <Pressable onPress={clearSearch} style={styles.clearButton}>
              <Text variant="caption" color={c.textSecondary}>Clear</Text>
            </Pressable>
          ) : null}
        </View>
      </Card>

      {error ? <ErrorBanner message={error} onRetry={reload} /> : null}

      <SectionHeader title={submittedSearch ? `Matching “${submittedSearch}”` : 'Customer records'} />
      {isLoading ? (
        <LoadingState message="Loading customers..." />
      ) : customers.length === 0 ? (
        <EmptyState icon="people-outline" title="No customers" message={submittedSearch ? 'No customer matches that search.' : 'Customers appear after orders are recorded or imported.'} />
      ) : (
        customers.map((customer) => <CustomerCard key={customer.id} customer={customer} />)
      )}
    </Screen>
  );
}

function CustomerCard({ customer }: { customer: CustomerSummary }) {
  const c = useColors();
  const name = customer.displayName || customer.email || customer.channels?.[0] || 'Buyer';

  return (
    <Pressable
      onPress={() => router.push(`/customers/${customer.id}`)}
      accessibilityRole="button"
      accessibilityLabel={`Open ${name}`}
      style={({ pressed }) => [pressed && { opacity: 0.85 }]}
    >
      <Card>
        <View style={styles.cardTop}>
          <View style={styles.cardTitle}>
            <Text variant="bodyBold" numberOfLines={1}>{name}</Text>
            <Text variant="caption" color={c.textMuted} numberOfLines={1}>
              {customer.email || customer.phone || 'Marketplace buyer'}
            </Text>
          </View>
          {customer.acceptsMarketing ? <StatusPill label="Marketing" tone="info" size="sm" /> : null}
        </View>
        {(customer.channels?.length ?? 0) > 0 ? (
          <View style={styles.pills}>
            {customer.channels!.map((channel) => (
              <StatusPill key={channel} label={channel} tone="neutral" size="sm" />
            ))}
          </View>
        ) : null}
        <View style={[styles.customerStats, { borderTopColor: c.borderLight }]}>
          <Stat label="Orders" value={String(customer.orderCount ?? 0)} />
          <Stat label="Spend" value={formatMoney(customer.lifetimeSpend ?? 0)} />
          <Stat label="Credit" value={formatMoney(customer.storeCreditBalance ?? 0)} />
          <Stat label="Last" value={shortDate(customer.lastOrderAt)} />
        </View>
      </Card>
    </Pressable>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  const c = useColors();
  return (
    <View style={styles.stat}>
      <Text variant="label" color={c.textMuted}>{label}</Text>
      <Text variant="caption" numberOfLines={1}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.md },
  headerText: { flex: 1, gap: spacing.xs },
  metricRow: { flexDirection: 'row', gap: spacing.md },
  searchActions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  searchButton: { minHeight: 40, borderRadius: 10, paddingHorizontal: spacing.lg, flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  clearButton: { minHeight: 40, justifyContent: 'center', paddingHorizontal: spacing.sm },
  cardTop: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: spacing.md },
  cardTitle: { flex: 1, gap: spacing.xxs },
  pills: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  customerStats: { flexDirection: 'row', gap: spacing.md, borderTopWidth: 1, paddingTop: spacing.md, marginTop: spacing.xs },
  stat: { flex: 1, gap: spacing.xxs },
});
