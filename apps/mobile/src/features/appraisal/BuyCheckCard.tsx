import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import type { ToneKey } from '../../components/theme';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { formatMoney, parseOptionalNumber } from '../../utils/money';
import { defaultBuyingRules, loadBuyingRules, saveBuyingRules, type BuyingRules } from './buyingRules';
import type { BuyCheckRequest, BuyDecision, PriceBasis } from './types';
import { useBuyCheck } from './useBuyCheck';

const decisionLabel: Record<BuyDecision, { label: string; tone: ToneKey }> = {
  Buy: { label: 'Buy', tone: 'success' },
  Negotiate: { label: 'Negotiate', tone: 'warning' },
  Pass: { label: 'Pass', tone: 'danger' },
  ResearchRequired: { label: 'Check comps first', tone: 'neutral' },
  Unavailable: { label: 'No price', tone: 'neutral' },
};

type BuyCheckCardProps = {
  expectedSalePrice: number;
  basis?: PriceBasis | null;
  confidence?: number | null;
  askingPrice: string;
  onAskingPriceChange: (value: string) => void;
};

export function BuyCheckCard({ expectedSalePrice, basis = null, confidence = null, askingPrice: asking, onAskingPriceChange }: BuyCheckCardProps) {
  const { colors: c } = useAppTheme();
  const [channel, setChannel] = useState<string | null>(null);
  const [rules, setRules] = useState<BuyingRules | null>(null);
  const [editingRules, setEditingRules] = useState(false);

  useEffect(() => {
    let active = true;
    void loadBuyingRules().then((loaded) => active && setRules(loaded));
    return () => {
      active = false;
    };
  }, []);

  const askingPrice = parseOptionalNumber(asking);
  const request = useMemo<BuyCheckRequest | null>(
    () =>
      rules && expectedSalePrice > 0
        ? {
            expectedSalePrice,
            askingPrice: askingPrice !== null && askingPrice >= 0 ? askingPrice : null,
            channel,
            shippingCost: null,
            suppliesCost: null,
            minimumProfit: rules.minimumProfit,
            targetMarginPercent: rules.targetMarginPercent,
            confidence,
            basis,
          }
        : null,
    [rules, expectedSalePrice, askingPrice, channel, confidence, basis],
  );
  const { check, error, loading } = useBuyCheck(request);
  const decision = check && check.askingPrice !== null ? decisionLabel[check.decision] : null;

  return (
    <Card>
      <View style={styles.titleRow}>
        <Text variant="bodyBold" style={styles.flex}>Buy check</Text>
        {loading ? <ActivityIndicator size="small" color={c.textMuted} /> : null}
        {decision ? <StatusPill label={decision.label} tone={decision.tone} /> : null}
      </View>

      <TextField
        label="Asking price"
        prefix="$"
        value={asking}
        onChangeText={onAskingPriceChange}
        keyboardType="decimal-pad"
        placeholder="What they want for it"
      />

      {error ? <Text variant="caption" color={c.danger}>{error}</Text> : null}

      {check ? (
        <>
          <View style={styles.headline}>
            <Text variant="label">Pay up to</Text>
            <Text variant="metric">{formatMoney(check.maxBuyPrice)}</Text>
            <Text variant="caption">selling on {check.channel.label} for {formatMoney(check.expectedSalePrice)}</Text>
          </View>

          {check.reasons.map((reason) => (
            <Text key={reason} variant="caption" color={c.textSecondary}>• {reason}</Text>
          ))}

          <View style={[styles.breakdown, { borderColor: c.borderLight }]}>
            <Line label="Fees" amount={check.channel.fees} from={check.channel.feesFrom} />
            <Line label="Shipping" amount={check.channel.shipping} from={check.channel.shippingFrom} />
            {check.channel.supplies > 0 ? <Line label="Supplies" amount={check.channel.supplies} from="Your average" /> : null}
            <Line label="You keep" amount={check.channel.netProceeds} strong />
          </View>

          {check.channels.length > 1 ? (
            <Text variant="caption" color={c.textMuted}>What you keep at {formatMoney(check.expectedSalePrice)} on each channel</Text>
          ) : null}
          {check.channels.length > 1 ? (
            <View style={styles.channels}>
              {check.channels.map((option) => {
                const selected = option.channel === check.channel.channel;
                return (
                  <Pressable
                    key={option.channel}
                    onPress={() => setChannel(option.channel)}
                    accessibilityRole="radio"
                    accessibilityState={{ selected }}
                    style={[
                      styles.channel,
                      { borderColor: selected ? c.primary : c.border, backgroundColor: selected ? c.primaryLight : c.surface },
                    ]}
                  >
                    <Text variant="caption" color={c.text}>{option.label}</Text>
                    <Text variant="bodyBold">{formatMoney(option.netProceeds)}</Text>
                  </Pressable>
                );
              })}
            </View>
          ) : null}
        </>
      ) : null}

      {rules ? (
        editingRules ? (
          <RulesEditor
            rules={rules}
            onCancel={() => setEditingRules(false)}
            onSave={(next) => {
              setRules(next);
              setEditingRules(false);
              void saveBuyingRules(next);
            }}
          />
        ) : (
          <Pressable onPress={() => setEditingRules(true)} accessibilityRole="button" hitSlop={8} style={styles.rules}>
            <Text variant="caption" color={c.textMuted} style={styles.flex}>
              Your rule: at least {formatMoney(rules.minimumProfit)} profit
              {rules.targetMarginPercent !== null ? ` and a ${rules.targetMarginPercent}% margin` : ''}
            </Text>
            <Text variant="caption" color={c.primary}>Change</Text>
          </Pressable>
        )
      ) : null}
    </Card>
  );
}

