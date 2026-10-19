import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { SelectField, type SelectOption } from '../../components/SelectField';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { parseOptionalNumber } from '../../utils/money';
import { parseSoldDay } from './labels';

export type FoundComp = {
  kind: 'Sold' | 'Asking';
  source: string;
  price: number;
  shipping: number | null;
  url: string | null;
  soldAt: string | null;
};

const sources: SelectOption[] = ['eBay', 'Poshmark', 'Mercari', 'Depop', 'Facebook Marketplace', 'Vinted', 'Other'].map((name) => ({
  value: name,
  label: name,
}));

type AddCompCardProps = {
  /** Saves the comp and prices the item again; resolves to what to tell the person. */
  onSave: (comp: FoundComp) => Promise<string>;
};

/** Record a listing found by hand, so the next price counts it as a checked comp. */
export function AddCompCard({ onSave }: AddCompCardProps) {
  const { colors: c } = useAppTheme();
  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState<FoundComp['kind']>('Sold');
  const [source, setSource] = useState('eBay');
  const [price, setPrice] = useState('');
  const [shipping, setShipping] = useState('');
  const [url, setUrl] = useState('');
  const [soldOn, setSoldOn] = useState('');
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const parsedPrice = parseOptionalNumber(price);
  const parsedShipping = parseOptionalNumber(shipping);
  const soldAt = parseSoldDay(soldOn);
  const invalidDate = soldOn.trim().length > 0 && soldAt === null;
  const valid = source.length > 0 && parsedPrice !== null && parsedPrice > 0 && (parsedShipping === null || parsedShipping >= 0) && !invalidDate;

  if (!open) {
    return (
      <Pressable onPress={() => setOpen(true)} accessibilityRole="button" style={[styles.closed, { borderColor: c.border }]}>
        <Text variant="bodyBold" color={c.primary}>Found a comp yourself? Add it</Text>
        <Text variant="caption">A sold listing you checked counts more than one found by search.</Text>
        {notice ? <Text variant="caption" color={c.success}>{notice}</Text> : null}
      </Pressable>
    );
  }

  async function save() {
    if (!valid || parsedPrice === null) return;
    setSaving(true);
    setError(null);
    try {
      const outcome = await onSave({
        kind,
        source,
        price: parsedPrice,
        shipping: parsedShipping,
        url: url.trim() || null,
        soldAt: kind === 'Sold' ? soldAt : null,
      });
      setNotice(outcome);
      setPrice('');
      setShipping('');
      setUrl('');
      setSoldOn('');
      setOpen(false);
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Could not save the comp.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card>
      <Text variant="bodyBold">Add a comp</Text>
      <View style={[styles.segment, { borderColor: c.border }]}>
        {(['Sold', 'Asking'] as const).map((option) => (
          <Pressable
            key={option}
            onPress={() => setKind(option)}
            accessibilityRole="radio"
            accessibilityState={{ selected: kind === option }}
            style={[styles.segmentOption, kind === option && { backgroundColor: c.primaryLight }]}
          >
            <Text variant="bodyBold" color={kind === option ? c.primary : c.textSecondary}>
              {option === 'Sold' ? 'Sold' : 'Listed now'}
            </Text>
          </Pressable>
        ))}
      </View>
      <SelectField label="Where" value={source} options={sources} onChange={setSource} clearable={false} />
      <View style={styles.row}>
        <TextField label="Price" prefix="$" value={price} onChangeText={setPrice} keyboardType="decimal-pad" containerStyle={styles.flex} />
        <TextField label="Shipping" prefix="$" value={shipping} onChangeText={setShipping} keyboardType="decimal-pad" containerStyle={styles.flex} />
      </View>
      {kind === 'Sold' ? (
        <TextField
          label="Sold on"
          value={soldOn}
          onChangeText={setSoldOn}
          placeholder="YYYY-MM-DD, blank for today"
          error={invalidDate ? 'Use YYYY-MM-DD, not in the future' : null}
          autoCapitalize="none"
        />
      ) : null}
      <TextField label="Link (optional)" value={url} onChangeText={setUrl} keyboardType="url" autoCapitalize="none" autoCorrect={false} />
      {error ? <Text variant="caption" color={c.danger}>{error}</Text> : null}
      <View style={styles.actions}>
        <Button label="Cancel" variant="ghost" size="sm" onPress={() => setOpen(false)} />
        <Button label={saving ? 'Saving…' : 'Save and reprice'} size="sm" disabled={!valid || saving} onPress={() => void save()} />
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  closed: {
    borderWidth: 1,
    borderStyle: 'dashed',
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.xs,
  },
  segment: {
    flexDirection: 'row',
    borderWidth: 1,
    borderRadius: radius.md,
    overflow: 'hidden',
  },
  segmentOption: {
    flex: 1,
    alignItems: 'center',
    paddingVertical: spacing.sm,
    minHeight: 40,
    justifyContent: 'center',
  },
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  actions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: spacing.sm,
  },
});
