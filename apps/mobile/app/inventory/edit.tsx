import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import * as ImagePicker from 'expo-image-picker';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useMemo, useRef, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import { Button } from '../../src/components/Button';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { ExpandableSection } from '../../src/components/ExpandableSection';
import { LoadingState } from '../../src/components/LoadingState';
import { Screen } from '../../src/components/Screen';
import { SelectField } from '../../src/components/SelectField';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { useHaptics } from '../../src/hooks/useHaptics';
import { enqueueOperation } from '../../src/db/localStore';
import { takeIdentifyHandoff } from '../../src/features/scanner/identifyHandoff';
import {
  categoryAttributeFields,
  categoryOptions,
  conditionOptions,
  damageSeverityOptions,
  damageTypeOptions,
  sizeSystemOptions,
} from '../../src/domain/inventoryOptions';
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

type AttributeMap = Record<string, string>;

export default function EditInventoryScreen() {
  const { colors: c, shadows: s } = useAppTheme();
  const haptics = useHaptics();
  const params = useLocalSearchParams<{
    id?: string;
    title?: string;
    barcode?: string;
    brand?: string;
    model?: string;
    category?: string;
    color?: string;
    photoUri?: string;
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
  const [category, setCategory] = useState(params.category ?? '');
  const [brand, setBrand] = useState(params.brand ?? '');
  const [model, setModel] = useState(params.model ?? '');
  const [color, setColor] = useState(params.color ?? '');
  const [sizeSystem, setSizeSystem] = useState('');
  const [sizeValue, setSizeValue] = useState('');
  const [attributes, setAttributes] = useState<AttributeMap>({});
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
  const [titleTouched, setTitleTouched] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  // Guards loadItem against re-running on every focus (which would clobber
  // unsaved edits and any fields applied from the scanner handoff).
  const hasLoadedRef = useRef(false);

  // Dynamic category attribute fields
  const categoryFields = useMemo(
    () => categoryAttributeFields[category] ?? [],
    [category],
  );

  // Section fill counts for collapsed badges
  const essentialsFilled = useMemo(() => {
    let n = 0;
    if (title.trim()) n++;
    if (condition) n++;
    if (category) n++;
    if (brand.trim()) n++;
    return n;
  }, [title, condition, category, brand]);

  const pricingFilled = useMemo(() => {
    let n = 0;
    if (purchasePrice.trim()) n++;
    if (estimatedSalePrice.trim()) n++;
    return n;
  }, [purchasePrice, estimatedSalePrice]);

  const detailsFilled = useMemo(() => {
    let n = 0;
    if (sizeSystem) n++;
    if (sizeValue.trim()) n++;
    if (barcode.trim()) n++;
    const filled = Object.values(attributes).filter((v) => v.trim()).length;
    n += filled;
    return n;
  }, [sizeSystem, sizeValue, barcode, attributes]);

  const detailsTotal = useMemo(() => 3 + categoryFields.length, [categoryFields]);

  const workFilled = useMemo(() => {
    let n = 0;
    if (storageLocation.trim()) n++;
    if (accessories.trim()) n++;
    if (testingNotes.trim()) n++;
    if (cleaningNotes.trim()) n++;
    if (listingNotes.trim()) n++;
    if (missingParts.trim()) n++;
    return n;
  }, [storageLocation, accessories, testingNotes, cleaningNotes, listingNotes, missingParts]);

  const damageFilled = useMemo(() => {
    let n = 0;
    if (damageType) n++;
    if (damageSeverity) n++;
    if (damageNote.trim()) n++;
    return n;
  }, [damageType, damageSeverity, damageNote]);

  // Margin summary computed in real-time
  const projectedMargin = useMemo(() => {
    const cost = parseOptionalNumber(purchasePrice);
    const sale = parseOptionalNumber(estimatedSalePrice);
    if (cost === null || sale === null || sale === 0) return null;
    const profit = sale - cost;
    const pct = ((profit / sale) * 100).toFixed(0);
    return { profit, pct, positive: profit > 0 };
  }, [purchasePrice, estimatedSalePrice]);

  // Seed photo gallery from scanner's captured image
  useFocusEffect(
    useCallback(() => {
      if (params.photoUri && photos.length === 0) {
        setPhotos([{ uri: params.photoUri, isHero: true }]);
      }
    }, [params.photoUri]),
  );

  useFocusEffect(
    useCallback(() => {
      if (!isEdit || !params.id || hasLoadedRef.current) return;
      hasLoadedRef.current = true;
      void loadItem(params.id);
    }, [params.id]),
  );

  // Apply fields handed back by the scanner's photo-identify flow (fill mode).
  // Functional updaters read the *current* value so we never clobber what the
  // user (or loadItem) already filled in.
  useFocusEffect(
    useCallback(() => {
      const h = takeIdentifyHandoff();
      if (!h) return;
      setTitle((cur) => (cur.trim() ? cur : h.title ?? cur));
      setBrand((cur) => (cur.trim() ? cur : h.brand ?? cur));
      setModel((cur) => (cur.trim() ? cur : h.model ?? cur));
      setCategory((cur) => (cur ? cur : h.category ?? cur));
      setColor((cur) => (cur.trim() ? cur : h.color ?? cur));
      setCondition((cur) => (cur ? cur : h.condition ?? cur));
      setBarcode((cur) => (cur.trim() ? cur : h.barcode ?? cur));
      setEstimatedSalePrice((cur) => (cur.trim() ? cur : h.estimatedSalePrice ?? cur));
      if (h.photoUri) {
        const uri = h.photoUri;
        setPhotos((prev) => (prev.some((p) => p.uri === uri) ? prev : [...prev, { uri, isHero: prev.length === 0 }]));
      }
      haptics.success();
    }, []),
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
      setAttributes(parseAttributes(item.attributesJson));
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
      if (!combined.some((p) => p.isHero) && combined.length > 0) {
        combined[0].isHero = true;
      }
      return combined;
    });
    haptics.light();
  }

  async function takePhoto() {
    const perm = await ImagePicker.requestCameraPermissionsAsync();
    if (!perm.granted) return;
    const result = await ImagePicker.launchCameraAsync({ quality: 0.8 });
    if (result.canceled) return;
    const newPhoto: LocalPhoto = { uri: result.assets[0].uri, isHero: photos.length === 0 };
    setPhotos((prev) => [...prev, newPhoto]);
    haptics.light();
  }

  function removePhoto(index: number) {
    haptics.warning();
    setPhotos((prev) => {
      const next = prev.filter((_, i) => i !== index);
      if (next.length > 0 && !next.some((p) => p.isHero)) {
        next[0].isHero = true;
      }
      return next;
    });
  }

  function setHero(index: number) {
    haptics.selection();
    setPhotos((prev) => prev.map((p, i) => ({ ...p, isHero: i === index })));
  }

  // Photo-identify reuses the scanner's full flow (capture → analyze overlay →
  // grounded comps). The scanner hands the resolved fields back here via the
  // module handoff, which the focus effect above applies on return.
  function identifyItem() {
    haptics.medium();
    router.push({ pathname: '/scanner', params: { mode: 'photo-identify', fill: '1' } });
  }

  async function save() {
    setTitleTouched(true);
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
      attributesJson: serializeAttributes(attributes),
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
      haptics.success();
      if (params.sourcingTripId) {
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
      } else {
        router.replace(`/inventory/${item.id}`);
      }
    } catch (e) {
      haptics.error();
      if (!isEdit && params.sourcingTripId && !savedItem) {
        await enqueueOperation('CreateRouteStopPurchase', '/api/inventory', 'POST', body);
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
        return;
      }

      if (!isEdit && !savedItem) {
        await enqueueOperation('CreateInventory', '/api/inventory', 'POST', body);
        setError('Saved locally. Will sync when online.');
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

  const titleError = titleTouched && !title.trim() ? 'Title is required' : null;

  return (
    <Screen>
      {error ? <ErrorBanner message={error} onRetry={() => void save()} /> : null}

      {/* ── Photo Management ── */}
      <PhotoManager
        photos={photos}
        onTake={() => void takePhoto()}
        onPick={() => void pickPhotos()}
        onIdentify={identifyItem}
        onRemove={removePhoto}
        onSetHero={setHero}
      />

      {/* ── Essentials — always expanded ── */}
      <ExpandableSection
        title="Item Info"
        icon="cube-outline"
        defaultExpanded
        filledCount={essentialsFilled}
        totalCount={4}
      >
        <TextField
          label="Title"
          value={title}
          onChangeText={setTitle}
          onBlur={() => setTitleTouched(true)}
          placeholder="What is this item?"
          error={titleError}
        />
        <TextField
          label="Description"
          value={description}
          onChangeText={setDescription}
          placeholder="Details, color, size, model year..."
          multiline
        />
        <SelectField
          label="Category"
          value={category}
          options={categoryOptions}
          onChange={(v) => {
            setCategory(v);
            if (v !== category) setAttributes({});
          }}
          placeholder="Select category..."
          searchable
        />
        <View style={styles.row}>
          <TextField label="Brand" value={brand} onChangeText={setBrand} placeholder="Nike, Sony, Lego" containerStyle={styles.flex} />
          <TextField label="Model" value={model} onChangeText={setModel} placeholder="Style, model, SKU" containerStyle={styles.flex} />
        </View>
        <TextField label="Color" value={color} onChangeText={setColor} placeholder="Black, red, multicolor" />
        <SelectField
          label="Condition"
          value={condition}
          options={conditionOptions}
          onChange={setCondition}
          placeholder="Select condition..."
          required
        />
      </ExpandableSection>

      {/* ── Size, Identifiers & Category Details — collapsed ── */}
      <ExpandableSection
        title="Details & Identifiers"
        icon="resize-outline"
        filledCount={detailsFilled}
        totalCount={detailsTotal}
      >
        <SelectField
          label="Size system"
          value={sizeSystem}
          options={sizeSystemOptions}
          onChange={setSizeSystem}
          placeholder="Select sizing..."
        />
        {sizeSystem ? (
          <TextField
            label="Size value"
            value={sizeValue}
            onChangeText={setSizeValue}
            placeholder={sizePlaceholder(sizeSystem)}
          />
        ) : null}
        <TextField
          label="Barcode"
          value={barcode}
          onChangeText={setBarcode}
          keyboardType="number-pad"
          placeholder="UPC, EAN, ISBN"
        />

        {/* Dynamic category attribute fields */}
        {categoryFields.length > 0 ? (
          <View style={styles.dynamicSection}>
            <View style={styles.dynamicHeader}>
              <View style={[styles.dynamicBadge, { backgroundColor: c.primaryLight }]}>
                <Ionicons name="layers-outline" size={14} color={c.primary} />
              </View>
              <Text variant="bodyBold" color={c.primary}>
                {category} Details
              </Text>
            </View>
            {categoryFields.map((field) =>
              field.type === 'select' && field.options ? (
                <SelectField
                  key={field.key}
                  label={field.label}
                  value={attributes[field.key] ?? ''}
                  options={field.options}
                  onChange={(v) => setAttributes((curr) => ({ ...curr, [field.key]: v }))}
                  placeholder={field.placeholder}
                />
              ) : (
                <TextField
                  key={field.key}
                  label={field.label}
                  value={attributes[field.key] ?? ''}
                  onChangeText={(v) => setAttributes((curr) => ({ ...curr, [field.key]: v }))}
                  placeholder={field.placeholder}
                  multiline={field.multiline}
                />
              ),
            )}
          </View>
        ) : category ? (
          <View style={[styles.noCategoryFields, { backgroundColor: c.surfaceAlt }]}>
            <Ionicons name="information-circle-outline" size={16} color={c.textMuted} />
            <Text variant="caption" color={c.textMuted}>
              No additional fields for {category}.
            </Text>
          </View>
        ) : null}
      </ExpandableSection>

      {/* ── Pricing — collapsed, shows margin ── */}
      <ExpandableSection
        title="Pricing"
        icon="cash-outline"
        filledCount={pricingFilled}
        totalCount={2}
        badge={projectedMargin ? `${projectedMargin.pct}%` : undefined}
      >
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
        {projectedMargin ? (
          <View style={[styles.marginBar, { backgroundColor: projectedMargin.positive ? c.successLight : c.dangerLight }]}>
            <Ionicons
              name={projectedMargin.positive ? 'trending-up' : 'trending-down'}
              size={18}
              color={projectedMargin.positive ? c.success : c.danger}
            />
            <Text variant="bodyBold" color={projectedMargin.positive ? c.successDark : c.dangerDark}>
              ${Math.abs(projectedMargin.profit).toFixed(2)} {projectedMargin.positive ? 'profit' : 'loss'}
            </Text>
            <StatusPill
              label={`${projectedMargin.pct}% margin`}
              tone={projectedMargin.positive ? 'success' : 'danger'}
              size="sm"
            />
          </View>
        ) : null}
      </ExpandableSection>

      {/* ── Item Work — collapsed ── */}
      <ExpandableSection
        title="Item Work"
        icon="construct-outline"
        filledCount={workFilled}
        totalCount={6}
      >
        <TextField
          label="Storage / bin location"
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
      </ExpandableSection>

      {/* ── Damage & Issues — collapsed ── */}
      <ExpandableSection
        title="Damage / Issues"
        icon="warning-outline"
        filledCount={damageFilled}
        totalCount={3}
      >
        <SelectField
          label="Issue type"
          value={damageType}
          options={damageTypeOptions}
          onChange={setDamageType}
          placeholder="Select issue type..."
        />
        <SelectField
          label="Severity"
          value={damageSeverity}
          options={damageSeverityOptions}
          onChange={setDamageSeverity}
          placeholder="Select severity..."
        />
        <TextField
          label="Damage note"
          value={damageNote}
          onChangeText={setDamageNote}
          multiline
          placeholder="Where it is, how visible, whether it affects function"
        />
      </ExpandableSection>

      {/* ── Notes — collapsed ── */}
      <ExpandableSection title="Notes" icon="document-text-outline">
        <TextField
          label="Notes"
          value={notes}
          onChangeText={setNotes}
          multiline
          placeholder="Anything else worth remembering"
        />
      </ExpandableSection>

      {/* ── Save Button ── */}
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

// ── Photo Manager ──

function PhotoManager({
  photos,
  onTake,
  onPick,
  onIdentify,
  onRemove,
  onSetHero,
}: {
  photos: LocalPhoto[];
  onTake: () => void;
  onPick: () => void;
  onIdentify: () => void;
  onRemove: (index: number) => void;
  onSetHero: (index: number) => void;
}) {
  const { colors: c, shadows: s } = useAppTheme();
  const hasPhotos = photos.length > 0;

  return (
    <View style={[styles.photoSection, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm }]}>
      <View style={styles.photoHeader}>
        <View style={styles.photoHeaderLeft}>
          <Ionicons name="camera-outline" size={18} color={c.primary} />
          <Text variant="bodyBold">Photos</Text>
        </View>
        {hasPhotos ? (
          <View style={[styles.photoCountBadge, { backgroundColor: c.primaryLight }]}>
            <Text variant="label" color={c.primary}>{photos.length}</Text>
          </View>
        ) : (
          <Text variant="caption" color={c.textMuted}>Tap to add</Text>
        )}
      </View>

      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.photoScroll}
      >
        <Pressable
          onPress={onIdentify}
          style={[styles.addPhotoBtn, styles.addPhotoPrimary, { backgroundColor: c.accentLight, borderColor: c.accent }]}
          accessibilityLabel="Photo identify item"
        >
          <Ionicons name="sparkles-outline" size={24} color={c.accent} />
          <Text variant="label" color={c.accent}>Identify</Text>
        </Pressable>
        <Pressable
          onPress={onTake}
          style={[styles.addPhotoBtn, styles.addPhotoPrimary, { backgroundColor: c.primaryLight, borderColor: c.primary }]}
          accessibilityLabel="Take photo"
        >
          <Ionicons name="camera" size={24} color={c.primary} />
          <Text variant="label" color={c.primary}>Camera</Text>
        </Pressable>
        <Pressable
          onPress={onPick}
          style={[styles.addPhotoBtn, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}
          accessibilityLabel="Pick from library"
        >
          <Ionicons name="images-outline" size={22} color={c.textSecondary} />
          <Text variant="label" color={c.textSecondary}>Library</Text>
        </Pressable>
        {photos.map((photo, i) => (
          <View key={photo.remoteId ?? photo.uri} style={styles.photoWrapper}>
            <Pressable onPress={() => onSetHero(i)} style={styles.photoTouch}>
              <Image
                source={{ uri: resolvePhotoUrl(photo.thumbnailUri ?? photo.uri) ?? photo.thumbnailUri ?? photo.uri }}
                style={styles.photoThumb}
                contentFit="cover"
                cachePolicy="disk"
              />
              {photo.isHero ? (
                <View style={[styles.heroBadge, { backgroundColor: c.primary }]}>
                  <Ionicons name="star" size={10} color={c.textInverse} />
                </View>
              ) : (
                <View style={[styles.orderBadge, { backgroundColor: 'rgba(0,0,0,0.5)' }]}>
                  <Text variant="label" color={c.textInverse} style={styles.orderText}>{i + 1}</Text>
                </View>
              )}
            </Pressable>
            <Pressable
              onPress={() => onRemove(i)}
              style={[styles.removeBtn, { backgroundColor: c.danger }]}
              hitSlop={6}
            >
              <Ionicons name="close" size={12} color={c.textInverse} />
            </Pressable>
          </View>
        ))}
      </ScrollView>

      {!hasPhotos ? (
        <View style={[styles.photoHint, { backgroundColor: c.surfaceAlt }]}>
          <Ionicons name="information-circle-outline" size={14} color={c.textMuted} />
          <Text variant="caption" color={c.textMuted}>
            First photo becomes the hero image. Tap any photo to set it as hero.
          </Text>
        </View>
      ) : null}
    </View>
  );
}

// ── Helpers ──

function sizePlaceholder(system: string): string {
  switch (system) {
    case 'US letter': return 'XS, S, M, L, XL, 2XL';
    case 'US numeric': return '0, 2, 4, 6, 8, 10, 12';
    case 'Waist/Inseam': return '32×30, 34×32';
    case 'Shoe US M': return '8, 9.5, 10, 11, 12';
    case 'Shoe US W': return '6, 7.5, 8, 9, 10';
    case 'Youth': return '4T, 5, 6, 7, 8, 10-12';
    default: return 'Enter size';
  }
}

function parseAttributes(value?: string | null): AttributeMap {
  if (!value) return {};
  try {
    const parsed = JSON.parse(value) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {};
    return Object.fromEntries(
      Object.entries(parsed as Record<string, unknown>)
        .filter(([, entry]) => typeof entry === 'string')
        .map(([key, entry]) => [key, entry as string]),
    );
  } catch {
    return {};
  }
}

function serializeAttributes(value: AttributeMap): string | null {
  const cleaned = Object.fromEntries(
    Object.entries(value)
      .map(([key, entry]) => [key, entry.trim()])
      .filter(([, entry]) => entry.length > 0),
  );
  return Object.keys(cleaned).length > 0 ? JSON.stringify(cleaned) : null;
}

// ── Styles ──

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },

  // Photo section
  photoSection: {
    borderRadius: radius.lg,
    borderWidth: 1,
    padding: spacing.lg,
    gap: spacing.md,
  },
  photoHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  photoHeaderLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  photoCountBadge: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
    borderRadius: radius.full,
  },
  photoScroll: {
    gap: spacing.sm,
  },
  photoWrapper: {
    position: 'relative',
  },
  photoTouch: {
    borderRadius: radius.md,
    overflow: 'hidden',
  },
  photoThumb: {
    width: 76,
    height: 76,
    borderRadius: radius.md,
  },
  heroBadge: {
    position: 'absolute',
    bottom: 4,
    left: 4,
    width: 20,
    height: 20,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  orderBadge: {
    position: 'absolute',
    bottom: 4,
    left: 4,
    minWidth: 18,
    height: 18,
    borderRadius: 9,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 4,
  },
  orderText: {
    fontSize: 9,
    lineHeight: 12,
    letterSpacing: 0,
  },
  removeBtn: {
    position: 'absolute',
    top: -5,
    right: -5,
    width: 22,
    height: 22,
    borderRadius: 11,
    alignItems: 'center',
    justifyContent: 'center',
  },
  addPhotoBtn: {
    width: 76,
    height: 76,
    borderRadius: radius.md,
    borderWidth: 1.5,
    borderStyle: 'dashed',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xxs,
  },
  addPhotoPrimary: {
    borderStyle: 'solid',
  },
  photoHint: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.sm,
    borderRadius: radius.sm,
  },

  // Dynamic category fields
  dynamicSection: {
    gap: spacing.md,
  },
  dynamicHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingTop: spacing.xs,
  },
  dynamicBadge: {
    width: 24,
    height: 24,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  noCategoryFields: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.sm,
  },

  // Margin bar
  marginBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
  },
});
