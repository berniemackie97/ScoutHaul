import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { Screen } from '../src/components/Screen';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { spacing } from '../src/components/theme';
import { BuyCheckCard } from '../src/features/appraisal/BuyCheckCard';
import { parseOptionalNumber } from '../src/utils/money';

type DealParams = {
  inventoryItemId?: string;
  expectedSalePrice?: string;
  askingPrice?: string;
  query?: string;
};

export default function DealCheckScreen() {
  const params = useLocalSearchParams<DealParams>();
  const [salePrice, setSalePrice] = useState(params.expectedSalePrice ?? '');
  const [askingPrice, setAskingPrice] = useState(params.askingPrice ?? '');
  const [title, setTitle] = useState<string | null>(params.query ?? null);
  const [loadError, setLoadError] = useState<string | null>(null);

  // An item already in stock brings its expected price and what was paid for it.
  useEffect(() => {
    const id = params.inventoryItemId;
    if (!id) return;
    let active = true;
    api
      .inventoryItem(id)
      .then((item) => {
        if (!active) return;
        setTitle(item.title);
        setSalePrice((current) => current || (item.estimatedSalePrice != null ? String(item.estimatedSalePrice) : ''));
        setAskingPrice((current) => current || (item.purchasePrice != null ? String(item.purchasePrice) : ''));
      })
      .catch((error: unknown) => active && setLoadError(error instanceof Error ? error.message : 'Could not load the item.'));
    return () => {
      active = false;
    };
  }, [params.inventoryItemId]);

  const expectedSalePrice = parseOptionalNumber(salePrice);

  return (
    <Screen>
      {loadError ? <ErrorBanner message={loadError} /> : null}
      <Card>
        {title ? <Text variant="bodyBold" numberOfLines={2}>{title}</Text> : null}
        <TextField
          label="Expected sale price"
          prefix="$"
          value={salePrice}
          onChangeText={setSalePrice}
          keyboardType="decimal-pad"
          placeholder="What it will sell for"
        />
        {expectedSalePrice === null ? (
          <View style={styles.hint}>
            <Text variant="caption">Not sure what it sells for? Scan it and price it from comps first.</Text>
            <Button label="Scan it" icon="scan-outline" variant="secondary" size="sm" onPress={() => router.push('/scanner')} />
          </View>
        ) : null}
      </Card>

      {expectedSalePrice !== null && expectedSalePrice > 0 ? (
        <BuyCheckCard expectedSalePrice={expectedSalePrice} askingPrice={askingPrice} onAskingPriceChange={setAskingPrice} />
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  hint: {
    gap: spacing.sm,
    alignItems: 'flex-start',
  },
});
