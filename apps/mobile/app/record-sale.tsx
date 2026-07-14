import { router, useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { StyleSheet, Switch, View } from 'react-native';
import { Button } from '../src/components/Button';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { ExpandableSection } from '../src/components/ExpandableSection';
import { Screen } from '../src/components/Screen';
import { SelectField, type SelectOption } from '../src/components/SelectField';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import { ordersApi } from '../src/api/orders';
import type { ChannelFeeProfile, ChannelFeeRule, SaleCandidate, SaleChannelOption } from '../src/domain/order.types';
import { useHaptics } from '../src/hooks/useHaptics';
import { formatMoney, parseOptionalNumber } from '../src/utils/money';

const localDateValue = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
const round2 = (value: number) => Math.round(value * 100) / 100;
const compactId = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;

function moneyValue(value: string, field: string, errors: Record<string, string>): number {
  const trimmed = value.trim();
  if (!trimmed) return 0;
  const parsed = Number(trimmed);
  if (!Number.isFinite(parsed)) {
    errors[field] = 'Enter a valid amount.';
    return 0;
  }
  if (parsed < 0) {
    errors[field] = 'Amount cannot be negative.';
    return 0;
  }
  return parsed;
}

function estimateRule(rule: ChannelFeeRule, salePrice: number): number {
  const above = Math.max(0, rule.appliesAboveAmount ?? 0);
  const upTo = rule.appliesUpToAmount ?? salePrice;
  const basis = Math.max(0, Math.min(salePrice, upTo) - above);
  if (basis <= 0 && (rule.percent ?? 0) > 0) return 0;

  let fee = (basis * Math.max(0, rule.percent ?? 0)) / 100 + Math.max(0, rule.fixedAmount ?? 0);
  if (rule.minimumFee != null) fee = Math.max(fee, rule.minimumFee);
  if (rule.maximumFee != null) fee = Math.min(fee, rule.maximumFee);
  return round2(fee);
}

function estimateRules(rules: ChannelFeeRule[] | null | undefined, salePrice: number): number {
  return round2((rules ?? []).reduce((sum, rule) => sum + estimateRule(rule, salePrice), 0));
}

export default function RecordSaleScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const haptics = useHaptics();
  const params = useLocalSearchParams<{
    inventoryItemId?: string;
    listingId?: string;
    title?: string;
    platform?: string;
    listPrice?: string;
    purchaseCost?: string;
  }>();

  const [candidates, setCandidates] = useState<SaleCandidate[]>([]);
  const [channels, setChannels] = useState<SaleChannelOption[]>([]);
  const [candidateId, setCandidateId] = useState(params.inventoryItemId ?? '');
  const [listingId, setListingId] = useState(params.listingId ?? '');
  const [platform, setPlatform] = useState(params.platform ?? '');
  const [feeProfileId, setFeeProfileId] = useState('');
  const [soldPrice, setSoldPrice] = useState(params.listPrice ?? '');
  const [shippingCharged, setShippingCharged] = useState('');
  const [platformFees, setPlatformFees] = useState('');
  const [paymentFees, setPaymentFees] = useState('');
  const [shippingCost, setShippingCost] = useState('');
  const [suppliesCost, setSuppliesCost] = useState('');
  const [otherCosts, setOtherCosts] = useState('');
  const [salesTax, setSalesTax] = useState('');
  const [taxRemitted, setTaxRemitted] = useState(true);
  const [soldAt, setSoldAt] = useState(localDateValue());
  const [externalOrderId, setExternalOrderId] = useState('');
  const [buyerReference, setBuyerReference] = useState('');
  const [buyerName, setBuyerName] = useState('');
  const [shippingCarrier, setShippingCarrier] = useState('');
  const [trackingNumber, setTrackingNumber] = useState('');
  const [notes, setNotes] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    let active = true;
    void Promise.all([ordersApi.candidates(), ordersApi.referenceData()])
      .then(([nextCandidates, reference]) => {
        if (!active) return;
        setCandidates(nextCandidates);
        setChannels(reference.channels.filter((channel) => !channel.isLegacy));
      })
      .catch((e) => {
        if (active) setError(e instanceof Error ? e.message : 'Unable to load sale reference data.');
      });
    return () => {
      active = false;
    };
  }, []);

  const mergedCandidates = useMemo(() => {
    if (!params.inventoryItemId || candidates.some((candidate) => candidate.inventoryItemId === params.inventoryItemId)) {
      return candidates;
    }

    const launched: SaleCandidate = {
      inventoryItemId: params.inventoryItemId,
      title: params.title ?? 'Selected inventory item',
      status: 'Selected',
      costOfGoods: parseOptionalNumber(params.purchaseCost ?? '') ?? 0,
      expectedSalePrice: parseOptionalNumber(params.listPrice ?? ''),
      listingId: params.listingId ?? null,
      listingPlatform: params.platform ?? null,
      listingPrice: parseOptionalNumber(params.listPrice ?? ''),
    };

    return [launched, ...candidates];
  }, [candidates, params.inventoryItemId, params.listingId, params.listPrice, params.platform, params.purchaseCost, params.title]);

  const selectedCandidate = mergedCandidates.find((candidate) => candidate.inventoryItemId === candidateId);
  const selectedChannel = channels.find((channel) => channel.value === platform);
  const feeProfiles = selectedChannel?.feeProfiles ?? [];
  const selectedFeeProfile =
    feeProfiles.find((profile) => profile.id === feeProfileId) ??
    feeProfiles.find((profile) => profile.isDefault) ??
    feeProfiles[0];

  useEffect(() => {
    if (!platform || feeProfileId || !selectedFeeProfile) return;
    setFeeProfileId(selectedFeeProfile.id);
  }, [feeProfileId, platform, selectedFeeProfile]);

  useEffect(() => {
    if (!selectedCandidate || soldPrice.trim()) return;
    setSoldPrice(String(selectedCandidate.listingPrice ?? selectedCandidate.expectedSalePrice ?? ''));
  }, [selectedCandidate, soldPrice]);

  const candidateOptions: SelectOption[] = useMemo(() => mergedCandidates.map((candidate) => ({
    value: candidate.inventoryItemId,
    label: candidate.title,
    description: `${candidate.status} · COGS ${formatMoney(candidate.costOfGoods)}${candidate.listingPlatform ? ` · ${candidate.listingPlatform}` : ''}`,
    group: candidate.listingPlatform ? 'Listed / ready' : 'Inventory',
  })), [mergedCandidates]);

  const channelOptions: SelectOption[] = useMemo(() => channels.map((channel) => ({
    value: channel.value,
    label: channel.label,
    group: channel.group,
  })), [channels]);

  const feeProfileOptions: SelectOption[] = useMemo(() => feeProfiles.map((profile) => ({
    value: profile.id,
    label: profile.label,
    description: profile.description,
  })), [feeProfiles]);

  const sold = parseOptionalNumber(soldPrice) ?? 0;
  const shipping = parseOptionalNumber(shippingCharged) ?? 0;
  const revenue = sold + shipping;
  const costOfGoods = selectedCandidate?.costOfGoods ?? 0;
  const totalFees =
    (parseOptionalNumber(platformFees) ?? 0) +
    (parseOptionalNumber(paymentFees) ?? 0) +
    (parseOptionalNumber(shippingCost) ?? 0) +
    (parseOptionalNumber(suppliesCost) ?? 0) +
    (parseOptionalNumber(otherCosts) ?? 0);
  const marketplaceNet = revenue - totalFees;
  const trueNet = marketplaceNet - costOfGoods;
  const taxOwed = taxRemitted ? 0 : parseOptionalNumber(salesTax) ?? 0;
  const estimatedPlatformFees = selectedFeeProfile ? estimateRules(selectedFeeProfile.platformRules, revenue) : 0;
  const estimatedPaymentFees = selectedFeeProfile ? estimateRules(selectedFeeProfile.paymentRules, revenue) : 0;
  const canApplyEstimate = revenue > 0 && (estimatedPlatformFees > 0 || estimatedPaymentFees > 0);
  const costsFilled = [platformFees, paymentFees, shippingCost, suppliesCost, otherCosts].filter((value) => value.trim()).length;
  const identityFilled = [externalOrderId, buyerReference, buyerName, shippingCarrier, trackingNumber].filter((value) => value.trim()).length;

  const chooseChannel = useCallback((value: string) => {
    setPlatform(value);
    const nextChannel = channels.find((channel) => channel.value === value);
    const defaultProfile = nextChannel?.feeProfiles?.find((profile: ChannelFeeProfile) => profile.isDefault) ?? nextChannel?.feeProfiles?.[0];
    setFeeProfileId(defaultProfile?.id ?? '');
  }, [channels]);

  function chooseCandidate(value: string) {
    setCandidateId(value);
    const item = mergedCandidates.find((candidate) => candidate.inventoryItemId === value);
    if (!item) {
      setListingId('');
      return;
    }

    setListingId(item.listingId ?? '');
    setSoldPrice(String(item.listingPrice ?? item.expectedSalePrice ?? ''));
    if (item.listingPlatform) chooseChannel(item.listingPlatform);
  }

  function applyFeeEstimate() {
    setPlatformFees(String(estimatedPlatformFees));
    setPaymentFees(String(estimatedPaymentFees));
  }

  function validate() {
    const errors: Record<string, string> = {};
    if (!candidateId) errors.inventoryItemId = 'Select the inventory item that sold.';
    if (!platform) errors.platform = 'Select the sales channel.';

    const parsedSoldPrice = moneyValue(soldPrice, 'soldPrice', errors);
    if (parsedSoldPrice <= 0 && !errors.soldPrice) errors.soldPrice = 'Enter a positive sold price.';

    moneyValue(shippingCharged, 'shippingCharged', errors);
    moneyValue(platformFees, 'platformFees', errors);
    moneyValue(paymentFees, 'paymentFees', errors);
    moneyValue(shippingCost, 'shippingCost', errors);
    moneyValue(suppliesCost, 'suppliesCost', errors);
    moneyValue(otherCosts, 'otherCosts', errors);
    moneyValue(salesTax, 'salesTax', errors);

    if (!/^\d{4}-\d{2}-\d{2}$/.test(soldAt) || Number.isNaN(new Date(`${soldAt}T12:00:00Z`).getTime())) {
      errors.soldAt = 'Enter a valid sale date.';
    } else if (soldAt > localDateValue()) {
      errors.soldAt = 'Sale date cannot be in the future.';
    }

    if (shippingCarrier.trim() && !trackingNumber.trim()) {
      errors.trackingNumber = 'Tracking number is required when carrier is set.';
    }
    if (trackingNumber.trim() && !shippingCarrier.trim()) {
      errors.shippingCarrier = 'Carrier is required when tracking number is set.';
    }

    setFieldErrors(errors);
    return { ok: Object.keys(errors).length === 0, soldPrice: parsedSoldPrice };
  }

  async function save() {
    const validation = validate();
    if (!validation.ok) {
      haptics.error();
      setError('Fix the highlighted fields.');
      return;
    }

    try {
      setIsSaving(true);
      setError(null);
      const sale = await ordersApi.recordSale({
        inventoryItemId: candidateId,
        listingId: listingId || null,
        platform,
        soldPrice: validation.soldPrice,
        shippingCharged: moneyValue(shippingCharged, 'shippingCharged', {}),
        platformFees: moneyValue(platformFees, 'platformFees', {}),
        paymentFees: moneyValue(paymentFees, 'paymentFees', {}),
        shippingCost: moneyValue(shippingCost, 'shippingCost', {}),
        suppliesCost: moneyValue(suppliesCost, 'suppliesCost', {}),
        otherCosts: moneyValue(otherCosts, 'otherCosts', {}),
        salesTaxCollected: moneyValue(salesTax, 'salesTax', {}),
        taxRemittedByPlatform: taxRemitted,
        soldAt: new Date(`${soldAt}T12:00:00Z`).toISOString(),
        notes: notes.trim() || null,
        externalOrderId: externalOrderId.trim() || null,
        buyerReference: buyerReference.trim() || null,
        buyerName: buyerName.trim() || null,
        shippingCarrier: shippingCarrier.trim() || null,
        trackingNumber: trackingNumber.trim() || null,
      }, compactId());
      haptics.success();
      router.replace(sale.orderId ? `/orders/${sale.orderId}` : '/(tabs)/orders');
    } catch (e) {
      haptics.error();
      setError(e instanceof Error ? e.message : 'Unable to record sale.');
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <Screen>
      {error ? <ErrorBanner message={error} /> : null}

      <View style={[styles.heroCard, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.md }]}>
        <Text variant="caption" numberOfLines={1}>
          {selectedCandidate?.title ?? 'No inventory item selected'}
        </Text>
        <Text variant="hero" color={trueNet >= 0 ? c.success : c.danger}>
          {revenue > 0 ? formatMoney(trueNet) : '$—'}
        </Text>
        <View style={styles.heroMeta}>
          <StatusPill label={`${formatMoney(costOfGoods)} COGS`} tone="neutral" size="sm" />
          <StatusPill label={`${formatMoney(totalFees)} fees/costs`} tone={totalFees > 0 ? 'warning' : 'neutral'} size="sm" />
          {taxOwed > 0 ? <StatusPill label={`${formatMoney(taxOwed)} tax owed`} tone="warning" size="sm" /> : null}
        </View>
      </View>

      <ExpandableSection title="Sale identity" icon="cash-outline" defaultExpanded>
        <SelectField
          label="Inventory item"
          value={candidateId}
          options={candidateOptions}
          onChange={chooseCandidate}
          placeholder="Select sold item..."
          error={fieldErrors.inventoryItemId}
          searchable
          required
        />
        <SelectField
          label="Sales channel"
          value={platform}
          options={channelOptions}
          onChange={chooseChannel}
          placeholder="Select channel..."
          error={fieldErrors.platform}
          searchable
          required
        />
        {feeProfileOptions.length > 0 ? (
          <SelectField
            label="Fee profile"
            value={feeProfileId}
            options={feeProfileOptions}
            onChange={setFeeProfileId}
            placeholder="Select fee profile..."
            searchable
          />
        ) : (
          <Text variant="caption">No fee profile on this channel. Enter actual fees manually.</Text>
        )}
        <View style={styles.row}>
          <TextField
            label="Sold price"
            value={soldPrice}
            onChangeText={setSoldPrice}
            keyboardType="decimal-pad"
            prefix="$"
            placeholder="0.00"
            error={fieldErrors.soldPrice}
            containerStyle={styles.flex}
          />
          <TextField
            label="Buyer shipping"
            value={shippingCharged}
            onChangeText={setShippingCharged}
            keyboardType="decimal-pad"
            prefix="$"
            placeholder="0.00"
            error={fieldErrors.shippingCharged}
            containerStyle={styles.flex}
          />
        </View>
        <TextField
          label="Sold date (YYYY-MM-DD)"
          value={soldAt}
          onChangeText={setSoldAt}
          placeholder="2026-07-13"
          error={fieldErrors.soldAt}
        />
        <TextField
          label="Sales tax collected"
          value={salesTax}
          onChangeText={setSalesTax}
          keyboardType="decimal-pad"
          prefix="$"
          placeholder="0.00"
          error={fieldErrors.salesTax}
        />
        <View style={styles.taxRow}>
          <View style={styles.flex}>
            <Text variant="bodyBold">Marketplace facilitator remits tax</Text>
            <Text variant="caption">Tax is pass-through when the channel collects and remits it.</Text>
          </View>
          <Switch value={taxRemitted} onValueChange={setTaxRemitted} />
        </View>
      </ExpandableSection>

      <ExpandableSection title="Order identity & tracking" icon="receipt-outline" filledCount={identityFilled} totalCount={5}>
        <TextField
          label="Marketplace order number"
          value={externalOrderId}
          onChangeText={setExternalOrderId}
          placeholder="e.g. 12-34567-89012"
        />
        <TextField
          label="Buyer username / handle"
          value={buyerReference}
          onChangeText={setBuyerReference}
          placeholder="eBay username, Depop handle..."
        />
        <TextField
          label="Buyer display name"
          value={buyerName}
          onChangeText={setBuyerName}
          placeholder="Optional"
        />
        <View style={styles.row}>
          <TextField
            label="Carrier"
            value={shippingCarrier}
            onChangeText={setShippingCarrier}
            placeholder="USPS, UPS, FedEx..."
            error={fieldErrors.shippingCarrier}
            containerStyle={styles.flex}
          />
          <TextField
            label="Tracking number"
            value={trackingNumber}
            onChangeText={setTrackingNumber}
            placeholder="Tracking"
            error={fieldErrors.trackingNumber}
            containerStyle={styles.flex}
          />
        </View>
        <Text variant="caption">Matching order number or tracking lets the backend attach multiple sale lines to one order.</Text>
      </ExpandableSection>

      <ExpandableSection title="Fees & costs" icon="calculator-outline" filledCount={costsFilled} totalCount={5}>
        {canApplyEstimate ? (
          <View style={[styles.estimateCard, { borderColor: c.border, backgroundColor: c.surfaceAlt }]}>
            <View style={styles.flex}>
              <Text variant="bodyBold">{selectedChannel?.label} fee estimate</Text>
              <Text variant="caption">
                Platform {formatMoney(estimatedPlatformFees)} · payment {formatMoney(estimatedPaymentFees)}
              </Text>
            </View>
            <Button label="Apply" size="sm" variant="secondary" onPress={applyFeeEstimate} />
          </View>
        ) : null}
        <View style={styles.row}>
          <TextField
            label="Platform fees"
            value={platformFees}
            onChangeText={setPlatformFees}
            keyboardType="decimal-pad"
            prefix="$"
            error={fieldErrors.platformFees}
            containerStyle={styles.flex}
          />
          <TextField
            label="Payment fees"
            value={paymentFees}
            onChangeText={setPaymentFees}
            keyboardType="decimal-pad"
            prefix="$"
            error={fieldErrors.paymentFees}
            containerStyle={styles.flex}
          />
        </View>
        <View style={styles.row}>
          <TextField
            label="Shipping label"
            value={shippingCost}
            onChangeText={setShippingCost}
            keyboardType="decimal-pad"
            prefix="$"
            error={fieldErrors.shippingCost}
            containerStyle={styles.flex}
          />
          <TextField
            label="Supplies"
            value={suppliesCost}
            onChangeText={setSuppliesCost}
            keyboardType="decimal-pad"
            prefix="$"
            error={fieldErrors.suppliesCost}
            containerStyle={styles.flex}
          />
        </View>
        <TextField
          label="Other costs"
          value={otherCosts}
          onChangeText={setOtherCosts}
          keyboardType="decimal-pad"
          prefix="$"
          placeholder="0.00"
          error={fieldErrors.otherCosts}
        />
      </ExpandableSection>

      <ExpandableSection title="Net breakdown" icon="analytics-outline">
        <BreakdownRow label="Item sale" value={sold} />
        <BreakdownRow label="Shipping charged" value={shipping} />
        <BreakdownRow label="Marketplace net before COGS" value={marketplaceNet} />
        <BreakdownRow label="COGS" value={-costOfGoods} />
        <BreakdownRow label="True net" value={trueNet} strong />
        {salesTax.trim() ? (
          <BreakdownRow label={taxRemitted ? 'Sales tax pass-through' : 'Sales tax owed'} value={taxRemitted ? parseOptionalNumber(salesTax) ?? 0 : taxOwed} />
        ) : null}
      </ExpandableSection>

      <ExpandableSection title="Notes" icon="document-text-outline">
        <TextField
          label="Notes"
          value={notes}
          onChangeText={setNotes}
          multiline
          placeholder="Payout notes, buyer context, exceptions..."
        />
      </ExpandableSection>

      <Button
        label={isSaving ? 'Recording...' : 'Record sale'}
        icon="cash-outline"
        size="lg"
        fullWidth
        disabled={isSaving}
        onPress={() => void save()}
      />
    </Screen>
  );
}

function BreakdownRow({ label, value, strong }: { label: string; value: number; strong?: boolean }) {
  const { colors: c } = useAppTheme();
  return (
    <View style={[styles.breakdownRow, strong && { borderTopColor: c.borderLight, borderTopWidth: 1, paddingTop: spacing.sm }]}>
      <Text variant={strong ? 'bodyBold' : 'body'} color={strong ? undefined : c.textSecondary}>
        {label}
      </Text>
      <Text variant={strong ? 'bodyBold' : 'body'} color={strong && value < 0 ? c.danger : undefined}>
        {formatMoney(value)}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  heroCard: {
    borderRadius: radius.xl,
    borderWidth: 1,
    padding: spacing.xl,
    alignItems: 'center',
    gap: spacing.sm,
  },
  heroMeta: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    flexWrap: 'wrap',
    justifyContent: 'center',
  },
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
  taxRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  estimateCard: {
    borderWidth: 1,
    borderRadius: radius.lg,
    padding: spacing.md,
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  breakdownRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
});
