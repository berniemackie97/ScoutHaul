import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { OfflineBanner } from '../src/components/OfflineBanner';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import { enqueueComp } from '../src/db/localStore';
import type { MarketObservationType } from '../src/domain/types';
import { useConnectivity } from '../src/hooks/useConnectivity';
import { parseOptionalNumber } from '../src/utils/money';

type CompTypeOption = {
  type: MarketObservationType;
  label: string;
  description: string;
};

const compTypes: CompTypeOption[] = [
  { type: 'ManualSold', label: 'Sold', description: 'Confirmed sold price' },
  { type: 'ManualActive', label: 'Active', description: 'Current listing price' },
  { type: 'Estimate', label: 'Estimate', description: 'Your price estimate' },
];

export default function CompCaptureScreen() {
  const params = useLocalSearchParams<{
    inventoryItemId?: string;
    productIdentityId?: string;
    title?: string;
  }>();
  const c = useColors();
  const connectivity = useConnectivity();
  const [type, setType] = useState<MarketObservationType>('ManualSold');
  const [source, setSource] = useState('eBay');
  const [title, setTitle] = useState(params.title ?? '');
  const [price, setPrice] = useState('');
  const [shipping, setShipping] = useState('');
  const [url, setUrl] = useState('');
  const [condition, setCondition] = useState('');
  const [notes, setNotes] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showOptional, setShowOptional] = useState(false);

  async function saveComp() {
    const hasAttachment = Boolean(params.productIdentityId || params.inventoryItemId);
    if (!hasAttachment) {
      setError('Save to inventory first so the comp has a product link.');
      return;
    }

    const parsedPrice = parseOptionalNumber(price);
    if (!title.trim() || !source.trim() || parsedPrice === null) {
      setError('Title, source, and price are required.');
      return;
    }

    const body = {
      productIdentityId: params.productIdentityId ?? null,
      inventoryItemId: params.inventoryItemId ?? null,
      observationType: type,
      source,
      sourceUrl: url || null,
      title,
      price: parsedPrice,
      shippingPrice: parseOptionalNumber(shipping),
      currency: 'USD',
      condition: condition || null,
      observedAt: new Date().toISOString(),
      soldAt: type === 'ManualSold' ? new Date().toISOString() : null,
      notes: notes || null,
    };

    if (!connectivity.isOnline) {
      await enqueueComp(body);
      setMessage('Queued for sync. Will not affect pricing until synced.');
      setError(null);
      return;
    }

    try {
      await api.addComp(body);
      setMessage('Comp saved.');
      setError(null);
    } catch (e) {
      await enqueueComp(body);
      setMessage(
        e instanceof Error
          ? `Queued after error: ${e.message}`
          : 'Queued after error.',
      );
    }
  }

  return (
    <Screen>
      {!connectivity.isOnline ? <OfflineBanner message="Comp queued offline. Not used for pricing until synced." /> : null}

      <SectionHeader title="Comp Type" />
      <View style={styles.typeRow}>
        {compTypes.map((opt) => {
          const isActive = opt.type === type;
          return (
            <Pressable
              key={opt.type}
              onPress={() => setType(opt.type)}
              accessibilityRole="button"
              accessibilityState={{ selected: isActive }}
              style={[
                styles.typeCard,
                { backgroundColor: isActive ? c.primaryLight : c.surface, borderColor: isActive ? c.primary : c.border },
              ]}
            >
              <Text variant="bodyBold" color={isActive ? c.primary : c.textSecondary}>
                {opt.label}
              </Text>
              <Text variant="caption" style={styles.typeDesc}>
                {opt.description}
              </Text>
            </Pressable>
          );
        })}
      </View>

      {type === 'ManualActive' ? (
        <Card variant="flat">
          <View style={styles.warningRow}>
            <Ionicons name="information-circle" size={16} color={c.warningDark} />
            <Text variant="caption" color={c.warningDark}>
              Active listing prices show demand context only — not sold data.
            </Text>
          </View>
        </Card>
      ) : null}

      <Card>
        <TextField label="Source" value={source} onChangeText={setSource} placeholder="eBay, Facebook, Mercari" />
        <TextField label="Title" value={title} onChangeText={setTitle} placeholder="Comparable listing title" />
        <TextField label="Price" value={price} onChangeText={setPrice} keyboardType="decimal-pad" prefix="$" placeholder="0.00" />

        {!showOptional ? (
          <Pressable onPress={() => setShowOptional(true)} accessibilityRole="button" accessibilityLabel="Show optional fields" style={styles.expandRow}>
            <Text variant="caption" color={c.primary}>Add shipping, URL, condition, notes</Text>
            <Ionicons name="chevron-down" size={16} color={c.primary} />
          </Pressable>
        ) : (
          <>
            <TextField label="Shipping" value={shipping} onChangeText={setShipping} keyboardType="decimal-pad" prefix="$" placeholder="0.00" />
            <TextField label="URL" value={url} onChangeText={setUrl} autoCapitalize="none" placeholder="https://..." />
            <TextField label="Condition" value={condition} onChangeText={setCondition} placeholder="Used - good" />
            <TextField label="Notes" value={notes} onChangeText={setNotes} multiline />
          </>
        )}
      </Card>

      {error ? <ErrorBanner message={error} /> : null}

      {message ? (
        <Card variant="flat">
          <View style={styles.successRow}>
            <Ionicons name="checkmark-circle" size={18} color={c.success} />
            <Text variant="caption" color={c.successDark}>{message}</Text>
          </View>
        </Card>
      ) : null}

      <Button
        label="Save Comp"
        icon="checkmark-outline"
        size="lg"
        fullWidth
        onPress={() => void saveComp()}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  typeRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  typeCard: {
    flex: 1,
    borderRadius: radius.md,
    padding: spacing.md,
    borderWidth: 1.5,
    alignItems: 'center',
    gap: spacing.xxs,
  },
  typeDesc: {
    textAlign: 'center',
    fontSize: 11,
  },
  expandRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.xs,
  },
  warningRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  successRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
});
