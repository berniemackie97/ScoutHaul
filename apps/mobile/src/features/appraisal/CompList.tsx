import { Ionicons } from '@expo/vector-icons';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import type { ToneKey } from '../../components/theme';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { formatMoney } from '../../utils/money';
import { compDateLabel, compKindLabel, plural } from './labels';
import type { Appraisal, Comp, CompKind, CompSourceReport } from './types';

const kindTone: Record<CompKind, ToneKey> = {
  YourSale: 'success',
  Sold: 'info',
  Asking: 'neutral',
  Guide: 'primary',
};

export function CompList({ comps }: { comps: Comp[] }) {
  const { colors: c } = useAppTheme();
  const [showExcluded, setShowExcluded] = useState(false);
  const included = comps.filter((comp) => comp.included);
  const excluded = comps.filter((comp) => !comp.included);

  if (comps.length === 0) return null;

  return (
    <Card noPadding>
      <View style={styles.header}>
        <Text variant="bodyBold">Comps</Text>
        <Text variant="caption">{included.length} used for the price</Text>
      </View>
      {included.map((comp, index) => (
        <CompRow key={`${comp.url ?? comp.title}-${index}`} comp={comp} />
      ))}
      {excluded.length > 0 ? (
        <>
          <Pressable
            onPress={() => setShowExcluded((shown) => !shown)}
            accessibilityRole="button"
            accessibilityState={{ expanded: showExcluded }}
            style={[styles.toggle, { borderTopColor: c.borderLight }]}
          >
            <Text variant="caption" color={c.primary} style={styles.flex}>
              {showExcluded ? 'Hide' : 'Show'} {plural(excluded.length, 'comp')} left out
            </Text>
            <Ionicons name={showExcluded ? 'chevron-up' : 'chevron-down'} size={16} color={c.primary} />
          </Pressable>
          {showExcluded ? excluded.map((comp, index) => <CompRow key={`x-${comp.url ?? comp.title}-${index}`} comp={comp} />) : null}
        </>
      ) : null}
    </Card>
  );
}

function CompRow({ comp }: { comp: Comp }) {
  const { colors: c } = useAppTheme();
  const meta = [comp.source, comp.condition, compDateLabel(comp)].filter(Boolean).join(' · ');

  return (
    <Pressable
      onPress={comp.url ? () => void WebBrowser.openBrowserAsync(comp.url!) : undefined}
      disabled={!comp.url}
      accessibilityRole={comp.url ? 'link' : undefined}
      accessibilityLabel={`${compKindLabel[comp.kind]} ${formatMoney(comp.price)}, ${comp.title}`}
      style={({ pressed }) => [
        styles.row,
        { borderTopColor: c.borderLight, opacity: comp.included ? 1 : 0.6 },
        pressed && { backgroundColor: c.surfaceAlt },
      ]}
    >
      <View style={styles.flex}>
        <View style={styles.badges}>
          <StatusPill label={compKindLabel[comp.kind]} tone={kindTone[comp.kind]} size="sm" />
          {!comp.verified ? <Text variant="caption" color={c.textMuted}>Unchecked</Text> : null}
        </View>
        <Text variant="body" numberOfLines={2}>{comp.title}</Text>
        {meta ? <Text variant="caption" color={c.textMuted} numberOfLines={2}>{meta}</Text> : null}
        {!comp.included && comp.excludedBecause ? (
          <Text variant="caption" color={c.warningDark}>Left out: {comp.excludedBecause}</Text>
        ) : null}
      </View>
      <View style={styles.priceColumn}>
        <Text variant="bodyBold">{formatMoney(comp.price)}</Text>
        {comp.shipping ? <Text variant="caption" color={c.textMuted}>+{formatMoney(comp.shipping)} ship</Text> : null}
        {comp.url ? <Ionicons name="open-outline" size={14} color={c.textMuted} /> : null}
      </View>
    </Pressable>
  );
}

const statusText: Record<CompSourceReport['status'], (report: CompSourceReport) => string> = {
  Found: (report) => (report.count > 0 ? plural(report.count, 'comp') : 'Estimate only'),
  Empty: () => 'Nothing found',
  Failed: (report) => report.message ?? 'Failed',
  Off: () => 'Not set up',
};

export function SourceReport({ appraisal }: { appraisal: Appraisal }) {
  const { colors: c } = useAppTheme();
  return (
    <Card>
      <Text variant="bodyBold">Where we looked</Text>
      {appraisal.sources.map((report) => (
        <View key={report.name} style={styles.sourceRow}>
          <Text variant="caption" color={c.text} style={styles.flex}>{report.name}</Text>
          <Text
            variant="caption"
            color={report.status === 'Failed' ? c.warningDark : report.status === 'Found' ? c.success : c.textMuted}
            style={styles.sourceStatus}
          >
            {statusText[report.status](report)}
          </Text>
        </View>
      ))}
    </Card>
  );
}

export function SearchLinks({ appraisal }: { appraisal: Appraisal }) {
  const { colors: c } = useAppTheme();
  if (appraisal.searchLinks.length === 0) return null;

  return (
    <Card>
      <Text variant="bodyBold">Check it yourself</Text>
      <Text variant="caption">Searches for “{appraisal.searchText}”.</Text>
      <View style={styles.links}>
        {appraisal.searchLinks.map((link) => (
          <Pressable
            key={link.label}
            onPress={() => void WebBrowser.openBrowserAsync(link.url)}
            accessibilityRole="link"
            style={({ pressed }) => [styles.link, { borderColor: c.border }, pressed && { backgroundColor: c.surfaceAlt }]}
          >
            <Text variant="caption" color={c.text}>{link.label}</Text>
            <Ionicons name="open-outline" size={12} color={c.textMuted} />
          </Pressable>
        ))}
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'baseline',
    padding: spacing.lg,
    paddingBottom: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    gap: spacing.md,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  badges: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: spacing.xxs,
  },
  priceColumn: {
    alignItems: 'flex-end',
    gap: spacing.xxs,
  },
  toggle: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
    borderTopWidth: StyleSheet.hairlineWidth,
    minHeight: 44,
  },
  sourceRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  sourceStatus: {
    flexShrink: 1,
    textAlign: 'right',
  },
  links: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  link: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    borderWidth: 1,
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    minHeight: 36,
  },
});
