import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import * as ImagePicker from 'expo-image-picker';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { api } from '../../src/api/client';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { LoadingState } from '../../src/components/LoadingState';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { enqueueOperation } from '../../src/db/localStore';
import type { InventoryItem } from '../../src/domain/types';
import { parseOptionalNumber } from '../../src/utils/money';

type LocalPhoto = {
  uri: string;
  isHero: boolean;
};

export default function EditInventoryScreen() {
  const { colors: c } = useAppTheme();
  const params = useLocalSearchParams<{
    id?: string;
    title?: string;
    barcode?: string;
    productIdentityId?: string;
    estimatedSalePrice?: string;
    condition?: string;
    sourcingTripId?: string;
    sourcingTripStopId?: string;
  }>();

  const isEdit = !!params.id;
  const [isLoading, setIsLoading] = useState(isEdit);
  const [title, setTitle] = useState(params.title ?? '');
  const [condition, setCondition] = useState(params.condition ?? '');
  const [purchasePrice, setPurchasePrice] = useState('');
  const [estimatedSalePrice, setEstimatedSalePrice] = useState(params.estimatedSalePrice ?? '');
  const [barcode, setBarcode] = useState(params.barcode ?? '');
  const [notes, setNotes] = useState('');
  const [description, setDescription] = useState('');
  const [photos, setPhotos] = useState<LocalPhoto[]>([]);
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

  async function pickPhotos() {
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ['images'],
      allowsMultipleSelection: true,
      quality: 0.8,
      selectionLimit: 10,
    });
    if (result.canceled) return;
    const newPhotos: LocalPhoto[] = result.assets.map((asset) => ({
      uri: asset.uri,
      isHero: false,
    }));
    setPhotos((prev) => {
      const combined = [...prev, ...newPhotos];
      // First photo is hero if none set
      if (!combined.some((p) => p.isHero) && combined.length > 0) {
        combined[0].isHero = true;
      }
      return combined;
    });
  }

  async function takePhoto() {
    const perm = await ImagePicker.requestCameraPermissionsAsync();
    if (!perm.granted) return;
    const result = await ImagePicker.launchCameraAsync({
      quality: 0.8,
    });
    if (result.canceled) return;
    const newPhoto: LocalPhoto = { uri: result.assets[0].uri, isHero: photos.length === 0 };
    setPhotos((prev) => [...prev, newPhoto]);
  }

  function removePhoto(index: number) {
    setPhotos((prev) => {
      const next = prev.filter((_, i) => i !== index);
      // Ensure hero exists
      if (next.length > 0 && !next.some((p) => p.isHero)) {
        next[0].isHero = true;
      }
      return next;
    });
  }

  function setHero(index: number) {
    setPhotos((prev) =>
      prev.map((p, i) => ({ ...p, isHero: i === index })),
    );
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
      sourcingTripId: params.sourcingTripId ?? null,
      sourcingTripStopId: params.sourcingTripStopId ?? null,
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
      // TODO: Upload photos to server when photo API exists
      if (params.sourcingTripId) {
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
      } else {
        router.replace(`/inventory/${item.id}`);
      }
    } catch (e) {
      if (!isEdit && params.sourcingTripId) {
        await enqueueOperation('CreateRouteStopPurchase', '/api/inventory', 'POST', body);
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
        return;
      }

      setError(e instanceof Error ? e.message : 'Unable to save item.');
    } finally {
      setIsSaving(false);
    }
  }

  if (isLoading) {
    return (
      <Screen>
        <LoadingState message="Loading item..." />
      </Screen>
    );
  }

  return (
    <Screen>
      {error ? <ErrorBanner message={error} /> : null}

      {/* Photos section */}
      <Card>
        <SectionHeader title="Photos" />
        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={styles.photoScroll}
        >
          {photos.map((photo, i) => (
            <View key={photo.uri} style={styles.photoWrapper}>
              <Pressable onPress={() => setHero(i)} style={styles.photoTouch}>
                <Image source={{ uri: photo.uri }} style={styles.photoThumb} contentFit="cover" />
                {photo.isHero ? (
                  <View style={[styles.heroBadge, { backgroundColor: c.primary }]}>
                    <Ionicons name="star" size={10} color={c.textInverse} />
                  </View>
                ) : null}
              </Pressable>
              <Pressable
                onPress={() => removePhoto(i)}
                style={[styles.removeBtn, { backgroundColor: c.dangerLight }]}
                hitSlop={4}
              >
                <Ionicons name="close" size={14} color={c.danger} />
              </Pressable>
            </View>
          ))}
          <View style={styles.addPhotoButtons}>
            <Pressable
              onPress={() => void takePhoto()}
              style={[styles.addPhotoBtn, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}
              accessibilityLabel="Take photo"
            >
              <Ionicons name="camera-outline" size={24} color={c.primary} />
              <Text variant="label" color={c.primary}>Camera</Text>
            </Pressable>
            <Pressable
              onPress={() => void pickPhotos()}
              style={[styles.addPhotoBtn, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}
              accessibilityLabel="Pick from library"
            >
              <Ionicons name="images-outline" size={24} color={c.primary} />
              <Text variant="label" color={c.primary}>Library</Text>
            </Pressable>
          </View>
        </ScrollView>
      </Card>

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
            hint={params.estimatedSalePrice ? 'From marketplace avg' : undefined}
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
  photoScroll: {
    gap: spacing.sm,
    paddingBottom: spacing.xs,
  },
  photoWrapper: {
    position: 'relative',
  },
  photoTouch: {
    borderRadius: radius.md,
    overflow: 'hidden',
  },
  photoThumb: {
    width: 80,
    height: 80,
    borderRadius: radius.md,
  },
  heroBadge: {
    position: 'absolute',
    bottom: 4,
    left: 4,
    width: 18,
    height: 18,
    borderRadius: 9,
    alignItems: 'center',
    justifyContent: 'center',
  },
  removeBtn: {
    position: 'absolute',
    top: -4,
    right: -4,
    width: 20,
    height: 20,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  addPhotoButtons: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  addPhotoBtn: {
    width: 80,
    height: 80,
    borderRadius: radius.md,
    borderWidth: 1,
    borderStyle: 'dashed',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xs,
  },
});
