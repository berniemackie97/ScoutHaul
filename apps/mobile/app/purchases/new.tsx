import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import { Button } from '../../src/components/Button';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { ExpandableSection } from '../../src/components/ExpandableSection';
import { QueuedBanner } from '../../src/components/QueuedBanner';
import { Screen } from '../../src/components/Screen';
import { SelectField, type SelectOption } from '../../src/components/SelectField';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { enqueueOperation } from '../../src/db/localStore';
import type { AcquisitionLotAllocationMethod } from '../../src/domain/types';
import {
  emptyAcquisitionLotDraft,
  landedTotal,
  localDateValue,
  validateAcquisitionLotDraft,
  type AcquisitionLotDraft,
} from '../../src/features/purchases/acquisitionLotDraft';
import { useHaptics } from '../../src/hooks/useHaptics';
import { isWorthQueueing } from '../../src/services/offlineWrites';
import { formatMoney } from '../../src/utils/money';

const compactId = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;

const allocationOptions: SelectOption[] = [
  {
    value: 'Equal',
    label: 'Split evenly',
    description: 'Every item in the lot carries the same share of the cost.',
    icon: 'apps-outline',
  },
  {
    value: 'WeightedByExpectedValue',
    label: 'Weighted by expected value',
    description: 'The pieces you expect to sell high carry more of the cost.',
    icon: 'trending-up-outline',
  },
  {
    value: 'Manual',
    label: 'Set each cost by hand',
    description: 'You assign the cost per item when you itemize the lot.',
    icon: 'create-outline',
  },
];

/**
 * Recording a purchase where it happens.
 *
 * Purchases were read-only on this app: the empty state told whoever was standing at the sale to go
 * and use the admin portal on a computer. That is the one place the buying is not happening. The
 * money is known at the till and the receipt is in hand right then; what was in the pile is not, so
 * this records the costs and leaves itemizing for afterwards — which the server already allows.
 */
