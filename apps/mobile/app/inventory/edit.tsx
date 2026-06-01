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
import { categoryOptions, conditionOptions, damageSeverityOptions, damageTypeOptions, sizeSystemOptions } from '../../src/domain/inventoryOptions';
import type { InventoryItem } from '../../src/domain/types';
import { parseOptionalNumber } from '../../src/utils/money';
import { resolvePhotoUrl } from '../../src/utils/photoUrl';

type LocalPhoto = {
  uri: string;
  thumbnailUri?: string;
  remoteId?: string;
  isHero: boolean;
  isRemote?: boolean;
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
  const [category, setCategory] = useState('');
  const [brand, setBrand] = useState('');
  const [model, setModel] = useState('');
  const [color, setColor] = useState('');
  const [sizeSystem, setSizeSystem] = useState('');
  const [sizeValue, setSizeValue] = useState('');
  const [storageLocation, setStorageLocation] = useState('');
  const [accessories, setAccessories] = useState('');
  const [missingParts, setMissingParts] = useState('');
  const [testingNotes, setTestingNotes] = useState('');
  const [cleaningNotes, setCleaningNotes] = useState('');
  const [listingNotes, setListingNotes] = useState('');
  const [damageType, setDamageType] = useState('');
  const [damageSeverity, setDamageSeverity] = useState('');
  const [damageNote, setDamageNote] = useState('');
  const [purchasePrice, setPurchasePrice] = useState('');
  const [estimatedSalePrice, setEstimatedSalePrice] = useState(params.estimatedSalePrice ?? '');
  const [barcode, setBarcode] = useState(params.barcode ?? '');
  const [notes, setNotes] = useState('');
  const [description, setDescription] = useState('');
  const [photos, setPhotos] = useState<LocalPhoto[]>([]);
  const [originalRemotePhotoRefs, setOriginalRemotePhotoRefs] = useState<string[]>([]);
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
      setCategory(item.category ?? '');
      setBrand(item.brand ?? '');
      setModel(item.model ?? '');
      setColor(item.color ?? '');
      setSizeSystem(item.sizeSystem ?? '');
      setSizeValue(item.sizeValue ?? '');
      setStorageLocation(item.storageLocation ?? '');
      setAccessories(item.accessories ?? '');
      setMissingParts(item.missingParts ?? '');
      setTestingNotes(item.testingNotes ?? '');
      setCleaningNotes(item.cleaningNotes ?? '');
      setListingNotes(item.listingNotes ?? '');
      setPurchasePrice(item.purchasePrice != null ? String(item.purchasePrice) : '');
      setEstimatedSalePrice(item.estimatedSalePrice != null ? String(item.estimatedSalePrice) : '');
      setBarcode(item.barcode ?? '');
      setNotes(item.notes ?? '');
      setDescription(item.description ?? '');
      const typedPhotos = item.photos ?? [];
      const remotePhotos = typedPhotos.length > 0
        ? typedPhotos.map((photo) => ({
            uri: photo.originalUrl,
            thumbnailUri: photo.thumbnailUrl,
            remoteId: photo.id,
            isHero: photo.isHero,
            isRemote: true,
          }))
        : (item.photoUrls ?? []).map((uri, index) => ({ uri, isHero: index === 0, isRemote: true }));
      setOriginalRemotePhotoRefs(remotePhotos.map(remotePhotoKey));
      setPhotos(remotePhotos);
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
      category: category || null,
      brand: brand || null,
      model: model || null,
      color: color || null,
      condition: condition || null,
      sizeSystem: sizeSystem || null,
      sizeValue: sizeValue || null,
      measurementsJson: null,
      storageLocation: storageLocation || null,
      accessories: accessories || null,
      missingParts: missingParts || null,
      testingNotes: testingNotes || null,
      cleaningNotes: cleaningNotes || null,
      listingNotes: listingNotes || null,
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

    let savedItem: InventoryItem | null = null;
    try {
      setIsSaving(true);
      setError(null);
      let item: InventoryItem;
      if (isEdit && params.id) {
        item = await api.updateInventory(params.id, body);
      } else {
        item = await api.createInventory(body);
      }
      savedItem = item;
      await persistPhotoChanges(item.id);
      await persistIssueChanges(item.id);
      if (params.sourcingTripId) {
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
      } else {
        router.replace(`/inventory/${item.id}`);
      }
    } catch (e) {
      if (!isEdit && params.sourcingTripId && !savedItem) {
        await enqueueOperation('CreateRouteStopPurchase', '/api/inventory', 'POST', body);
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
        return;
      }

      if (!isEdit && !savedItem) {
        await enqueueOperation('CreateInventory', '/api/inventory', 'POST', body);
        setError('Inventory create queued locally. Sync before installing a new APK so it reaches the server.');
        return;
      }

      setError(e instanceof Error ? e.message : 'Unable to save item.');
    } finally {
      setIsSaving(false);
    }
  }

  async function persistPhotoChanges(itemId: string) {
    const currentRemotePhotoRefs = photos.filter((photo) => photo.isRemote).map(remotePhotoKey);
    const removedRemotePhotoRefs = originalRemotePhotoRefs.filter((ref) => !currentRemotePhotoRefs.includes(ref));

    for (const ref of removedRemotePhotoRefs) {
      if (ref.startsWith('id:')) {
        await api.deleteInventoryPhotoById(itemId, ref.slice(3));
      } else {
        await api.deleteInventoryPhoto(itemId, ref.slice(4));
      }
    }

    const remoteHero = photos.find((photo) => photo.isHero && photo.isRemote);
    if (remoteHero && originalRemotePhotoRefs[0] !== remotePhotoKey(remoteHero)) {
      await api.setInventoryHeroPhoto(itemId, remoteHero.uri);
    }

    for (const photo of photos.filter((candidate) => !candidate.isRemote)) {
      await api.uploadInventoryPhoto(itemId, photo.uri, photo.isHero);
    }
  }

  function remotePhotoKey(photo: LocalPhoto): string {
    return photo.remoteId ? `id:${photo.remoteId}` : `url:${photo.uri}`;
  }

  async function persistIssueChanges(itemId: string) {
    if (!damageType && !damageSeverity && !damageNote.trim()) return;
    await api.createInventoryIssue(itemId, {
      issueType: damageType || 'Other',
      severity: damageSeverity || 'Minor',
      note: damageNote.trim() || null,
      inventoryPhotoId: photos.find((photo) => photo.isHero && photo.remoteId)?.remoteId ?? null,
      sortOrder: null,
      isResolved: false,
    });
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
            <View key={photo.remoteId ?? photo.uri} style={styles.photoWrapper}>
              <Pressable onPress={() => setHero(i)} style={styles.photoTouch}>
                <Image source={{ uri: resolvePhotoUrl(photo.thumbnailUri ?? photo.uri) ?? photo.thumbnailUri ?? photo.uri }} style={styles.photoThumb} contentFit="cover" cachePolicy="disk" />
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
        <ChipGroup label="Category" value={category} options={categoryOptions} onSelect={setCategory} />
        <View style={styles.row}>
          <TextField label="Brand" value={brand} onChangeText={setBrand} placeholder="Nike, Sony, Lego" containerStyle={styles.flex} />
          <TextField label="Model" value={model} onChangeText={setModel} placeholder="Style, model, SKU" containerStyle={styles.flex} />
        </View>
        <TextField label="Color" value={color} onChangeText={setColor} placeholder="Black, red, multicolor" />
        <ChipGroup label="Condition" value={condition} options={conditionOptions} onSelect={setCondition} />
        <ChipGroup label="Size system" value={sizeSystem} options={sizeSystemOptions} onSelect={setSizeSystem} />
        <TextField
          label="Size"
          value={sizeValue}
          onChangeText={setSizeValue}
          placeholder="M, 32x30, 10.5, One size"
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
        <SectionHeader title="Item Work" />
        <TextField
          label="Storage/bin"
          value={storageLocation}
          onChangeText={setStorageLocation}
          placeholder="Bin A4, shelf 2, photo station"
        />
        <TextField
          label="Accessories included"
          value={accessories}
          onChangeText={setAccessories}
          multiline
          placeholder="Charger, box, manual, extra parts"
        />
        <TextField
          label="Missing parts"
          value={missingParts}
          onChangeText={setMissingParts}
          multiline
          placeholder="No remote, missing laces, no box"
        />
        <TextField
          label="Testing notes"
          value={testingNotes}
          onChangeText={setTestingNotes}
          multiline
          placeholder="Powers on, buttons tested, untested"
        />
        <TextField
          label="Cleaning notes"
          value={cleaningNotes}
          onChangeText={setCleaningNotes}
          multiline
          placeholder="Needs wash, wipe down, odor treatment"
        />
        <TextField
          label="Listing notes"
          value={listingNotes}
          onChangeText={setListingNotes}
          multiline
          placeholder="Keywords, bundle notes, listing reminders"
        />
      </Card>

      <Card>
        <SectionHeader title="Damage / Issues" />
        <ChipGroup label="Issue type" value={damageType} options={damageTypeOptions} onSelect={setDamageType} />
        <ChipGroup label="Severity" value={damageSeverity} options={damageSeverityOptions} onSelect={setDamageSeverity} />
        <TextField
          label="Damage note"
          value={damageNote}
          onChangeText={setDamageNote}
          multiline
          placeholder="Where it is, how visible it is, whether it affects function"
        />
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

function ChipGroup({
  label,
  value,
  options,
  onSelect,
}: {
  label: string;
  value: string;
  options: readonly string[];
  onSelect: (value: string) => void;
}) {
  const { colors: c } = useAppTheme();
  return (
    <View style={styles.chipGroup}>
      <Text variant="caption" color={c.textSecondary}>{label}</Text>
      <View style={styles.chipWrap}>
        {options.map((option) => {
          const selected = option === value;
          return (
            <Pressable
              key={option}
              accessibilityRole="button"
              accessibilityState={{ selected }}
              onPress={() => onSelect(selected ? '' : option)}
              style={[
                styles.chip,
                {
                  backgroundColor: selected ? c.primary : c.surfaceAlt,
                  borderColor: selected ? c.primary : c.border,
                },
              ]}
            >
              <Text variant="caption" color={selected ? c.textInverse : c.textSecondary}>
                {option}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
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
  chipGroup: {
    gap: spacing.xs,
  },
  chipWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  chip: {
    minHeight: 36,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.full,
    borderWidth: 1,
    justifyContent: 'center',
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
