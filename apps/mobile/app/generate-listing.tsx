import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import type { GeneratedListing } from '../src/api/marketplace';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import type { InventoryItem } from '../src/domain/types';
import {
  describeListingCopyOutcome,
  saveListingCopy,
  type ListingCopySaveOutcome,
} from '../src/services/saveListingCopy';

const money = (value: number | null | undefined) =>
  typeof value === 'number' ? `$${value.toFixed(2)}` : '—';

export default function GenerateListingScreen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const c = useColors();
  const [item, setItem] = useState<InventoryItem | null>(null);
  const [listing, setListing] = useState<GeneratedListing | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState<ListingCopySaveOutcome | null>(null);

  const generate = useCallback(async (inv: InventoryItem) => {
    setLoading(true);
    setError(null);
    setSaved(null);
    try {
      const result = await api.generateListing({
        title: inv.title,
        brand: inv.brand,
        model: inv.model,
        category: inv.category,
        condition: inv.condition,
        color: inv.colors?.join(', ') || null,
        sizeSystem: inv.sizeSystem,
        sizeValue: inv.sizeValue,
        attributesJson: inv.attributesJson,
        notes: inv.notes,
      });
      setListing(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not generate a listing. Try again.');
    } finally {
      setLoading(false);
    }
  }, []);

  const save = useCallback(async (inv: InventoryItem, generated: GeneratedListing) => {
    setSaving(true);
    setError(null);
    try {
      setSaved(
        await saveListingCopy({
          inventoryItemId: inv.id,
          title: generated.listingTitle,
          description: generated.listingDescription,
          // Only used if the item has no listing yet and one has to be created to hold the copy.
          listPrice:
            generated.suggestedListPrice ??
            inv.estimatedSalePrice ??
            inv.sellerAskingPrice ??
            inv.purchasePrice ??
            0,
        }),
      );
    } finally {
      setSaving(false);
    }
  }, []);

  useEffect(() => {
    const id = params.id;
    if (!id) return;
    let active = true;
    api
      .inventoryItem(id)
      .then((inv) => {
        if (!active) return;
        setItem(inv);
        void generate(inv);
      })
      .catch((e) => {
        if (active) setError(e instanceof Error ? e.message : 'Could not load the item.');
      });
    return () => {
      active = false;
    };
  }, [params.id, generate]);

  return (
    <Screen>
      <View style={[styles.header, { backgroundColor: c.primaryLight }]}>
        <Ionicons name="document-text-outline" size={18} color={c.primary} />
        <Text variant="bodyBold" numberOfLines={1} style={styles.headerText}>
          {item?.title ?? 'Write listing'}
        </Text>
      </View>

      {error ? <ErrorBanner message={error} /> : null}

      {loading ? (
        <Card>
          <View style={styles.loading}>
            <ActivityIndicator color={c.primary} />
            <Text variant="caption" color={c.textMuted} style={{ marginTop: spacing.sm }}>
              Writing your listing and pricing from comps…
            </Text>
          </View>
        </Card>
      ) : listing ? (
        <>
          <Card>
            <SectionHeader title="Title" />
            <Text selectable variant="bodyBold" style={{ marginTop: spacing.xs }}>
              {listing.listingTitle}
            </Text>
            <Text variant="caption" color={c.textMuted} style={{ marginTop: spacing.xs }}>
              {listing.listingTitle.length}/80 chars · long-press to copy
            </Text>
          </Card>

          <Card>
            <SectionHeader title="Description" />
            <Text selectable color={c.textSecondary} style={{ marginTop: spacing.xs }}>
              {listing.listingDescription}
            </Text>
          </Card>

          <Card>
            <SectionHeader title={listing.priceBasis === 'Estimate' ? 'Rough estimate' : 'List at'} />
            <Text variant="metric" style={{ marginTop: spacing.xs }}>{money(listing.suggestedListPrice)}</Text>
            {(listing.estimatedResaleLow != null || listing.estimatedResaleHigh != null) && (
              <Text variant="caption" color={c.textMuted} style={{ marginTop: spacing.xs }}>
                Quick sale {money(listing.estimatedResaleLow)} · High {money(listing.estimatedResaleHigh)}
                {listing.confidence != null ? ` · confidence ${listing.confidence}` : ''}
              </Text>
            )}
            {listing.priceRationale ? (
              <Text variant="caption" color={c.textSecondary} style={{ marginTop: spacing.xs }}>
                {listing.priceRationale}
              </Text>
            ) : null}
          </Card>

          {listing.comps.length > 0 ? (
            <Card>
              <SectionHeader title="Comps" />
              {listing.comps.map((comp, index) => (
                <View key={index} style={[styles.compRow, { borderTopColor: c.border, borderTopWidth: index === 0 ? 0 : StyleSheet.hairlineWidth }]}>
                  <Text variant="bodyBold">{money(comp.price)}</Text>
                  <Text variant="caption" color={c.textMuted} numberOfLines={1} style={styles.compNote}>
                    {comp.status === 'sold' ? 'Sold' : 'Listed'}
                    {comp.note ? ` · ${comp.note}` : ''}
                    {comp.title ? ` · ${comp.title}` : ''}
                  </Text>
                </View>
              ))}
            </Card>
          ) : null}

          <Button
            label={saving ? 'Saving…' : 'Save to listing'}
            icon="bookmark-outline"
            disabled={loading || saving || !item}
            onPress={() => item && void save(item, listing)}
          />

          {saved ? (
            <Text
              variant="caption"
              color={saved.status === 'rejected' ? c.danger : c.textMuted}
              style={styles.outcome}
            >
              {describeListingCopyOutcome(saved)}
            </Text>
          ) : null}

          <Button
            label="Regenerate"
            icon="refresh-outline"
            variant="secondary"
            disabled={loading || saving}
            onPress={() => item && void generate(item)}
          />
        </>
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: spacing.sm,
    marginBottom: spacing.md,
  },
  headerText: { flex: 1 },
  outcome: { marginTop: -spacing.xs, marginBottom: spacing.xs, textAlign: 'center' },
  loading: { alignItems: 'center', paddingVertical: spacing.xl },
  compRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
    paddingVertical: spacing.sm,
  },
  compNote: { flexShrink: 1, textAlign: 'right' },
});