function Line({ label, amount, from, strong }: { label: string; amount: number; from?: string; strong?: boolean }) {
  const { colors: c } = useAppTheme();
  return (
    <View style={styles.line}>
      <View style={styles.flex}>
        <Text variant={strong ? 'bodyBold' : 'body'}>{label}</Text>
        {from ? <Text variant="caption" color={c.textMuted}>{from}</Text> : null}
      </View>
      <Text variant={strong ? 'bodyBold' : 'body'}>{formatMoney(amount)}</Text>
    </View>
  );
}

function RulesEditor({ rules, onSave, onCancel }: { rules: BuyingRules; onSave: (rules: BuyingRules) => void; onCancel: () => void }) {
  const [profit, setProfit] = useState(String(rules.minimumProfit));
  const [margin, setMargin] = useState(rules.targetMarginPercent === null ? '' : String(rules.targetMarginPercent));
  const parsedProfit = parseOptionalNumber(profit);
  const parsedMargin = parseOptionalNumber(margin);
  const valid = parsedProfit !== null && parsedProfit >= 0 && (parsedMargin === null || (parsedMargin > 0 && parsedMargin < 100));

  return (
    <View style={styles.rulesEditor}>
      <TextField label="Least profit worth buying for" prefix="$" value={profit} onChangeText={setProfit} keyboardType="decimal-pad" />
      <TextField
        label="Target margin (optional)"
        value={margin}
        onChangeText={setMargin}
        keyboardType="decimal-pad"
        placeholder="e.g. 40"
        hint="Percent of the sale price you want to keep as profit."
      />
      <View style={styles.rulesActions}>
        <Button label="Reset" variant="ghost" size="sm" onPress={() => onSave(defaultBuyingRules)} />
        <Button label="Cancel" variant="ghost" size="sm" onPress={onCancel} />
        <Button
          label="Save"
          size="sm"
          disabled={!valid}
          onPress={() => onSave({ minimumProfit: parsedProfit ?? 0, targetMarginPercent: parsedMargin })}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  titleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  headline: {
    gap: spacing.xxs,
    marginTop: spacing.xs,
  },
  breakdown: {
    borderTopWidth: StyleSheet.hairlineWidth,
    paddingTop: spacing.sm,
    gap: spacing.sm,
  },
  line: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  channels: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  channel: {
    borderWidth: 1,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    minWidth: 96,
  },
  rules: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    minHeight: 32,
  },
  rulesEditor: {
    gap: spacing.md,
  },
  rulesActions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: spacing.sm,
  },
});
