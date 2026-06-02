import { Ionicons } from '@expo/vector-icons';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { ExpandableSection } from '../src/components/ExpandableSection';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import { useHaptics } from '../src/hooks/useHaptics';
import { formatMoney, parseOptionalNumber } from '../src/utils/money';

export default function RecordSaleScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const haptics = useHaptics();
  const params = useLocalSearchParams<{
    inventoryItemId?: string;
    listingId?: string;
    title?: string;
    platform?: string;
    listPrice?: string;
  }>();
  const [platform, setPlatform] = useState(params.platform ?? 'eBay');
  const [soldPrice, setSoldPrice] = useState(params.listPrice ?? '');
  const [platformFees, setPlatformFees] = useState('');
  const [paymentFees, setPaymentFees] = useState('');
  const [shippingCost, setShippingCost] = useState('');
  const [suppliesCost, setSuppliesCost] = useState('');
  const [otherCosts, setOtherCosts] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const costsFilled = useMemo(() => {
    let n = 0;
    if (platformFees.trim()) n++;
    if (paymentFees.trim()) n++;
    if (shippingCost.trim()) n++;
    if (suppliesCost.trim()) n++;
    if (otherCosts.trim()) n++;
    return n;
  }, [platformFees, paymentFees, shippingCost, suppliesCost, otherCosts]);

  const netPreview = useMemo(() => {
    const sold = parseOptionalNumber(soldPrice) ?? 0;
    const fees = (parseOptionalNumber(platformFees) ?? 0)
      + (parseOptionalNumber(paymentFees) ?? 0)
      + (parseOptionalNumber(shippingCost) ?? 0)
      + (parseOptionalNumber(suppliesCost) ?? 0)
      + (parseOptionalNumber(otherCosts) ?? 0);
    return sold > 0 ? { net: sold - fees, totalFees: fees, positive: sold - fees > 0 } : null;
  }, [soldPrice, platformFees, paymentFees, shippingCost, suppliesCost, otherCosts]);

  async function save() {
    const inventoryItemId = params.inventoryItemId;
    const parsedSoldPrice = parseOptionalNumber(soldPrice);
    if (!inventoryItemId) {
      setError('Inventory item is required.');
      return;
    }

    if (!platform.trim() || parsedSoldPrice === null || parsedSoldPrice <= 0) {
      setError('Platform and positive sold price are required.');
      return;
    }

    try {
      setIsSaving(true);
      setError(null);
      await api.recordSale({
        inventoryItemId,
        listingId: params.listingId ?? null,
        platform,
        soldPrice: parsedSoldPrice,
        platformFees: parseOptionalNumber(platformFees) ?? 0,
        paymentFees: parseOptionalNumber(paymentFees) ?? 0,
        shippingCost: parseOptionalNumber(shippingCost) ?? 0,
        suppliesCost: parseOptionalNumber(suppliesCost) ?? 0,
        otherCosts: parseOptionalNumber(otherCosts) ?? 0,
        soldAt: new Date().toISOString(),
        notes: notes || null,
      });
      haptics.success();
      router.replace('/sales');
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

      {/* Hero Net Preview */}
      <View style={[styles.heroCard, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.md }]}>
        {params.title ? (
          <Text variant="caption" numberOfLines={1}>{params.title}</Text>
        ) : null}
        <Text
          variant="hero"
          color={netPreview ? (netPreview.positive ? c.success : c.danger) : c.textMuted}
        >
          {netPreview ? formatMoney(netPreview.net) : '$—'}
        </Text>
        <View style={styles.heroMeta}>
          <Text variant="caption">Projected net before purchase cost</Text>
          {netPreview && netPreview.totalFees > 0 ? (
            <StatusPill label={`${formatMoney(netPreview.totalFees)} fees`} tone="warning" size="sm" />
          ) : null}
        </View>
      </View>

      {/* Sale Info */}
      <ExpandableSection title="Sale Details" icon="cash-outline" defaultExpanded>
        <TextField label="Platform" value={platform} onChangeText={setPlatform} placeholder="eBay, Mercari, Facebook..." />
        <TextField label="Sold price" value={soldPrice} onChangeText={setSoldPrice} keyboardType="decimal-pad" prefix="$" placeholder="0.00" />
      </ExpandableSection>

      {/* Costs — collapsed with fill count */}
      <ExpandableSection
        title="Fees & Costs"
        icon="calculator-outline"
        filledCount={costsFilled}
        totalCount={5}
      >
        <View style={styles.row}>
          <TextField label="Platform fees" value={platformFees} onChangeText={setPlatformFees} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
          <TextField label="Payment fees" value={paymentFees} onChangeText={setPaymentFees} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
        </View>
        <View style={styles.row}>
          <TextField label="Shipping" value={shippingCost} onChangeText={setShippingCost} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
          <TextField label="Supplies" value={suppliesCost} onChangeText={setSuppliesCost} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
        </View>
        <TextField label="Other costs" value={otherCosts} onChangeText={setOtherCosts} keyboardType="decimal-pad" prefix="$" placeholder="0.00" />
      </ExpandableSection>

      {/* Notes */}
      <ExpandableSection title="Notes" icon="document-text-outline">
        <TextField label="Notes" value={notes} onChangeText={setNotes} multiline placeholder="Buyer, order number, packing notes..." />
      </ExpandableSection>

      <Button label={isSaving ? 'Saving...' : 'Record Sale'} icon="cash-outline" size="lg" fullWidth disabled={isSaving} onPress={() => void save()} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  heroCard: {
    borderRadius: radius.xl,
    borderWidth: 1,
    padding: spacing.xl,
    alignItems: 'center',
    gap: spacing.xs,
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
});
