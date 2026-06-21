import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { LoadingState } from '../src/components/LoadingState';
import { Screen } from '../src/components/Screen';
import { SelectField, type SelectOption } from '../src/components/SelectField';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { spacing } from '../src/components/theme';
import type { Sale, SaleStatus } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

const labels: Record<SaleStatus, string> = {
  Recorded: 'Recorded / sold',
  Packed: 'Packed',
  Shipped: 'Shipped',
  Delivered: 'Delivered',
  ReturnRequested: 'Return requested',
  Returned: 'Return received',
  Refunded: 'Refunded',
  Cancelled: 'Cancelled',
};
const localDateValue = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 10);

export default function SaleTransitionScreen() {
  const { saleId } = useLocalSearchParams<{ saleId?: string }>();
  const [sale, setSale] = useState<Sale | null>(null);
  const [status, setStatus] = useState<SaleStatus | ''>('');
  const [occurredAt, setOccurredAt] = useState(localDateValue());
  const [reason, setReason] = useState('');
  const [carrier, setCarrier] = useState('');
  const [tracking, setTracking] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!saleId) return;
    void api
      .sale(saleId)
      .then((value) => {
        setSale(value);
        setStatus(value.allowedTransitions[0] ?? '');
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Sale unavailable.'));
  }, [saleId]);

  if (!sale && !error)
    return (
      <Screen>
        <LoadingState message="Loading sale workflow…" />
      </Screen>
    );
  const options: SelectOption[] = (sale?.allowedTransitions ?? []).map((value) => ({
    value,
    label: labels[value],
  }));
  const needsReason =
    !!sale &&
    !!status &&
    (['Cancelled', 'ReturnRequested', 'Returned', 'Refunded'].includes(status) ||
      (sale.status === 'Packed' && status === 'Recorded') ||
      (sale.status === 'ReturnRequested' && status === 'Delivered'));

  async function submit() {
    if (!sale || !status) return;
    if (needsReason && !reason.trim()) {
      setError('A reason is required for this transition.');
      return;
    }
    if ((carrier.trim() && !tracking.trim()) || (!carrier.trim() && tracking.trim())) {
      setError('Enter both carrier and tracking number.');
      return;
    }
    const occurredDate = occurredAt === localDateValue() ? new Date() : new Date(`${occurredAt}T12:00:00Z`);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(occurredAt) || Number.isNaN(occurredDate.getTime()) || occurredAt > localDateValue()) {
      setError('Enter a valid date that is not in the future.');
      return;
    }
    try {
      setSaving(true);
      setError(null);
      await api.transitionSale(sale.id, {
        status,
        occurredAt: occurredDate.toISOString(),
        reason: reason.trim() || null,
        carrier: carrier.trim() || null,
        trackingNumber: tracking.trim() || null,
      });
      router.replace('/sales');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to update sale.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <Screen>
      {error ? <ErrorBanner message={error} /> : null}
      {sale ? (
        <>
          <View style={{ gap: spacing.xs }}>
            <Text variant="title">{sale.inventoryItemTitle}</Text>
            <Text variant="caption">
              {sale.platform} · {formatMoney(sale.soldPrice)} · currently {labels[sale.status]}
            </Text>
          </View>
          {options.length === 0 ? (
            <Text variant="body">This sale is in a terminal state.</Text>
          ) : (
            <>
              <SelectField
                label="Next status"
                value={status}
                options={options}
                onChange={(value) => setStatus(value as SaleStatus)}
                clearable={false}
              />
              <TextField label="Occurred date (YYYY-MM-DD)" value={occurredAt} onChangeText={setOccurredAt} />
              {status === 'Shipped' ? (
                <>
                  <TextField label="Carrier" value={carrier} onChangeText={setCarrier} placeholder="USPS, UPS, FedEx…" />
                  <TextField label="Tracking number" value={tracking} onChangeText={setTracking} />
                </>
              ) : null}
              <TextField
                label={`Reason${needsReason ? ' (required)' : ''}`}
                value={reason}
                onChangeText={setReason}
                multiline
                placeholder="Operational context for the audit trail…"
              />
              <Button
                label={saving ? 'Updating…' : `Move to ${status ? labels[status] : ''}`}
                icon="git-branch-outline"
                size="lg"
                fullWidth
                disabled={saving || !status}
                onPress={() => void submit()}
              />
            </>
          )}
        </>
      ) : null}
    </Screen>
  );
}
