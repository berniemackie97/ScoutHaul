import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import * as ImagePicker from 'expo-image-picker';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import type { InventoryReferenceData, InventoryReferenceOption } from '../../src/api/inventory';
import { Button } from '../../src/components/Button';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { QueuedBanner } from '../../src/components/QueuedBanner';
import { ExpandableSection } from '../../src/components/ExpandableSection';
import { LoadingState } from '../../src/components/LoadingState';
import { Screen } from '../../src/components/Screen';
import { SelectField, type SelectOption } from '../../src/components/SelectField';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { useHaptics } from '../../src/hooks/useHaptics';
import { enqueueOperation } from '../../src/db/localStore';
import { isWorthQueueing } from '../../src/services/offlineWrites';
import { takeIdentifyHandoff } from '../../src/features/scanner/identifyHandoff';
import {
  categoryAttributeFields,
  authenticityStatusOptions,
  categoryOptions,
  colorOptions,
  commerceManagedStatuses,
  completenessStatusOptions,
  conditionOptions,
  createInventoryStatusOptions,
  damageSeverityOptions,
  damageTypeOptions,
  dimensionUnitOptions,
  functionalStatusOptions,
  inventoryStatusLabel,
  manualInventoryStatusOptions,
  ownershipOptions,
  packagingStatusOptions,
  sizeSystemOptions,
  weightUnitOptions,
} from '../../src/domain/inventoryOptions';
import { readinessBlockers } from '../../src/domain/lifecycleReadiness';
import { ReadinessChecklist } from '../../src/features/inventory/ReadinessChecklist';
import type {
  InventoryAuthenticityStatus,
  InventoryCompletenessStatus,
  DimensionUnit,
  InventoryFunctionalStatus,
  InventoryItem,
  InventoryOwnershipType,
  InventoryPackagingStatus,
  InventoryStatus,
  WeightUnit,
} from '../../src/domain/types';
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

function referenceOptions(
  remote: InventoryReferenceOption[] | undefined,
  fallback: SelectOption[],
  current?: string,
): SelectOption[] {
  const options = remote?.map((option) => ({
    value: option.value,
    label: option.label,
    group: option.group ?? undefined,
  })) ?? fallback;
  if (current && !options.some((option) => option.value === current)) {
    return [...options, { value: current, label: `${current} (existing value)`, group: 'Existing data' }];
  }
  return options;
}

