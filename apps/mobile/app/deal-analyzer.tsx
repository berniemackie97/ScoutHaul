import { Ionicons } from '@expo/vector-icons';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { DecisionCard } from '../src/components/DecisionCard';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { OfflineBanner } from '../src/components/OfflineBanner';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import { useHaptics } from '../src/hooks/useHaptics';
import type { MarketAnalysisResponse, PricingBasisType } from '../src/domain/types';
import { useConnectivity } from '../src/hooks/useConnectivity';
import { parseOptionalNumber } from '../src/utils/money';

type PricingBasisSelection = PricingBasisType | 'Auto';

const basisLabels: Record<PricingBasisSelection, string> = {
  Auto: 'Auto',
  ManualEstimate: 'Manual',
  InternalHistory: 'Internal',
  CachedRecent: 'Cached',
  CachedStale: 'Stale Cache',
  LiveValidated: 'Live',
  Unavailable: 'None',
};

export default function DealAnalyzerScreen() {
  const params = useLocalSearchParams<{
    inventoryItemId?: string;
    productIdentityId?: string;
    askingPrice?: string;
    expectedSalePrice?: string;
    query?: string;
    barcode?: string;
  }>();
  const c = useColors();
  const haptics = useHaptics();
  const connectivity = useConnectivity();

  const [expectedSalePrice, setExpectedSalePrice] = useState(params.expectedSalePrice ?? '');
  const [askingPrice, setAskingPrice] = useState(params.askingPrice ?? '');
  const [platformFeePercent, setPlatformFeePercent] = useState('13');
  const [paymentFeePercent, setPaymentFeePercent] = useState('3');
  const [fixedPaymentFee, setFixedPaymentFee] = useState('0.30');
  const [shipping, setShipping] = useState('');
  const [supplies, setSupplies] = useState('1.50');
  const [riskReserve, setRiskReserve] = useState('5');
  const [desiredProfit, setDesiredProfit] = useState('20');
  const [targetMargin, setTargetMargin] = useState('45');
  const [pricingBasis, setPricingBasis] = useState<PricingBasisSelection>('Auto');
  const [result, setResult] = useState<MarketAnalysisResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);

  const itemContext = params.query || params.barcode;

  async function analyze() {
    const salePrice = parseOptionalNumber(expectedSalePrice);
    if (salePrice === null && !params.productIdentityId && !params.inventoryItemId) {
      setError('Enter an expected sale price to run analysis.');
      return;
    }

    try {
      setIsLoading(true);
      setError(null);
      const response = await api.analyze({
        productIdentityId: params.productIdentityId ?? null,
        inventoryItemId: params.inventoryItemId ?? null,
        expectedSalePrice: salePrice,
        sellerAskingPrice: parseOptionalNumber(askingPrice),
        purchasePrice: null,
        platform: 'eBay',
        platformFeePercent: parseOptionalNumber(platformFeePercent) ?? 0,
        paymentFeePercent: parseOptionalNumber(paymentFeePercent) ?? 0,
        fixedPaymentFee: parseOptionalNumber(fixedPaymentFee) ?? 0,
        estimatedShippingCostPaidBySeller: parseOptionalNumber(shipping) ?? 0,
        suppliesCost: parseOptionalNumber(supplies) ?? 0,
        riskReserve: parseOptionalNumber(riskReserve) ?? 0,
        desiredMinimumProfit: parseOptionalNumber(desiredProfit) ?? 0,
        targetMarginPercent: parseOptionalNumber(targetMargin),
        pricingBasis: pricingBasis === 'Auto' ? null : pricingBasis,
        condition: null,
        riskFlags: [],
      });
      setResult(response);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Analysis unavailable.');
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <Screen>
      {!connectivity.isOnline ? (
        <OfflineBanner message="Offline. Enter expected sale price for manual analysis." />
      ) : null}

      {/* Item context from scanner/search */}
      {itemContext ? (
        <View style={[styles.contextBar, { backgroundColor: c.primaryLight }]}>
          <Ionicons name="analytics-outline" size={18} color={c.primary} />
          <View style={styles.contextText}>
            <Text variant="bodyBold" numberOfLines={1}>{params.query ?? 'Deal Check'}</Text>
            {params.barcode ? (
              <Text variant="label" color={c.textMuted}>{params.barcode}</Text>
            ) : null}
          </View>
        </View>
      ) : null}

      <Card>
        <TextField
          label="Expected sale price"
          value={expectedSalePrice}
          onChangeText={setExpectedSalePrice}
          keyboardType="decimal-pad"
          prefix="$"
          placeholder="0.00"
          hint={params.expectedSalePrice ? 'Pre-filled from marketplace avg' : 'What you expect to sell this item for'}
        />
        <TextField
          label="Seller asking price"
          value={askingPrice}
          onChangeText={setAskingPrice}
          keyboardType="decimal-pad"
          prefix="$"
          placeholder="0.00"
          hint="What the seller wants for this item"
        />
      </Card>

      <Pressable onPress={() => setShowAdvanced(!showAdvanced)} style={styles.advancedToggle}>
        <Text variant="caption" color={c.primary}>
          {showAdvanced ? 'Hide fee details' : 'Adjust fees & costs'}
        </Text>
        <Ionicons name={showAdvanced ? 'chevron-up' : 'chevron-down'} size={16} color={c.primary} />
      </Pressable>

      {showAdvanced ? (
        <Card>
          <View style={styles.row}>
            <TextField
              label="Platform fee %"
              value={platformFeePercent}
              onChangeText={setPlatformFeePercent}
              keyboardType="decimal-pad"
              containerStyle={styles.flex}
            />
            <TextField
              label="Payment fee %"
              value={paymentFeePercent}
              onChangeText={setPaymentFeePercent}
              keyboardType="decimal-pad"
              containerStyle={styles.flex}
            />
          </View>
          <View style={styles.row}>
            <TextField
              label="Fixed payment fee"
              value={fixedPaymentFee}
              onChangeText={setFixedPaymentFee}
              keyboardType="decimal-pad"
              prefix="$"
              containerStyle={styles.flex}
            />
            <TextField
              label="Shipping cost"
              value={shipping}
              onChangeText={setShipping}
              keyboardType="decimal-pad"
              prefix="$"
              containerStyle={styles.flex}
            />
          </View>
          <View style={styles.row}>
            <TextField
              label="Supplies"
              value={supplies}
              onChangeText={setSupplies}
              keyboardType="decimal-pad"
              prefix="$"
              containerStyle={styles.flex}
            />
            <TextField
              label="Risk reserve"
              value={riskReserve}
              onChangeText={setRiskReserve}
              keyboardType="decimal-pad"
              prefix="$"
              containerStyle={styles.flex}
            />
          </View>
          <View style={styles.row}>
            <TextField
              label="Min profit"
              value={desiredProfit}
              onChangeText={setDesiredProfit}
              keyboardType="decimal-pad"
              prefix="$"
              containerStyle={styles.flex}
            />
            <TextField
              label="Target margin %"
              value={targetMargin}
              onChangeText={setTargetMargin}
              keyboardType="decimal-pad"
              containerStyle={styles.flex}
            />
          </View>

          <SectionHeader title="Pricing Basis" />
          <View style={styles.basisRow}>
            {(Object.keys(basisLabels) as PricingBasisSelection[]).map((b) => {
              const isActive = b === pricingBasis;
              return (
                <Pressable
                  key={b}
                  onPress={() => setPricingBasis(b)}
                  accessibilityRole="button"
                  accessibilityState={{ selected: isActive }}
                  style={[
                    styles.basisChip,
                    { backgroundColor: isActive ? c.primary : c.surfaceAlt, borderColor: isActive ? c.primary : c.border },
                  ]}
                >
                  <Text
                    variant="caption"
                    color={isActive ? c.textInverse : c.textSecondary}
                    style={styles.basisChipText}
                  >
                    {basisLabels[b]}
                  </Text>
                </Pressable>
              );
            })}
          </View>
        </Card>
      ) : null}

      {error ? <ErrorBanner message={error} /> : null}

      <Button
        label={isLoading ? 'Running Analysis...' : 'Run Deal Check'}
        icon="calculator-outline"
        size="lg"
        fullWidth
        disabled={isLoading}
        onPress={() => void analyze()}
      />

      {result ? (
        <>
          <SectionHeader title="Recommendation" />
          <DecisionCard
            decision={result.recommendation.decision}
            maxBuyPrice={result.recommendation.recommendedMaxBuyPrice}
            sellerAskingPrice={parseOptionalNumber(askingPrice)}
            expectedSalePrice={result.estimatedSalePrice}
            expectedNetProfit={result.recommendation.expectedNetProfit}
            roiPercent={result.recommendation.roiPercent}
            marginPercent={result.recommendation.marginPercent}
            confidenceScore={result.confidence.score}
            pricingBasis={result.pricingBasis}
            warnings={[...result.confidence.warnings, ...result.recommendation.warnings]}
          />

          {result.confidence.reasons.length > 0 ? (
            <Card>
              <Text variant="label">Confidence Factors</Text>
              {result.confidence.reasons.map((r, i) => (
                <View key={i} style={styles.reasonRow}>
                  <Ionicons name="information-circle-outline" size={14} color={c.textMuted} />
                  <Text variant="caption">{r}</Text>
                </View>
              ))}
            </Card>
          ) : null}

          {/* The screen answered "should I buy this" and then stopped, so the only way to act on a
              Buy was to back out and start the item again by hand — retyping the price it had just
              been given. It carries what it knows straight into the item. */}
          <Button
            label="Add to inventory"
            icon="add-circle-outline"
            fullWidth
            onPress={() => {
              haptics.success();
              router.push({
                pathname: '/inventory/edit',
                params: {
                  ...(params.query ? { title: params.query } : {}),
                  ...(params.barcode ? { barcode: params.barcode } : {}),
                  ...(askingPrice.trim() ? { purchasePrice: askingPrice.trim() } : {}),
                  ...(expectedSalePrice.trim() ? { estimatedSalePrice: expectedSalePrice.trim() } : {}),
                },
              });
            }}
          />
        </>
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  contextBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    borderRadius: radius.lg,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
  },
  contextText: {
    flex: 1,
    gap: spacing.xxs,
  },
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
  advancedToggle: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.xs,
    alignSelf: 'flex-start',
  },
  basisRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  basisChip: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.full,
    borderWidth: 1,
  },
  basisChipText: {
    fontWeight: '600',
  },
  reasonRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
});