export default function NewPurchaseScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const haptics = useHaptics();
  const [draft, setDraft] = useState<AcquisitionLotDraft>(() => emptyAcquisitionLotDraft(localDateValue()));
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<keyof AcquisitionLotDraft, string>>>({});
  const [error, setError] = useState<string | null>(null);
  const [queuedNotice, setQueuedNotice] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const total = useMemo(() => landedTotal(draft), [draft]);

  function set<K extends keyof AcquisitionLotDraft>(field: K, value: AcquisitionLotDraft[K]) {
    setDraft((current) => ({ ...current, [field]: value }));
    setFieldErrors((current) => (current[field] ? { ...current, [field]: undefined } : current));
  }

  async function save() {
    const validation = validateAcquisitionLotDraft(draft);
    if (!validation.ok) {
      setFieldErrors(validation.errors);
      setError('Check the highlighted fields.');
      haptics.error();
      return;
    }

    setFieldErrors({});
    setIsSaving(true);
    setError(null);

    try {
      const lot = await api.createAcquisitionLot(validation.request, compactId());
      haptics.success();
      router.replace(`/purchases/${lot.id}`);
    } catch (e) {
      // A purchase is recorded at a sale, in a barn, in a car park — which is exactly where the
      // signal is worst. Losing it means the money left the account with no record of what it bought.
      if (isWorthQueueing(e)) {
        await enqueueOperation('CreateAcquisitionLot', '/api/acquisition-lots', 'POST', validation.request);
        haptics.warning();
        setQueuedNotice("Saved on this phone. It'll sync when you're back online.");
        return;
      }

      haptics.error();
      setError(e instanceof Error ? e.message : 'Unable to record this purchase.');
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <Screen>
      {error ? <ErrorBanner message={error} /> : null}
      {queuedNotice ? <QueuedBanner message={queuedNotice} /> : null}

      <View style={[styles.hero, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.md }]}>
        <Text variant="caption">Landed cost</Text>
        <Text variant="hero">{total > 0 ? formatMoney(total) : '$—'}</Text>
        <View style={styles.heroMeta}>
          <StatusPill
            label={allocationOptions.find((option) => option.value === draft.allocationMethod)?.label ?? 'Split evenly'}
            tone="neutral"
            size="sm"
          />
          <StatusPill label="Add items after saving" tone="info" size="sm" />
        </View>
      </View>

      <ExpandableSection title="What you bought" icon="receipt-outline" defaultExpanded>
        <TextField
          label="Name"
          value={draft.name}
          onChangeText={(value) => set('name', value)}
          placeholder="Estate sale — Maple St"
          error={fieldErrors.name}
          autoCapitalize="sentences"
        />
        <TextField
          label="Seller"
          value={draft.supplierName}
          onChangeText={(value) => set('supplierName', value)}
          placeholder="Who you bought it from"
          error={fieldErrors.supplierName}
          autoCapitalize="words"
        />
        <TextField
          label="Purchased on"
          value={draft.purchasedAt}
          onChangeText={(value) => set('purchasedAt', value)}
          placeholder="YYYY-MM-DD"
          error={fieldErrors.purchasedAt}
          autoCapitalize="none"
        />
      </ExpandableSection>

      <ExpandableSection title="What it cost" icon="cash-outline" defaultExpanded>
        <TextField
          label="Subtotal"
          value={draft.purchaseSubtotal}
          onChangeText={(value) => set('purchaseSubtotal', value)}
          placeholder="0.00"
          prefix="$"
          keyboardType="decimal-pad"
          error={fieldErrors.purchaseSubtotal}
        />
        <View style={styles.pair}>
          <TextField
            label="Shipping"
            value={draft.shippingCost}
            onChangeText={(value) => set('shippingCost', value)}
            placeholder="0.00"
            prefix="$"
            keyboardType="decimal-pad"
            error={fieldErrors.shippingCost}
            containerStyle={styles.pairField}
          />
          <TextField
            label="Tax"
            value={draft.taxCost}
            onChangeText={(value) => set('taxCost', value)}
            placeholder="0.00"
            prefix="$"
            keyboardType="decimal-pad"
            error={fieldErrors.taxCost}
            containerStyle={styles.pairField}
          />
        </View>
        <View style={styles.pair}>
          <TextField
            label="Fees"
            value={draft.feeCost}
            onChangeText={(value) => set('feeCost', value)}
            placeholder="0.00"
            prefix="$"
            keyboardType="decimal-pad"
            error={fieldErrors.feeCost}
            containerStyle={styles.pairField}
          />
          <TextField
            label="Other"
            value={draft.otherCost}
            onChangeText={(value) => set('otherCost', value)}
            placeholder="0.00"
            prefix="$"
            keyboardType="decimal-pad"
            error={fieldErrors.otherCost}
            containerStyle={styles.pairField}
          />
        </View>
      </ExpandableSection>

      <ExpandableSection title="How the cost gets split" icon="pie-chart-outline">
        <SelectField
          label="Allocation"
          value={draft.allocationMethod}
          options={allocationOptions}
          onChange={(value) => set('allocationMethod', value as AcquisitionLotAllocationMethod)}
        />
        <TextField
          label="Reference"
          value={draft.externalReference}
          onChangeText={(value) => set('externalReference', value)}
          placeholder="Receipt or invoice number"
          error={fieldErrors.externalReference}
          autoCapitalize="characters"
        />
        <TextField
          label="Notes"
          value={draft.notes}
          onChangeText={(value) => set('notes', value)}
          placeholder="Anything worth remembering about this buy"
          error={fieldErrors.notes}
          multiline
        />
      </ExpandableSection>

      <Button
        label={isSaving ? 'Saving…' : 'Record purchase'}
        icon="checkmark-circle-outline"
        size="lg"
        disabled={isSaving || Boolean(queuedNotice)}
        onPress={() => void save()}
      />
      {queuedNotice ? (
        <Button label="Back to purchases" variant="secondary" onPress={() => router.replace('/purchases')} />
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: {
    gap: spacing.xxs,
    padding: spacing.md,
    borderRadius: radius.lg,
    borderWidth: StyleSheet.hairlineWidth,
    marginBottom: spacing.md,
  },
  heroMeta: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
    marginTop: spacing.xs,
  },
  pair: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  pairField: {
    flex: 1,
  },
});
