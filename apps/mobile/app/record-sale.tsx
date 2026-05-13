import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { spacing } from '../src/components/theme';
import { formatMoney, parseOptionalNumber } from '../src/utils/money';

export default function RecordSaleScreen() {
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

  const netPreview = useMemo(() => {
    const sold = parseOptionalNumber(soldPrice) ?? 0;
    const fees = (parseOptionalNumber(platformFees) ?? 0)
      + (parseOptionalNumber(paymentFees) ?? 0)
      + (parseOptionalNumber(shippingCost) ?? 0)
      + (parseOptionalNumber(suppliesCost) ?? 0)
      + (parseOptionalNumber(otherCosts) ?? 0);
    return sold > 0 ? sold - fees : null;
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
      router.replace('/sales');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to record sale.');
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <Screen>
      {error ? <ErrorBanner message={error} /> : null}

      <Card variant="elevated">
        <Text variant="label">{params.title ?? 'Inventory sale'}</Text>
        <Text variant="hero">{formatMoney(netPreview)}</Text>
        <Text variant="caption">projected net before purchase cost</Text>
      </Card>

      <SectionHeader title="Sale" />
      <Card>
        <TextField label="Platform" value={platform} onChangeText={setPlatform} placeholder="eBay, Mercari, Facebook..." />
        <TextField label="Sold price" value={soldPrice} onChangeText={setSoldPrice} keyboardType="decimal-pad" prefix="$" placeholder="0.00" />
      </Card>

      <SectionHeader title="Costs" />
      <Card>
        <View style={styles.row}>
          <TextField label="Platform fees" value={platformFees} onChangeText={setPlatformFees} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
          <TextField label="Payment fees" value={paymentFees} onChangeText={setPaymentFees} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
        </View>
        <View style={styles.row}>
          <TextField label="Shipping" value={shippingCost} onChangeText={setShippingCost} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
          <TextField label="Supplies" value={suppliesCost} onChangeText={setSuppliesCost} keyboardType="decimal-pad" prefix="$" containerStyle={styles.flex} />
        </View>
        <TextField label="Other costs" value={otherCosts} onChangeText={setOtherCosts} keyboardType="decimal-pad" prefix="$" placeholder="0.00" />
      </Card>

      <Card>
        <TextField label="Notes" value={notes} onChangeText={setNotes} multiline placeholder="Buyer, order number, packing notes..." />
      </Card>

      <Button label={isSaving ? 'Saving...' : 'Record Sale'} icon="cash-outline" size="lg" fullWidth disabled={isSaving} onPress={() => void save()} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
});
