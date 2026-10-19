import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { ProgressBar } from '../../components/ProgressBar';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { formatMoney } from '../../utils/money';
import { basisLabel, levelTone } from './labels';
import type { Appraisal } from './types';

const REASONS_SHOWN = 3;

export function PriceSummary({ appraisal }: { appraisal: Appraisal }) {
  const { colors: c, tones } = useAppTheme();
  const [showAllReasons, setShowAllReasons] = useState(false);
  const { price } = appraisal;
  const reasons = showAllReasons ? price.reasons : price.reasons.slice(0, REASONS_SHOWN);
  const tone = tones[levelTone[price.level]];
  // A model's guess never wears the same headline as a price backed by comps.
  const headline = price.basis === 'Estimate' ? 'Rough estimate' : 'List at';

  return (
    <Card>
      {price.list !== null ? (
        <>
          <Text variant="label">{headline}</Text>
          <Text variant="hero" accessibilityLabel={`${headline} ${formatMoney(price.list)}`}>
            {formatMoney(price.list)}
          </Text>
          <View style={styles.range}>
            <Figure label="Quick sale" value={price.quickSale} />
            <Figure label="High" value={price.high} />
          </View>
        </>
      ) : (
        <>
          <Text variant="subtitle">No price yet</Text>
          <Text variant="caption">Nothing matched well enough to price from. Check the searches below and add what you find.</Text>
        </>
      )}

      <View style={styles.basisRow}>
        <Text variant="caption" style={styles.flex}>{basisLabel(price, appraisal.comps)}</Text>
        {price.level !== 'None' ? (
          <StatusPill label={`${price.level} · ${price.confidence}`} tone={levelTone[price.level]} size="sm" />
        ) : null}
      </View>
      {price.level !== 'None' ? <ProgressBar progress={price.confidence / 100} color={tone.border} height={6} /> : null}

      {price.basis === 'Estimate' && appraisal.estimate?.rationale ? (
        <Text variant="caption" color={c.textSecondary}>{appraisal.estimate.rationale}</Text>
      ) : null}

      <View style={styles.reasons}>
        {reasons.map((reason) => (
          <Text key={reason} variant="caption" color={c.textSecondary}>• {reason}</Text>
        ))}
        {price.reasons.length > REASONS_SHOWN ? (
          <Pressable onPress={() => setShowAllReasons((shown) => !shown)} hitSlop={8} accessibilityRole="button">
            <Text variant="caption" color={c.primary}>
              {showAllReasons ? 'Show less' : `Why this price (${price.reasons.length - REASONS_SHOWN} more)`}
            </Text>
          </Pressable>
        ) : null}
      </View>
    </Card>
  );
}

export function PricingInProgress() {
  const { colors: c } = useAppTheme();
  return (
    <Card>
      <View style={styles.progress}>
        <ActivityIndicator color={c.primary} />
        <View style={styles.flex}>
          <Text variant="bodyBold">Finding comps</Text>
          <Text variant="caption">Checking your sales, saved comps and recent listings on the web. This can take up to half a minute.</Text>
        </View>
      </View>
    </Card>
  );
}

function Figure({ label, value }: { label: string; value: number | null }) {
  return (
    <View style={styles.figure}>
      <Text variant="label">{label}</Text>
      <Text variant="bodyBold">{formatMoney(value)}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  range: {
    flexDirection: 'row',
    gap: spacing.xl,
  },
  figure: {
    gap: spacing.xxs,
  },
  basisRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
  reasons: {
    gap: spacing.xs,
    marginTop: spacing.xs,
  },
  progress: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
});
