import { Ionicons } from '@expo/vector-icons';
import { StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing, type ColorPalette } from './theme';
import { useAppTheme } from '../context/ThemeContext';
import type { RecommendationDecisionType } from '../domain/types';

type DecisionCardProps = {
  decision: RecommendationDecisionType;
  maxBuyPrice?: number | null;
  sellerAskingPrice?: number | null;
  expectedSalePrice?: number | null;
  expectedNetProfit?: number | null;
  roiPercent?: number | null;
  marginPercent?: number | null;
  confidenceScore?: number;
  pricingBasis?: string;
  warnings?: string[];
};

type DecisionTheme = {
  bg: string;
  accent: string;
  text: string;
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
};

function buildDecisionThemes(c: ColorPalette): Record<RecommendationDecisionType, DecisionTheme> {
  return {
    Buy: { bg: c.buyGreenBg, accent: c.buyGreen, text: c.successDark, icon: 'checkmark-circle', label: 'Buy' },
    Negotiate: { bg: c.negotiateAmberBg, accent: c.negotiateAmber, text: c.warningDark, icon: 'chatbubble-ellipses', label: 'Negotiate' },
    Pass: { bg: c.passRedBg, accent: c.passRed, text: c.dangerDark, icon: 'close-circle', label: 'Pass' },
    ResearchRequired: { bg: c.researchBlueBg, accent: c.researchBlue, text: c.info, icon: 'search', label: 'Research Required' },
    Unavailable: { bg: c.unavailableGrayBg, accent: c.unavailableGray, text: c.textSecondary, icon: 'help-circle', label: 'Unavailable' },
  };
}

function fmt(v?: number | null): string {
  if (v === undefined || v === null || Number.isNaN(v)) return '-';
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(v);
}

export function DecisionCard({
  decision,
  maxBuyPrice,
  sellerAskingPrice,
  expectedSalePrice,
  expectedNetProfit,
  roiPercent,
  marginPercent,
  confidenceScore,
  pricingBasis,
  warnings,
}: DecisionCardProps) {
  const { colors: c, shadows: s } = useAppTheme();
  const themes = buildDecisionThemes(c);
  const theme = themes[decision];
  const hasProfit = expectedNetProfit !== null && expectedNetProfit !== undefined;

  return (
    <View style={[styles.card, { borderColor: theme.accent, backgroundColor: c.surface, ...s.md }]}>
      <View style={[styles.decisionHeader, { backgroundColor: theme.bg }]}>
        <Ionicons name={theme.icon} size={28} color={theme.accent} />
        <Text variant="title" color={theme.text}>
          {theme.label}
        </Text>
        {confidenceScore !== undefined ? (
          <View style={[styles.confidenceBadge, { backgroundColor: theme.accent }]}>
            <Text variant="caption" color={c.textInverse} style={styles.confidenceText}>
              {confidenceScore}/100
            </Text>
          </View>
        ) : null}
      </View>

      <View style={styles.metricsGrid}>
        <MetricRow label="Max buy price" value={fmt(maxBuyPrice)} highlight bgColor={c.surfaceAlt} />
        <MetricRow label="Seller asking" value={fmt(sellerAskingPrice)} />
        <MetricRow label="Expected sale" value={fmt(expectedSalePrice)} />
        <MetricRow
          label="Expected profit"
          value={fmt(expectedNetProfit)}
          valueColor={hasProfit && expectedNetProfit! > 0 ? c.success : hasProfit && expectedNetProfit! < 0 ? c.danger : undefined}
        />
        <MetricRow label="ROI" value={roiPercent !== null && roiPercent !== undefined ? `${roiPercent}%` : '-'} />
        <MetricRow label="Margin" value={marginPercent !== null && marginPercent !== undefined ? `${marginPercent}%` : '-'} />
      </View>

      {pricingBasis ? (
        <View style={styles.basisRow}>
          <Ionicons name="information-circle-outline" size={16} color={c.textMuted} />
          <Text variant="caption">Pricing basis: {formatBasis(pricingBasis)}</Text>
        </View>
      ) : null}

      {warnings && warnings.length > 0 ? (
        <View style={[styles.warnings, { borderTopColor: c.border }]}>
          {warnings.map((w, i) => (
            <View key={i} style={styles.warningRow}>
              <Ionicons name="warning-outline" size={14} color={c.warning} />
              <Text variant="caption" color={c.warningDark}>
                {w}
              </Text>
            </View>
          ))}
        </View>
      ) : null}
    </View>
  );
}

function MetricRow({ label, value, highlight, valueColor, bgColor }: { label: string; value: string; highlight?: boolean; valueColor?: string; bgColor?: string }) {
  return (
    <View style={[styles.metricRow, highlight && [styles.metricRowHighlight, bgColor ? { backgroundColor: bgColor } : null]]}>
      <Text variant="caption">{label}</Text>
      <Text variant={highlight ? 'metricSmall' : 'bodyBold'} color={valueColor}>
        {value}
      </Text>
    </View>
  );
}

function formatBasis(basis: string): string {
  const map: Record<string, string> = {
    LiveValidated: 'Live market data',
    CachedRecent: 'Cached (recent)',
    CachedStale: 'Cached (stale)',
    ManualEstimate: 'Manual estimate',
    InternalHistory: 'Internal sales history',
    Unavailable: 'No pricing data',
  };
  return map[basis] ?? basis;
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.lg,
    borderWidth: 2,
    overflow: 'hidden',
  },
  decisionHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: spacing.lg,
    gap: spacing.md,
  },
  confidenceBadge: {
    marginLeft: 'auto',
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  confidenceText: {
    fontWeight: '800',
  },
  metricsGrid: {
    padding: spacing.lg,
    gap: spacing.sm,
  },
  metricRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: spacing.xs,
  },
  metricRowHighlight: {
    marginHorizontal: -spacing.sm,
    paddingHorizontal: spacing.sm,
    borderRadius: radius.sm,
    paddingVertical: spacing.sm,
  },
  basisRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.md,
  },
  warnings: {
    borderTopWidth: 1,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  warningRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
});
