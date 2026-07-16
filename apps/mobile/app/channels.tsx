import { Ionicons } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Alert, Linking, Pressable, StyleSheet, Switch, View } from 'react-native';
import { api } from '../src/api';
import { ordersApi } from '../src/api/orders';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { MetricCard } from '../src/components/MetricCard';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { SelectField, type SelectOption } from '../src/components/SelectField';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import { usableChannelOptions } from '../src/domain/channelOptions';
import type { ChannelAccount, ChannelListing, SaleChannelOption, UpsertChannelAccountRequest } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

const trimOrNull = (value: string) => value.trim() || null;
const dateLabel = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(new Date(value)) : '—';

export default function ChannelsScreen() {
  const c = useColors();
  const [accounts, setAccounts] = useState<ChannelAccount[]>([]);
  const [channelOptions, setChannelOptions] = useState<SaleChannelOption[]>([]);
  const [pendingDelist, setPendingDelist] = useState<ChannelListing[]>([]);
  const [editing, setEditing] = useState<ChannelAccount | null>(null);
  const [channel, setChannel] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [externalAccountId, setExternalAccountId] = useState('');
  const [defaultFeeProfileId, setDefaultFeeProfileId] = useState('');
  const [isActive, setIsActive] = useState(true);
  const [notes, setNotes] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [warning, setWarning] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async (showRefresh = false) => {
    if (showRefresh) setRefreshing(true);
    try {
      const nextAccounts = await api.channelAccounts();
      const [referenceResult, delistResult] = await Promise.allSettled([
        ordersApi.referenceData(),
        api.channelListings({ status: 'PendingDelist' }),
      ]);

      const warnings: string[] = [];
      setAccounts(nextAccounts);
      setChannelOptions(usableChannelOptions(referenceResult.status === 'fulfilled' ? referenceResult.value : null));
      if (referenceResult.status === 'rejected') {
        warnings.push('Reference data unavailable. Using default channels; fee profiles may be hidden.');
      }
      if (delistResult.status === 'fulfilled') {
        setPendingDelist(delistResult.value);
      } else {
        setPendingDelist([]);
        warnings.push('Manual delist worklist unavailable.');
      }
      setWarning(warnings.length > 0 ? warnings.join(' ') : null);
      setError(null);
    } catch (e) {
      setWarning(null);
      setError(e instanceof Error ? e.message : 'Channels unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }, []);

  useFocusEffect(useCallback(() => { void load(); }, [load]));

  const metrics = useMemo(() => ({
    active: accounts.filter((account) => account.isActive).length,
    native: accounts.filter((account) => account.isActive && account.isNativelySynced).length,
    external: accounts.filter((account) => account.isActive && !account.isNativelySynced).length,
    listings: accounts.reduce((sum, account) => sum + (account.activeListingCount ?? 0), 0),
  }), [accounts]);

  const selectableChannels: SelectOption[] = useMemo(() => channelOptions.map((option) => ({
    value: option.value,
    label: option.label,
    group: option.group,
  })), [channelOptions]);

  const feeProfiles = channelOptions.find((option) => option.value === channel)?.feeProfiles ?? [];
  const feeProfileOptions: SelectOption[] = useMemo(() => feeProfiles.map((profile) => ({
    value: profile.id,
    label: profile.label,
    description: profile.description,
  })), [feeProfiles]);

  function selectChannel(value: string) {
    setChannel(value);
    const selected = channelOptions.find((option) => option.value === value);
    const defaultProfile = selected?.feeProfiles?.find((profile) => profile.isDefault) ?? selected?.feeProfiles?.[0];
    setDefaultFeeProfileId(defaultProfile?.id ?? '');
  }

  function resetForm() {
    setEditing(null);
    setChannel('');
    setDisplayName('');
    setExternalAccountId('');
    setDefaultFeeProfileId('');
    setIsActive(true);
    setNotes('');
    setFieldErrors({});
  }

  function startEdit(account: ChannelAccount) {
    setEditing(account);
    setChannel(account.channel ?? '');
    setDisplayName(account.displayName ?? '');
    setExternalAccountId(account.externalAccountId ?? '');
    setDefaultFeeProfileId(account.defaultFeeProfileId ?? '');
    setIsActive(account.isActive);
    setNotes(account.notes ?? '');
    setMessage(null);
    setFieldErrors({});
  }

  function bodyFromForm(): UpsertChannelAccountRequest | null {
    const errors: Record<string, string> = {};
    if (!channel) errors.channel = 'Choose a channel.';
    if (!displayName.trim()) errors.displayName = 'Account name is required.';
    setFieldErrors(errors);
    if (Object.keys(errors).length > 0) return null;

    return {
      channel,
      displayName: displayName.trim(),
      externalAccountId: trimOrNull(externalAccountId),
      defaultFeeProfileId: trimOrNull(defaultFeeProfileId),
      isActive,
      notes: trimOrNull(notes),
    };
  }

  async function save() {
    const body = bodyFromForm();
    if (!body) {
      setError('Fix the highlighted fields.');
      return;
    }

    setBusy(true);
    try {
      const saved = editing
        ? await api.updateChannelAccount(editing.id, body)
        : await api.createChannelAccount(body);
      setMessage(editing ? 'Channel account updated.' : `Connected ${saved.displayName}.`);
      setError(null);
      resetForm();
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Channel account could not be saved.');
    } finally {
      setBusy(false);
    }
  }

  async function setAccountActive(account: ChannelAccount, nextActive: boolean) {
    setBusy(true);
    try {
      await api.updateChannelAccount(account.id, {
        channel: account.channel,
        displayName: account.displayName,
        externalAccountId: account.externalAccountId,
        defaultFeeProfileId: account.defaultFeeProfileId,
        isActive: nextActive,
        notes: account.notes,
      });
      setMessage(nextActive ? 'Channel reactivated.' : 'Channel deactivated.');
      setError(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Channel account could not be updated.');
    } finally {
      setBusy(false);
    }
  }

  function confirmDelete(account: ChannelAccount) {
    Alert.alert(
      'Delete channel account?',
      `${account.displayName} can only be deleted when it has no listing history.`,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Delete', style: 'destructive', onPress: () => void deleteAccount(account) },
      ],
    );
  }

  async function deleteAccount(account: ChannelAccount) {
    setBusy(true);
    try {
      await api.deleteChannelAccount(account.id);
      setMessage('Channel account deleted.');
      setError(null);
      if (editing?.id === account.id) resetForm();
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Channel account could not be deleted.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text variant="title">Channels</Text>
          <Text variant="caption">Accounts where inventory is listed, sold, imported, or manually tracked.</Text>
        </View>
      </View>

      <View style={styles.metricRow}>
        <MetricCard label="Active" value={String(metrics.active)} icon="radio-outline" tone="primary" />
        <MetricCard label="Listings" value={String(metrics.listings)} icon="pricetags-outline" tone="info" />
      </View>
      <View style={styles.metricRow}>
        <MetricCard label="Native" value={String(metrics.native)} icon="storefront-outline" tone="success" />
        <MetricCard label="External" value={String(metrics.external)} icon="globe-outline" />
      </View>

      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}
      {message ? (
        <Card><Text variant="caption" color={c.success}>{message}</Text></Card>
      ) : null}
      {warning ? (
        <Card style={{ borderColor: c.warning }}>
          <Text variant="caption" color={c.warning}>{warning}</Text>
        </Card>
      ) : null}

      {pendingDelist.length > 0 ? (
        <Card style={{ borderColor: c.warning }}>
          <View style={styles.warningTop}>
            <Ionicons name="warning-outline" size={18} color={c.warning} />
            <Text variant="bodyBold" color={c.warning}>Manual delist needed ({pendingDelist.length})</Text>
          </View>
          <Text variant="caption">These items sold elsewhere. Remove live marketplace listings manually, then mark them delisted.</Text>
          {pendingDelist.slice(0, 5).map((listing) => <PendingListing key={listing.id} listing={listing} />)}
        </Card>
      ) : null}

      <SectionHeader title={editing ? `Edit ${editing.displayName}` : 'Connect a channel'} />
      <Card>
        <SelectField
          label="Channel"
          value={channel}
          options={selectableChannels}
          onChange={selectChannel}
          placeholder="Select channel..."
          error={fieldErrors.channel}
          searchable
          required
        />
        <TextField
          label="Account name"
          value={displayName}
          onChangeText={setDisplayName}
          placeholder="Overhaul eBay Store"
          error={fieldErrors.displayName}
        />
        <TextField
          label="Username / handle / store ID"
          value={externalAccountId}
          onChangeText={setExternalAccountId}
          placeholder="@overhaultrading"
          autoCapitalize="none"
        />
        {feeProfileOptions.length > 0 ? (
          <SelectField
            label="Default fee profile"
            value={defaultFeeProfileId}
            options={feeProfileOptions}
            onChange={setDefaultFeeProfileId}
            placeholder="Channel default"
            searchable
          />
        ) : (
          <Text variant="caption">No fee profiles are configured for this channel.</Text>
        )}
        <View style={styles.switchRow}>
          <View style={styles.headerText}>
            <Text variant="bodyBold">Active</Text>
            <Text variant="caption">Use this account in sale, listing, and report workflows.</Text>
          </View>
          <Switch value={isActive} onValueChange={setIsActive} />
        </View>
        <TextField
          label="Notes"
          value={notes}
          onChangeText={setNotes}
          multiline
          placeholder="Store tier, seller standing, shipping profile, exceptions..."
        />
        <View style={styles.actions}>
          <Button label={busy ? 'Saving...' : editing ? 'Save changes' : 'Connect channel'} icon="save-outline" disabled={busy} onPress={() => void save()} />
          {editing ? <Button label="Cancel" variant="secondary" disabled={busy} onPress={resetForm} /> : null}
        </View>
      </Card>

      <SectionHeader title="Connected channels" />
      {isLoading ? (
        <LoadingState message="Loading channels..." />
      ) : accounts.length === 0 ? (
        <EmptyState icon="radio-outline" title="No channels connected" message="Add your storefront and marketplaces here." />
      ) : (
        accounts.map((account) => (
          <ChannelAccountCard
            key={account.id}
            account={account}
            busy={busy}
            onEdit={() => startEdit(account)}
            onToggle={() => void setAccountActive(account, !account.isActive)}
            onDelete={() => confirmDelete(account)}
          />
        ))
      )}
    </Screen>
  );
}

function PendingListing({ listing }: { listing: ChannelListing }) {
  const c = useColors();
  return (
    <View style={[styles.pendingRow, { borderTopColor: c.borderLight }]}>
      <Pressable style={styles.headerText} onPress={() => router.push(`/inventory/${listing.inventoryItemId}`)}>
        <Text variant="caption" numberOfLines={2}>{listing.inventoryItemTitle}</Text>
        <Text variant="label" color={c.textMuted}>{listing.channelAccountName}</Text>
      </Pressable>
      {listing.externalUrl ? (
        <Pressable onPress={() => void Linking.openURL(listing.externalUrl!)} hitSlop={8}>
          <Ionicons name="open-outline" size={20} color={c.primary} />
        </Pressable>
      ) : null}
    </View>
  );
}

function ChannelAccountCard({
  account,
  busy,
  onEdit,
  onToggle,
  onDelete,
}: {
  account: ChannelAccount;
  busy: boolean;
  onEdit: () => void;
  onToggle: () => void;
  onDelete: () => void;
}) {
  const c = useColors();
  return (
    <Card style={!account.isActive && styles.inactiveCard}>
      <View style={styles.accountTop}>
        <View style={styles.headerText}>
          <Text variant="bodyBold">{account.displayName}</Text>
          <Text variant="caption" color={c.textMuted}>{account.externalAccountId || 'No handle'} · connected {dateLabel(account.connectedAt)}</Text>
        </View>
        <StatusPill label={account.isActive ? 'Active' : 'Inactive'} tone={account.isActive ? 'success' : 'neutral'} size="sm" />
      </View>
      <View style={styles.pills}>
        <StatusPill label={account.channel} tone="primary" size="sm" />
        <StatusPill label={account.isNativelySynced ? 'Native sync' : 'Manual track'} tone={account.isNativelySynced ? 'success' : 'info'} size="sm" />
        <StatusPill label={`${account.activeListingCount ?? 0} active`} tone="neutral" size="sm" />
      </View>
      {account.defaultFeeProfileId ? <Text variant="caption">Fee profile: {account.defaultFeeProfileId}</Text> : null}
      {account.notes ? <Text variant="caption" color={c.textMuted}>{account.notes}</Text> : null}
      <View style={styles.actions}>
        <Button label="Edit" icon="create-outline" size="sm" variant="secondary" disabled={busy} onPress={onEdit} />
        <Button label={account.isActive ? 'Deactivate' : 'Reactivate'} icon={account.isActive ? 'power-outline' : 'checkmark-circle-outline'} size="sm" variant="secondary" disabled={busy} onPress={onToggle} />
        {account.activeListingCount === 0 ? (
          <Button label="Delete" icon="trash-outline" size="sm" variant="danger" disabled={busy} onPress={onDelete} />
        ) : null}
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.md },
  headerText: { flex: 1, gap: spacing.xxs },
  metricRow: { flexDirection: 'row', gap: spacing.md },
  warningTop: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  pendingRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, borderTopWidth: 1, paddingTop: spacing.sm, marginTop: spacing.sm },
  switchRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  actions: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm },
  inactiveCard: { opacity: 0.7 },
  accountTop: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.md },
  pills: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
});
