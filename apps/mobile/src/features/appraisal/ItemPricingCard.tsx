import { router } from 'expo-router';
import { useCallback } from 'react';
import { StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { spacing } from '../../components/theme';
import { useApiResource } from '../../hooks/useApiResource';
import { formatMoney } from '../../utils/money';
import { appraisalHistory } from './api';
import { levelTone, plural } from './labels';
import type { AppraisalSnapshot } from './types';

const basisText: Record<AppraisalSnapshot['basis'], (snapshot: AppraisalSnapshot) => string> = {
  Sales: (snapshot) => `from ${plural(snapshot.saleComps, 'sale')}`,
  Guide: () => 'from a price guide',
  Asking: (snapshot) => `from ${plural(snapshot.askingComps, 'listing')}, no sales`,
  Estimate: () => 'model estimate, no comps',
  None: () => 'no comps found',
};

/** What the item is expected to sell for, and when that was last checked against comps. */
export function ItemPricingCard({ itemId, expectedPrice }: { itemId: string; expectedPrice: number | null }) {
  const history = useApiResource(useCallback((signal: AbortSignal) => appraisalHistory(itemId, signal), [itemId]));
  const latest = history.data?.[0] ?? null;

  return (
    <Card>
      <View style={styles.row}>
        <View style={styles.flex}>
          <Text variant="label">Expected price</Text>
          <Text variant="metricSmall">{expectedPrice !== null ? formatMoney(expectedPrice) : 'Not set'}</Text>
        </View>
        {latest ? <StatusPill label={`${latest.level} · ${latest.confidence}`} tone={levelTone[latest.level]} size="sm" /> : null}
      </View>

      {latest ? (
        <Text variant="caption">
          Last appraised {formatMoney(latest.list)} on {new Date(latest.appraisedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })},{' '}
          {basisText[latest.basis](latest)}.
        </Text>
      ) : history.isLoading ? null : (
        <Text variant="caption">Not appraised yet. Price it from comps to check the expected price.</Text>
      )}
      {history.error ? <Text variant="caption">{history.error}</Text> : null}

      <View style={styles.actions}>
        <Button
          label="Price it now"
          icon="pricetags-outline"
          size="sm"
          onPress={() => router.push({ pathname: '/scanner', params: { mode: 'item', inventoryItemId: itemId } })}
        />
        <Button
          label="Check the deal"
          icon="calculator-outline"
          variant="secondary"
          size="sm"
          onPress={() => router.push({ pathname: '/deal-analyzer', params: { inventoryItemId: itemId } })}
        />
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  actions: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginTop: spacing.xs,
  },
});
