import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { spacing } from '../../src/components/theme';
import { parseOptionalNumber } from '../../src/utils/money';

export default function EditInventoryScreen() {
  const [title, setTitle] = useState('');
  const [condition, setCondition] = useState('');
  const [purchasePrice, setPurchasePrice] = useState('');
  const [estimatedSalePrice, setEstimatedSalePrice] = useState('');
  const [barcode, setBarcode] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  async function save() {
    if (!title.trim()) {
      setError('Item title is required.');
      return;
    }

    try {
      setIsSaving(true);
      setError(null);
      const item = await api.createInventory({
        title,
        condition: condition || null,
        purchasePrice: parseOptionalNumber(purchasePrice),
        estimatedSalePrice: parseOptionalNumber(estimatedSalePrice),
        barcode: barcode || null,
        notes: notes || null,
      });
      router.replace(`/inventory/${item.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to save item.');
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <Screen>
      {error ? <ErrorBanner message={error} /> : null}

      <Card>
        <SectionHeader title="Item Info" />
        <TextField
          label="Title"
          value={title}
          onChangeText={setTitle}
          placeholder="What is this item?"
        />
        <TextField
          label="Condition"
          value={condition}
          onChangeText={setCondition}
          placeholder="New, Used - Good, For Parts"
        />
        <TextField
          label="Barcode"
          value={barcode}
          onChangeText={setBarcode}
          keyboardType="number-pad"
          placeholder="UPC, EAN, ISBN"
        />
      </Card>

      <Card>
        <SectionHeader title="Pricing" />
        <View style={styles.row}>
          <TextField
            label="Purchase price"
            value={purchasePrice}
            onChangeText={setPurchasePrice}
            keyboardType="decimal-pad"
            prefix="$"
            placeholder="0.00"
            containerStyle={styles.flex}
          />
          <TextField
            label="Estimated sale price"
            value={estimatedSalePrice}
            onChangeText={setEstimatedSalePrice}
            keyboardType="decimal-pad"
            prefix="$"
            placeholder="0.00"
            containerStyle={styles.flex}
          />
        </View>
      </Card>

      <Card>
        <TextField
          label="Notes"
          value={notes}
          onChangeText={setNotes}
          multiline
          placeholder="Accessories, damage notes, storage location"
        />
      </Card>

      <Button
        label={isSaving ? 'Saving...' : 'Save Item'}
        icon="checkmark-outline"
        size="lg"
        fullWidth
        disabled={isSaving}
        onPress={() => void save()}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
});
