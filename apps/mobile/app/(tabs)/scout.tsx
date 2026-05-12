import { Ionicons } from '@expo/vector-icons';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { OfflineBanner } from '../../src/components/OfflineBanner';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { enqueueCreateInventoryFromDraft, saveItemDraft } from '../../src/db/localStore';
import type { LocalItemDraft } from '../../src/domain/types';
import { useConnectivity } from '../../src/hooks/useConnectivity';
import { parseOptionalNumber } from '../../src/utils/money';

type QuickAction = {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  color: string;
  bg: string;
  onPress: () => void;
};

export default function ScoutScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const params = useLocalSearchParams<{ barcode?: string; query?: string }>();
  const connectivity = useConnectivity();
  const [title, setTitle] = useState(params.query ?? '');
  const [barcode, setBarcode] = useState(params.barcode ?? '');
  const [askingPrice, setAskingPrice] = useState('');
  const [condition, setCondition] = useState('');
  const [sourceName, setSourceName] = useState('');
  const [notes, setNotes] = useState('');
  const [savedDraft, setSavedDraft] = useState<LocalItemDraft | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [showDetails, setShowDetails] = useState(false);

  useEffect(() => {
    if (params.barcode) {
      setBarcode(params.barcode);
      if (!title) setTitle(`Barcode ${params.barcode}`);
      setShowDetails(true);
    }
    if (params.query) {
      setTitle(params.query);
      setShowDetails(true);
    }
  }, [params.barcode, params.query]);

  const quickActions: QuickAction[] = [
    {
      icon: 'barcode-outline',
      label: 'Scan\nBarcode',
      color: c.primary,
      bg: c.primaryLight,
      onPress: () => router.push('/scanner'),
    },
    {
      icon: 'search-outline',
      label: 'Search\nItem',
      color: c.info,
      bg: c.infoLight,
      onPress: () => router.push('/manual-search'),
    },
    {
      icon: 'open-outline',
      label: 'Market\nSearch',
      color: c.accent,
      bg: c.accentLight,
      onPress: () =>
        router.push({
          pathname: '/marketplace-links',
          params: { query: title || barcode },
        }),
    },
    {
      icon: 'add-circle-outline',
      label: 'Add\nComp',
      color: c.success,
      bg: c.successLight,
      onPress: () =>
        router.push({
          pathname: '/comp-capture',
          params: { title, barcode },
        }),
    },
  ];

  async function saveDraft(needsResearch = true) {
    const draft = await saveItemDraft({
      id: savedDraft?.id,
      title: title || (barcode ? `Barcode ${barcode}` : 'Untitled draft'),
      barcode: barcode || null,
      sellerAskingPrice: parseOptionalNumber(askingPrice),
      condition: condition || null,
      sourceName: sourceName || null,
      notes: notes || null,
      needsResearch,
      syncStatus: 'local',
    });
    setSavedDraft(draft);
    setMessage('Draft saved locally.');
    return draft;
  }

  async function addToInventory() {
    const draft = await saveDraft(false);
    if (!connectivity.isOnline) {
      await enqueueCreateInventoryFromDraft(draft);
      setMessage('Queued for sync when online.');
      return;
    }

    try {
      const item = await api.createInventory({
        title: draft.title,
        barcode: draft.barcode,
        sellerAskingPrice: draft.sellerAskingPrice,
        condition: draft.condition,
        notes: draft.notes,
      });
      setMessage('Added to inventory.');
      router.push(`/inventory/${item.id}`);
    } catch (err) {
      await enqueueCreateInventoryFromDraft(draft);
      setMessage(
        err instanceof Error
          ? `Queued for sync: ${err.message}`
          : 'Queued for sync.',
      );
    }
  }

  function runAnalysis() {
    void saveDraft(true);
    router.push({
      pathname: '/deal-analyzer',
      params: { query: title, barcode, askingPrice },
    });
  }

  return (
    <Screen edges={['top', 'left', 'right']}>
      <View style={styles.header}>
        <Text variant="title">Scout</Text>
        <StatusPill
          label={connectivity.label}
          tone={connectivity.isOnline ? 'success' : 'warning'}
        />
      </View>

      {!connectivity.isOnline ? (
        <OfflineBanner message="Offline. Drafts save locally. Live pricing unavailable." />
      ) : null}

      <View style={styles.quickGrid}>
        {quickActions.map((action) => (
          <Pressable
            key={action.label}
            onPress={action.onPress}
            accessibilityRole="button"
            accessibilityLabel={action.label.replace('\n', ' ')}
            style={({ pressed }) => [
              styles.quickTile,
              { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm },
              pressed && { opacity: 0.8, backgroundColor: c.surfaceAlt },
            ]}
          >
            <View style={[styles.quickIconWrap, { backgroundColor: action.bg }]} accessibilityElementsHidden>
              <Ionicons name={action.icon} size={28} color={action.color} />
            </View>
            <Text variant="caption" style={styles.quickLabel}>
              {action.label}
            </Text>
          </Pressable>
        ))}
      </View>

      <SectionHeader title="Item Draft" />

      <Card>
        <TextField
          label="Item name or search terms"
          value={title}
          onChangeText={(v) => {
            setTitle(v);
            if (!showDetails && v.length > 0) setShowDetails(true);
          }}
          placeholder="Sony WH-1000XM4 headphones"
        />
        <TextField
          label="Barcode"
          value={barcode}
          onChangeText={setBarcode}
          placeholder="UPC, EAN, ISBN"
          keyboardType="number-pad"
        />
        <TextField
          label="Seller asking price"
          value={askingPrice}
          onChangeText={setAskingPrice}
          placeholder="0.00"
          prefix="$"
          keyboardType="decimal-pad"
        />

        {!showDetails ? (
          <Pressable onPress={() => setShowDetails(true)} accessibilityRole="button" accessibilityLabel="Show optional fields" style={styles.expandRow}>
            <Text variant="caption" color={c.primary}>
              Add condition, source, notes
            </Text>
            <Ionicons name="chevron-down" size={16} color={c.primary} />
          </Pressable>
        ) : (
          <>
            <TextField
              label="Condition"
              value={condition}
              onChangeText={setCondition}
              placeholder="Used - good"
            />
            <TextField
              label="Source / location"
              value={sourceName}
              onChangeText={setSourceName}
              placeholder="Goodwill, estate sale, yard sale"
            />
            <TextField
              label="Notes"
              value={notes}
              onChangeText={setNotes}
              placeholder="Accessories, damage, shelf location"
              multiline
            />
          </>
        )}
      </Card>

      {message ? (
        <Card variant="flat">
          <View style={styles.messageRow}>
            <Ionicons name="checkmark-circle" size={18} color={c.success} />
            <Text variant="caption" color={c.successDark}>
              {message}
            </Text>
          </View>
        </Card>
      ) : null}

      <View style={styles.primaryActions}>
        <Button
          label="Save Draft"
          icon="bookmark-outline"
          variant="secondary"
          size="lg"
          fullWidth
          onPress={() => void saveDraft(true)}
        />
        <Button
          label="Run Deal Check"
          icon="calculator-outline"
          size="lg"
          fullWidth
          onPress={runAnalysis}
        />
      </View>

      <Button
        label="Add to Inventory"
        icon="cube-outline"
        variant="secondary"
        fullWidth
        onPress={() => void addToInventory()}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.md,
  },
  quickGrid: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  quickTile: {
    flex: 1,
    alignItems: 'center',
    borderRadius: radius.lg,
    paddingVertical: spacing.lg,
    paddingHorizontal: spacing.sm,
    gap: spacing.sm,
    borderWidth: 1,
  },
  quickIconWrap: {
    width: 48,
    height: 48,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  quickLabel: {
    textAlign: 'center',
    fontWeight: '600',
    lineHeight: 16,
  },
  expandRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.xs,
  },
  messageRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  primaryActions: {
    gap: spacing.sm,
  },
});
