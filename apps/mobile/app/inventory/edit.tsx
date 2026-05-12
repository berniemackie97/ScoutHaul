import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { TextField } from '../../src/components/TextField';
import { spacing } from '../../src/components/theme';
import type { InventoryItem } from '../../src/domain/types';
import { formatMoney, parseOptionalNumber } from '../../src/utils/money';

export default function EditInventoryScreen() {
  const params = useLocalSearchParams<{
    id?: string;
    title?: string;
    barcode?: string;
    productIdentityId?: string;
  }>();

  const isEdit = !!params.id;
  const [isLoading, setIsLoading] = useState(isEdit);
  const [title, setTitle] = useState(params.title ?? '');
  const [condition, setCondition] = useState('');
  const [purchasePrice, setPurchasePrice] = useState('');
  const [estimatedSalePrice, setEstimatedSalePrice] = useState('');
  const [barcode, setBarcode] = useState(params.barcode ?? '');
  const [notes, setNotes] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  useFocusEffect(
    useCallback(() => {
      if (!isEdit || !params.id) return;
      void loadItem(params.id);
    }, [params.id]),
  );

  async function loadItem(id: string) {
    try {
      setIsLoading(true);
      const item = await api.inventoryItem(id);
      setTitle(item.title);
      setCondition(item.condition ?? '');
      setPurchasePrice(item.purchasePrice != null ? String(item.purchasePrice) : '');
      setEstimatedSalePrice(item.estimatedSalePrice != null ? String(item.estimatedSalePrice) : '');
      setBarcode(item.barcode ?? '');
      setNotes(item.notes ?? '');
      setDescription(item.description ?? '');
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to load item.');
    } finally {
      setIsLoading(false);
    }
  }

  async function save() {
    if (!title.trim()) {
      setError('Item title is required.');
      return;
    }

    const body = {
      title,
      condition: condition || null,
      purchasePrice: parseOptionalNumber(purchasePrice),
      estimatedSalePrice: parseOptionalNumber(estimatedSalePrice),
      barcode: barcode || null,
      notes: notes || null,
      description: description || null,
      productIdentityId: params.productIdentityId ?? null,
      sourcingTripId: null,
      sourceLocationId: null,
      sellerAskingPrice: null,
    };

    try {
      setIsSaving(true);
      setError(null);
      let item: InventoryItem;
      if (isEdit && params.id) {
        item = await api.updateInventory(params.id, body);
      } else {
        item = await api.createInventory(body);
      }
      router.replace(`/inventory/${item.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to save item.');
    } finally {
      setIsSaving(false);
    }
  }

  if (isLoading) {
    return <Screen><LoadingState message="Loading item..." /></Screen>;
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
          label="Description"
          value={description}
          onChangeText={setDescription}
          placeholder="Details, color, size, model year"
          multiline
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
            label="Est. sale price"
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
        label={isSaving ? 'Saving...' : isEdit ? 'Update Item' : 'Add to Inventory'}
        icon={isEdit ? 'save-outline' : 'add-circle-outline'}
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