export default function EditInventoryScreen() {
  const { colors: c } = useAppTheme();
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
  const [status, setStatus] = useState<InventoryStatus>('Draft');
  const [loadedStatus, setLoadedStatus] = useState<InventoryStatus | null>(null);
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
  const [ownershipType, setOwnershipType] = useState<InventoryOwnershipType>('Owned');
  const [functionalStatus, setFunctionalStatus] = useState<InventoryFunctionalStatus>('Unknown');
  const [completenessStatus, setCompletenessStatus] = useState<InventoryCompletenessStatus>('Unknown');
  const [authenticityStatus, setAuthenticityStatus] = useState<InventoryAuthenticityStatus>('NotAssessed');
  const [packagingStatus, setPackagingStatus] = useState<InventoryPackagingStatus>('NotApplicable');
  const [damageType, setDamageType] = useState('');
  const [damageSeverity, setDamageSeverity] = useState('');
  const [damageNote, setDamageNote] = useState('');
  const [purchasePrice, setPurchasePrice] = useState('');
  const [estimatedSalePrice, setEstimatedSalePrice] = useState(params.estimatedSalePrice ?? '');
  const [barcode, setBarcode] = useState(params.barcode ?? '');
  const [manufacturerPartNumber, setManufacturerPartNumber] = useState('');
  const [serialNumber, setSerialNumber] = useState('');
  const [countryOfOrigin, setCountryOfOrigin] = useState('');
  const [manufactureYear, setManufactureYear] = useState('');
  const [length, setLength] = useState('');
  const [width, setWidth] = useState('');
  const [height, setHeight] = useState('');
  const [dimensionUnit, setDimensionUnit] = useState<DimensionUnit>('Inches');
  const [weight, setWeight] = useState('');
  const [weightUnit, setWeightUnit] = useState<WeightUnit>('Ounces');
  const [notes, setNotes] = useState('');
  const [description, setDescription] = useState('');
  const [photos, setPhotos] = useState<LocalPhoto[]>([]);
  const [originalRemotePhotoRefs, setOriginalRemotePhotoRefs] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [queuedNotice, setQueuedNotice] = useState<string | null>(null);
  const [titleTouched, setTitleTouched] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [referenceData, setReferenceData] = useState<InventoryReferenceData | null>(null);
  // Guards loadItem against re-running on every focus (which would clobber
  // unsaved edits and any fields applied from the scanner handoff).
  const hasLoadedRef = useRef(false);

  // Dynamic category attribute fields
  const categoryFields = useMemo(
    () => categoryAttributeFields[category] ?? [],
    [category],
  );

  const controlledCategories = useMemo(() => referenceOptions(referenceData?.categories, categoryOptions, category), [referenceData, category]);
  const controlledConditions = useMemo(() => referenceOptions(referenceData?.conditions, conditionOptions, condition), [referenceData, condition]);
  const controlledColors = useMemo(() => referenceOptions(referenceData?.colors, colorOptions, color), [referenceData, color]);
  const controlledSizeSystems = useMemo(() => referenceOptions(referenceData?.sizeSystems, sizeSystemOptions, sizeSystem), [referenceData, sizeSystem]);
  const controlledBrands = useMemo(() => referenceOptions(referenceData?.brands, [], brand), [referenceData, brand]);
  const controlledModels = useMemo(
    () => referenceOptions(referenceData?.models.filter((option) => !option.parentValue || !brand || option.parentValue === brand), [], model),
    [referenceData, brand, model],
  );
  const controlledSizes = useMemo(
    () => referenceOptions(referenceData?.sizes.filter((option) => option.parentValue === sizeSystem), [], sizeValue),
    [referenceData, sizeSystem, sizeValue],
  );
  const controlledStorageLocations = useMemo(
    () => referenceOptions(referenceData?.storageLocations, [], storageLocation),
    [referenceData, storageLocation],
  );
  const controlledCountries = useMemo(
    () => referenceOptions(referenceData?.countries, [], countryOfOrigin),
    [referenceData, countryOfOrigin],
  );
  const statusOptions = isEdit ? manualInventoryStatusOptions : createInventoryStatusOptions;
  const isCommerceManagedStage = Boolean(loadedStatus && commerceManagedStatuses.includes(loadedStatus));

  useEffect(() => {
    let active = true;
    void api.inventoryReferenceData()
      .then((data) => { if (active) setReferenceData(data); })
      .catch(() => { /* Offline fallback remains intentionally available. */ });
    return () => { active = false; };
  }, []);

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
    const filled = Object.values(attributes).filter((v) => v.trim()).length;
    n += filled;
    return n;
  }, [sizeSystem, sizeValue, attributes]);

  const detailsTotal = useMemo(() => 2 + categoryFields.length, [categoryFields]);

  const physicalFilled = useMemo(() => {
    let n = 0;
    if (barcode.trim()) n++;
    if (manufacturerPartNumber.trim()) n++;
    if (serialNumber.trim()) n++;
    if (countryOfOrigin) n++;
    if (manufactureYear.trim()) n++;
    if (length.trim() || width.trim() || height.trim()) n++;
    if (weight.trim()) n++;
    return n;
  }, [barcode, manufacturerPartNumber, serialNumber, countryOfOrigin, manufactureYear, length, width, height, weight]);

  const workFilled = useMemo(() => {
    let n = 0;
    if (ownershipType) n++;
    if (storageLocation.trim()) n++;
    if (accessories.trim()) n++;
    if (testingNotes.trim()) n++;
    if (cleaningNotes.trim()) n++;
    if (listingNotes.trim()) n++;
    if (missingParts.trim()) n++;
    return n;
  }, [ownershipType, storageLocation, accessories, testingNotes, cleaningNotes, listingNotes, missingParts]);

  const readinessFacts = useMemo(() => ({
    title,
    description,
    category,
    condition,
    functionalStatus,
    completenessStatus,
    authenticityStatus,
    packagingStatus,
    testingNotes,
    estimatedSalePrice,
    photoCount: photos.length,
  }), [title, description, category, condition, functionalStatus, completenessStatus, authenticityStatus, packagingStatus, testingNotes, estimatedSalePrice, photos.length]);
  const readinessBlockingItems = useMemo(() => readinessBlockers(status, readinessFacts), [status, readinessFacts]);
  const blockedReadyToList = (status === 'ReadyToList' || status === 'Listed') && readinessBlockingItems.length > 0;

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
      // Explicit regenerate overwrites the AI-derived fields; the scan-to-add path only fills blanks
      // so it never clobbers what the user typed.
      const overwrite = h._overwrite === '1';
      const apply = (next: string | undefined) => (cur: string) =>
        overwrite ? (next ?? cur) : (cur.trim() ? cur : next ?? cur);
      setTitle(apply(h.title));
      setBrand(apply(h.brand));
      setModel(apply(h.model));
      setCategory(apply(h.category));
      setColor(apply(h.color));
      setCondition(apply(h.condition));
      setBarcode(apply(h.barcode));
      setEstimatedSalePrice(apply(h.estimatedSalePrice));
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
      setStatus(item.status);
      setLoadedStatus(item.status);
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
      setOwnershipType(item.ownershipType ?? 'Owned');
      setFunctionalStatus(item.functionalStatus ?? 'Unknown');
      setCompletenessStatus(item.completenessStatus ?? 'Unknown');
      setAuthenticityStatus(item.authenticityStatus ?? 'NotAssessed');
      setPackagingStatus(item.packagingStatus ?? 'NotApplicable');
      setPurchasePrice(item.purchasePrice != null ? String(item.purchasePrice) : '');
      setEstimatedSalePrice(item.estimatedSalePrice != null ? String(item.estimatedSalePrice) : '');
      setBarcode(item.barcode ?? '');
      setManufacturerPartNumber(item.manufacturerPartNumber ?? '');
      setSerialNumber(item.serialNumber ?? '');
      setCountryOfOrigin(item.countryOfOrigin ?? '');
      setManufactureYear(item.manufactureYear != null ? String(item.manufactureYear) : '');
      setLength(item.length != null ? String(item.length) : '');
      setWidth(item.width != null ? String(item.width) : '');
      setHeight(item.height != null ? String(item.height) : '');
      setDimensionUnit(item.dimensionUnit ?? 'Inches');
      setWeight(item.weight != null ? String(item.weight) : '');
      setWeightUnit(item.weightUnit ?? 'Ounces');
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
  // Any fields already filled in are forwarded as `context` so the AI can use them as ground truth
  // to sharpen identification + valuation (e.g. you typed the brand → it nails the variant).
  function buildKnownFactsContext(): string {
    const facts: string[] = [];
    if (brand.trim()) facts.push(`Brand: ${brand.trim()}`);
    if (title.trim()) facts.push(`Name/title: ${title.trim()}`);
    if (model.trim()) facts.push(`Model: ${model.trim()}`);
    if (manufacturerPartNumber.trim()) facts.push(`Manufacturer part number: ${manufacturerPartNumber.trim()}`);
    if (serialNumber.trim()) facts.push(`Serial number: ${serialNumber.trim()}`);
    if (barcode.trim()) facts.push(`Barcode / GTIN: ${barcode.trim()}`);
    if (category) facts.push(`Category: ${category}`);
    if (color.trim()) facts.push(`Color: ${color.trim()}`);
    if (condition) facts.push(`Condition: ${condition}`);
    if (countryOfOrigin) facts.push(`Country of origin: ${countryOfOrigin}`);
    if (manufactureYear.trim()) facts.push(`Manufacture year: ${manufactureYear.trim()}`);
    const size = [sizeSystem, sizeValue.trim()].filter(Boolean).join(' ');
    if (size) facts.push(`Size: ${size}`);
    if (notes.trim()) facts.push(`Notes: ${notes.trim()}`);
    return facts.join('\n');
  }

  function identifyItem() {
    haptics.medium();
    const context = buildKnownFactsContext();
    router.push({
      pathname: '/scanner',
      // regenerate=1: this is an explicit re-run on an existing record, so the AI result should
      // overwrite the current fields (using the typed facts as context), not just fill blanks.
      params: { mode: 'photo-identify', fill: '1', regenerate: '1', ...(context ? { context } : {}) },
    });
  }

  async function save() {
    setTitleTouched(true);
    if (!title.trim()) {
      setError('Item title is required.');
      return;
    }
    if (blockedReadyToList) {
      haptics.error();
      setError('Resolve readiness blockers before saving as Ready to list or Listed.');
      return;
    }
    const parsedManufactureYear = parseOptionalValidatedNumber(manufactureYear, 'Manufacture year', { integer: true, min: 1800, max: 2200 });
    const parsedLength = parseOptionalValidatedNumber(length, 'Length', { min: 0 });
    const parsedWidth = parseOptionalValidatedNumber(width, 'Width', { min: 0 });
    const parsedHeight = parseOptionalValidatedNumber(height, 'Height', { min: 0 });
    const parsedWeight = parseOptionalValidatedNumber(weight, 'Weight', { min: 0 });
    const validationError =
      parsedManufactureYear.error ??
      parsedLength.error ??
      parsedWidth.error ??
      parsedHeight.error ??
      parsedWeight.error;
    if (validationError) {
      haptics.error();
      setError(validationError);
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
      manufacturerPartNumber: manufacturerPartNumber.trim() || null,
      serialNumber: serialNumber.trim() || null,
      countryOfOrigin: countryOfOrigin || null,
      manufactureYear: parsedManufactureYear.value,
      length: parsedLength.value,
      width: parsedWidth.value,
      height: parsedHeight.value,
      dimensionUnit,
      weight: parsedWeight.value,
      weightUnit,
      notes: notes || null,
      description: description || null,
      productIdentityId: params.productIdentityId ?? null,
      sourcingTripId: params.sourcingTripId ?? null,
      sourcingTripStopId: params.sourcingTripStopId ?? null,
      sourceLocationId: null,
      ownershipType,
      functionalStatus,
      completenessStatus,
      authenticityStatus,
      packagingStatus,
      status: isEdit ? null : status,
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
      if (isEdit && loadedStatus && status !== loadedStatus && !isCommerceManagedStage) {
        item = await api.changeInventoryStatus(item.id, status);
        savedItem = item;
        setLoadedStatus(item.status);
      }
      haptics.success();
      if (params.sourcingTripId) {
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
      } else {
        router.replace(`/inventory/${item.id}`);
      }
    } catch (e) {
      haptics.error();
      // Only keep a write that could plausibly succeed later. Queuing a rejection — bad data, no
      // permission — builds a backlog that can never drain and hides the real problem from the user.
      const keepForLater = !isEdit && !savedItem && isWorthQueueing(e);

      if (keepForLater && params.sourcingTripId) {
        await enqueueOperation('CreateRouteStopPurchase', '/api/inventory', 'POST', body);
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
        return;
      }

      if (keepForLater) {
        await enqueueOperation('CreateInventory', '/api/inventory', 'POST', body);
        // Deliberately not setError: this succeeded. Reporting it as an error put a red banner with
        // a Retry button on a write that was already queued, and tapping it created the item twice.
        setQueuedNotice("Saved on this phone. It'll sync when you're back online.");
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
      {queuedNotice ? <QueuedBanner message={queuedNotice} /> : null}

      {/* ── Photo Management ── */}
      <PhotoManager
        photos={photos}
        onTake={() => void takePhoto()}
        onPick={() => void pickPhotos()}
        onIdentify={identifyItem}
        onRemove={removePhoto}
        onSetHero={setHero}
      />

      <ExpandableSection
        title={isEdit ? 'Lifecycle stage' : 'Starting stage'}
        icon="git-branch-outline"
        defaultExpanded
        badge={inventoryStatusLabel(status)}
      >
        {isCommerceManagedStage ? (
          <View style={[styles.lifecycleNotice, { backgroundColor: c.surfaceAlt, borderColor: c.borderLight }]}>
            <View style={styles.lifecycleNoticeText}>
              <Text variant="bodyBold">Controlled by Orders</Text>
              <Text variant="caption">
                This item is {inventoryStatusLabel(loadedStatus)}. Sale, fulfillment, and return stages must move through the order workspace.
              </Text>
            </View>
            <Button label="Open Orders" icon="receipt-outline" size="sm" variant="secondary" onPress={() => router.push('/(tabs)/orders')} />
          </View>
        ) : (
          <>
            <SelectField
              label={isEdit ? 'Current stage' : 'Starting stage'}
              value={status}
              options={statusOptions}
              onChange={(value) => setStatus(value as InventoryStatus)}
              placeholder="Select stage..."
              searchable
              clearable={false}
            />
            <ReadinessChecklist targetStatus={status} facts={readinessFacts} />
            {blockedReadyToList ? (
              <View style={[styles.blockerBox, { borderColor: c.warning, backgroundColor: c.warningLight }]}>
                <Ionicons name="alert-circle-outline" size={16} color={c.warning} />
                <Text variant="caption" color={c.warningDark}>
                  Resolve the blocker{readinessBlockingItems.length === 1 ? '' : 's'} before moving this item to Ready to list or Listed.
                </Text>
              </View>
            ) : null}
          </>
        )}
      </ExpandableSection>

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
          options={controlledCategories}
          onChange={(v) => {
            setCategory(v);
            if (v !== category) setAttributes({});
          }}
          placeholder="Select category..."
          searchable
        />
        <View style={styles.row}>
          <SelectField label="Brand" value={brand} options={controlledBrands} onChange={(value) => { setBrand(value); setModel(''); }} placeholder="Select approved brand..." searchable containerStyle={styles.flex} />
          <SelectField label="Model" value={model} options={controlledModels} onChange={setModel} placeholder="Select approved model..." searchable containerStyle={styles.flex} />
        </View>
        <SelectField label="Color" value={color} options={controlledColors} onChange={setColor} placeholder="Select color..." searchable />
        <SelectField
          label="Condition"
          value={condition}
          options={controlledConditions}
          onChange={setCondition}
          placeholder="Select condition..."
          required
        />
      </ExpandableSection>

      <ExpandableSection
        title="Condition controls"
        icon="shield-checkmark-outline"
        filledCount={[
          functionalStatus !== 'Unknown',
          completenessStatus !== 'Unknown',
          authenticityStatus !== 'NotAssessed',
          packagingStatus !== 'NotApplicable',
        ].filter(Boolean).length}
        totalCount={4}
      >
        <SelectField
          label="Functional / tested state"
          value={functionalStatus}
          options={functionalStatusOptions}
          onChange={(value) => setFunctionalStatus(value as InventoryFunctionalStatus)}
          clearable={false}
        />
        <SelectField
          label="Completeness"
          value={completenessStatus}
          options={completenessStatusOptions}
          onChange={(value) => setCompletenessStatus(value as InventoryCompletenessStatus)}
          clearable={false}
        />
        <SelectField
          label="Authenticity"
          value={authenticityStatus}
          options={authenticityStatusOptions}
          onChange={(value) => setAuthenticityStatus(value as InventoryAuthenticityStatus)}
          clearable={false}
        />
        <SelectField
          label="Packaging / CIB state"
          value={packagingStatus}
          options={packagingStatusOptions}
          onChange={(value) => setPackagingStatus(value as InventoryPackagingStatus)}
          clearable={false}
        />
      </ExpandableSection>

      {/* ── Identifiers & Physical Data — collapsed ── */}
      <ExpandableSection
        title="Identifiers & physical data"
        icon="barcode-outline"
        filledCount={physicalFilled}
        totalCount={7}
      >
        <TextField
          label="Barcode / GTIN"
          value={barcode}
          onChangeText={setBarcode}
          keyboardType="number-pad"
          placeholder="UPC, EAN, ISBN"
        />
        <View style={styles.row}>
          <TextField
            label="Manufacturer part #"
            value={manufacturerPartNumber}
            onChangeText={setManufacturerPartNumber}
            placeholder="MPN, style code, part number"
            containerStyle={styles.flex}
          />
          <TextField
            label="Serial number"
            value={serialNumber}
            onChangeText={setSerialNumber}
            placeholder="Serial, IMEI, service tag"
            containerStyle={styles.flex}
          />
        </View>
        <View style={styles.row}>
          <SelectField
            label="Country of origin"
            value={countryOfOrigin}
            options={controlledCountries}
            onChange={setCountryOfOrigin}
            placeholder="Search countries..."
            searchable
            containerStyle={styles.flex}
          />
          <TextField
            label="Manufacture year"
            value={manufactureYear}
            onChangeText={setManufactureYear}
            keyboardType="number-pad"
            placeholder="YYYY"
            containerStyle={styles.flex}
          />
        </View>
        <View style={styles.row}>
          <TextField
            label="Length"
            value={length}
            onChangeText={setLength}
            keyboardType="decimal-pad"
            placeholder="0"
            containerStyle={styles.flex}
          />
          <TextField
            label="Width"
            value={width}
            onChangeText={setWidth}
            keyboardType="decimal-pad"
            placeholder="0"
            containerStyle={styles.flex}
          />
          <TextField
            label="Height"
            value={height}
            onChangeText={setHeight}
            keyboardType="decimal-pad"
            placeholder="0"
            containerStyle={styles.flex}
          />
        </View>
        <SelectField
          label="Dimension unit"
          value={dimensionUnit}
          options={dimensionUnitOptions}
          onChange={(value) => setDimensionUnit(value as DimensionUnit)}
          clearable={false}
        />
        <View style={styles.row}>
          <TextField
            label="Weight"
            value={weight}
            onChangeText={setWeight}
            keyboardType="decimal-pad"
            placeholder="0"
            containerStyle={styles.flex}
          />
          <SelectField
            label="Weight unit"
            value={weightUnit}
            options={weightUnitOptions}
            onChange={(value) => setWeightUnit(value as WeightUnit)}
            clearable={false}
            containerStyle={styles.flex}
          />
        </View>
      </ExpandableSection>

      {/* ── Size & Category Details — collapsed ── */}
      <ExpandableSection
        title="Size & category details"
        icon="resize-outline"
        filledCount={detailsFilled}
        totalCount={detailsTotal}
      >
        <SelectField
          label="Size system"
          value={sizeSystem}
          options={controlledSizeSystems}
          onChange={(value) => { setSizeSystem(value); setSizeValue(''); }}
          placeholder="Select sizing..."
        />
        {sizeSystem ? (
          <SelectField
            label="Size value"
            value={sizeValue}
            options={controlledSizes}
            onChange={setSizeValue}
            placeholder="Select size..."
            searchable
          />
        ) : null}

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
        totalCount={7}
      >
        <SelectField
          label="Ownership"
          value={ownershipType}
          options={ownershipOptions}
          onChange={(value) => setOwnershipType(value as InventoryOwnershipType)}
          clearable={false}
        />
        <SelectField
          label="Storage / bin location"
          value={storageLocation}
          options={controlledStorageLocations}
          onChange={setStorageLocation}
          placeholder="Select approved storage location..."
          searchable
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

function parseOptionalValidatedNumber(
  value: string,
  label: string,
  rules: { min?: number; max?: number; integer?: boolean } = {},
): { value: number | null; error: string | null } {
  const trimmed = value.trim();
  if (!trimmed) return { value: null, error: null };

  const parsed = Number(trimmed);
  if (!Number.isFinite(parsed)) {
    return { value: null, error: `${label} must be a valid number.` };
  }

  if (rules.integer && !Number.isInteger(parsed)) {
    return { value: null, error: `${label} must be a whole number.` };
  }

  if (rules.min !== undefined && parsed < rules.min) {
    return { value: null, error: `${label} must be at least ${rules.min}.` };
  }

  if (rules.max !== undefined && parsed > rules.max) {
    return { value: null, error: `${label} must be ${rules.max} or less.` };
  }

  return { value: parsed, error: null };
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
  lifecycleNotice: {
    borderWidth: 1,
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.md,
  },
  lifecycleNoticeText: {
    gap: spacing.xs,
  },
  blockerBox: {
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1,
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.sm,
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
