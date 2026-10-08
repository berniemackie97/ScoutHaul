import { Ionicons } from '@expo/vector-icons';
import * as ImagePicker from 'expo-image-picker';
import { router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Alert, StyleSheet, View } from 'react-native';
import { api } from '../../src/api';
import type { InventoryReferenceData, ItemField } from '../../src/api/inventory';
import { Button } from '../../src/components/Button';
import { ErrorBanner } from '../../src/components/ErrorBanner';
import { QueuedBanner } from '../../src/components/QueuedBanner';
import { ExpandableSection } from '../../src/components/ExpandableSection';
import { LoadingState } from '../../src/components/LoadingState';
import { Screen } from '../../src/components/Screen';
import { MultiSelectField, SelectField } from '../../src/components/SelectField';
import { StatusPill } from '../../src/components/StatusPill';
import { Text } from '../../src/components/Text';
import { TextField } from '../../src/components/TextField';
import { radius, spacing } from '../../src/components/theme';
import { useAppTheme } from '../../src/context/ThemeContext';
import { useHaptics } from '../../src/hooks/useHaptics';
import { enqueueOperation } from '../../src/db/localStore';
import { isWorthQueueing } from '../../src/services/offlineWrites';
import { savePhoto } from '../../src/utils/photos';
import { takeIdentifyHandoff } from '../../src/features/scanner/identifyHandoff';
import {
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
import { PhotoManager, type LocalPhoto } from '../../src/features/inventoryEdit/PhotoManager';
import { parseAttributes, serializeAttributes, type AttributeMap } from '../../src/features/inventoryEdit/attributes';
import { parseOptionalValidatedNumber } from '../../src/features/inventoryEdit/validation';
import { referenceOptions } from '../../src/features/inventoryEdit/referenceOptions';
import { fieldSpec, isListAttribute, profileFor } from '../../src/features/inventoryEdit/itemProfiles';
import {
  damageFilled as countDamageFilled,
  detailsFilled as countDetailsFilled,
  essentialsFilled as countEssentialsFilled,
  physicalFilled as countPhysicalFilled,
  pricingFilled as countPricingFilled,
  projectedMargin as computeProjectedMargin,
  workFilled as countWorkFilled,
} from '../../src/features/inventoryEdit/completeness';




/** The API keeps two colors, as Poshmark, Depop and Vinted take; beyond that an item is Multicolor or Patterned. */
const MAX_COLORS = 2;

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
    purchasePrice?: string;
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
  const [colors, setColors] = useState<string[]>(params.color ? [params.color] : []);
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
  const [purchasePrice, setPurchasePrice] = useState(params.purchasePrice ?? '');
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

  // The API's profile for this kind of item decides which fields exist. Until the profiles arrive (offline), every field
  // shows rather than hiding ones this item may have.
  const profile = useMemo(() => (referenceData ? profileFor(referenceData.profiles, category) : null), [referenceData, category]);
  const shows = (field: ItemField) => profile === null || fieldSpec(profile, field) !== undefined;
  const labelFor = (field: ItemField, fallback: string) => (profile ? fieldSpec(profile, field)?.label : undefined) ?? fallback;
  const profileAttributes = profile?.attributes ?? [];

  const controlledCategories = useMemo(() => referenceOptions(referenceData?.categories, categoryOptions, category), [referenceData, category]);
  const controlledConditions = useMemo(() => referenceOptions(referenceData?.conditions, conditionOptions, condition), [referenceData, condition]);
  const controlledColors = useMemo(() => referenceOptions(referenceData?.colors, colorOptions, colors), [referenceData, colors]);
  const controlledSizeSystems = useMemo(
    () =>
      referenceOptions(
        referenceData?.sizeSystems.filter((option) => !profile || profile.sizeSystems.includes(option.value)),
        sizeSystemOptions,
        sizeSystem,
      ),
    [referenceData, profile, sizeSystem],
  );
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
  const essentialsFilled = useMemo(
    () => countEssentialsFilled({ title, condition, category, brand }),
    [title, condition, category, brand],
  );

  const pricingFilled = useMemo(
    () => countPricingFilled({ purchasePrice, estimatedSalePrice }),
    [purchasePrice, estimatedSalePrice],
  );

  const detailsFilled = useMemo(
    () => countDetailsFilled({ sizeSystem, sizeValue, attributes }),
    [sizeSystem, sizeValue, attributes],
  );

  const detailsTotal = (shows('Size') ? 2 : 0) + profileAttributes.length;

  const physicalFilled = useMemo(
    () => countPhysicalFilled({
      barcode, manufacturerPartNumber, serialNumber, countryOfOrigin,
      manufactureYear, length, width, height, weight,
    }),
    [barcode, manufacturerPartNumber, serialNumber, countryOfOrigin, manufactureYear, length, width, height, weight],
  );

  const workFilled = useMemo(
    () => countWorkFilled({
      ownershipType, storageLocation, accessories, testingNotes,
      cleaningNotes, listingNotes, missingParts,
    }),
    [ownershipType, storageLocation, accessories, testingNotes, cleaningNotes, listingNotes, missingParts],
  );

  const readinessFacts = useMemo(() => ({
    profile,
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
  }), [title, description, category, condition, functionalStatus, completenessStatus, authenticityStatus, packagingStatus, testingNotes, estimatedSalePrice, photos.length, profile]);
  const readinessBlockingItems = useMemo(() => readinessBlockers(status, readinessFacts), [status, readinessFacts]);
  const blockedReadyToList = (status === 'ReadyToList' || status === 'Listed') && readinessBlockingItems.length > 0;

  const conditionControls = (
    [
      { field: 'FunctionalStatus', fallback: 'Working condition', value: functionalStatus, options: functionalStatusOptions, answered: functionalStatus !== 'Unknown', onChange: (value: string) => setFunctionalStatus(value as InventoryFunctionalStatus) },
      { field: 'CompletenessStatus', fallback: 'Completeness', value: completenessStatus, options: completenessStatusOptions, answered: completenessStatus !== 'Unknown', onChange: (value: string) => setCompletenessStatus(value as InventoryCompletenessStatus) },
      { field: 'AuthenticityStatus', fallback: 'Authenticity', value: authenticityStatus, options: authenticityStatusOptions, answered: authenticityStatus !== 'NotAssessed', onChange: (value: string) => setAuthenticityStatus(value as InventoryAuthenticityStatus) },
      { field: 'PackagingStatus', fallback: 'Packaging', value: packagingStatus, options: packagingStatusOptions, answered: packagingStatus !== 'NotApplicable', onChange: (value: string) => setPackagingStatus(value as InventoryPackagingStatus) },
    ] as const
  )
    .filter((control) => shows(control.field))
    .map((control) => ({ ...control, label: labelFor(control.field, control.fallback) }));

  const damageFilled = useMemo(
    () => countDamageFilled({ damageType, damageSeverity, damageNote }),
    [damageType, damageSeverity, damageNote],
  );

  // Margin summary computed in real-time
  const projectedMargin = useMemo(
    () => computeProjectedMargin(purchasePrice, estimatedSalePrice),
    [purchasePrice, estimatedSalePrice],
  );

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
      setColors((current) => (h.color && (overwrite || current.length === 0) ? [h.color] : current));
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
      setColors(item.colors ?? []);
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
    // The only destructive action on this form, and it used to happen on the first tap of a 22pt
    // target sitting on the corner of a thumbnail. A photo taken in a shop cannot be retaken later.
    Alert.alert(
      'Remove this photo?',
      photos[index]?.isRemote
        ? 'It will be deleted from the item when you save.'
        : 'This photo has not been uploaded yet, so it will be lost.',
      [
        { text: 'Keep', style: 'cancel' },
        {
          text: 'Remove',
          style: 'destructive',
          onPress: () => {
            haptics.warning();
            setPhotos((prev) => {
              const next = prev.filter((_, i) => i !== index);
              if (next.length > 0 && !next.some((p) => p.isHero)) {
                next[0].isHero = true;
              }
              return next;
            });
          },
        },
      ],
    );
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
    if (colors.length > 0) facts.push(`Color: ${colors.join(', ')}`);
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
      colors,
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

      // Past this point the item exists on the server. A failure attaching photos or issues must
      // never look like "the save failed", because the operator's only move then is to press save
      // again — which creates the item a second time and leaves the first as an orphan with no
      // photos. Report it as what it is: saved, with something still to finish.
      try {
        await persistPhotoChanges(item.id);
        await persistIssueChanges(item.id);
      } catch (attachmentError) {
        haptics.warning();
        setQueuedNotice(
          attachmentError instanceof Error
            ? `Item saved, but its photos did not finish uploading: ${attachmentError.message}`
            : 'Item saved, but its photos did not finish uploading.',
        );
        router.replace(`/inventory/${item.id}`);
        return;
      }
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
      // An edit is worth queueing too. Only a create that already succeeded must not be repeated —
      // that is what would duplicate the item. Editing an existing one offline used to be dropped
      // on the floor: no queue, no notice, the change simply gone.
      const keepForLater = !savedItem && isWorthQueueing(e);

      if (keepForLater && params.sourcingTripId) {
        const queueId = await enqueueOperation('CreateRouteStopPurchase', '/api/inventory', 'POST', body);
        await holdPhotosForSync(queueId);
        router.replace({ pathname: '/active-route', params: { tripId: params.sourcingTripId } });
        return;
      }

      if (keepForLater) {
        const queueId = await (isEdit && params.id
          ? enqueueOperation('UpdateInventory', `/api/inventory/${params.id}`, 'PUT', body)
          : enqueueOperation('CreateInventory', '/api/inventory', 'POST', body));
        await holdPhotosForSync(queueId);
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

  /**
   * Copies the photos that have not been uploaded yet into the app's own storage, against the queued
   * write that will eventually carry them.
   *
   * They live in the camera's cache until this happens, which the system is free to clear, and they
   * only existed in this component's state — so a queued item reached the server with no photos at
   * all, silently and for good. Photos are the one thing that cannot be recaptured: by the time it
   * syncs, the item is back on the shelf.
   */
  async function holdPhotosForSync(queueId: string) {
    for (const photo of photos.filter((candidate) => !candidate.isRemote)) {
      try {
        await savePhoto(queueId, photo.uri, photo.isHero);
      } catch (photoError) {
        // One unreadable file must not cost the others, or the item.
        console.warn('Could not hold a photo for sync:', photoError);
      }
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
          <SelectField
            label={profile?.brandLabel ?? 'Brand'}
            value={brand}
            options={controlledBrands}
            onChange={(value) => { setBrand(value); setModel(''); }}
            placeholder="Select or search..."
            searchable
            containerStyle={styles.flex}
          />
          {shows('Model') ? (
            <SelectField label={labelFor('Model', 'Model')} value={model} options={controlledModels} onChange={setModel} placeholder="Select or search..." searchable containerStyle={styles.flex} />
          ) : null}
        </View>
        {shows('Color') ? (
          <MultiSelectField label="Colors" values={colors} options={controlledColors} max={MAX_COLORS} onChange={setColors} placeholder="Select colors..." searchable />
        ) : null}
        <SelectField
          label="Condition"
          value={condition}
          options={controlledConditions}
          onChange={setCondition}
          placeholder="Select condition..."
          required
        />
      </ExpandableSection>

      {conditionControls.length > 0 ? (
        <ExpandableSection
          title="Condition controls"
          icon="shield-checkmark-outline"
          filledCount={conditionControls.filter((control) => control.answered).length}
          totalCount={conditionControls.length}
        >
          {conditionControls.map((control) => (
            <SelectField key={control.field} label={control.label} value={control.value} options={control.options} onChange={control.onChange} clearable={false} />
          ))}
        </ExpandableSection>
      ) : null}

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
        {shows('ManufacturerPartNumber') || shows('SerialNumber') ? (
          <View style={styles.row}>
            {shows('ManufacturerPartNumber') ? (
              <TextField
                label={labelFor('ManufacturerPartNumber', 'Manufacturer part #')}
                value={manufacturerPartNumber}
                onChangeText={setManufacturerPartNumber}
                placeholder="MPN, style code, part number"
                containerStyle={styles.flex}
              />
            ) : null}
            {shows('SerialNumber') ? (
              <TextField
                label={labelFor('SerialNumber', 'Serial number')}
                value={serialNumber}
                onChangeText={setSerialNumber}
                placeholder="Serial, IMEI, service tag"
                containerStyle={styles.flex}
              />
            ) : null}
          </View>
        ) : null}
        {shows('CountryOfOrigin') || shows('ManufactureYear') ? (
          <View style={styles.row}>
            {shows('CountryOfOrigin') ? (
              <SelectField
                label={labelFor('CountryOfOrigin', 'Country of origin')}
                value={countryOfOrigin}
                options={controlledCountries}
                onChange={setCountryOfOrigin}
                placeholder="Search countries..."
                searchable
                containerStyle={styles.flex}
              />
            ) : null}
            {shows('ManufactureYear') ? (
              <TextField
                label={labelFor('ManufactureYear', 'Manufacture year')}
                value={manufactureYear}
                onChangeText={setManufactureYear}
                keyboardType="number-pad"
                placeholder="YYYY"
                containerStyle={styles.flex}
              />
            ) : null}
          </View>
        ) : null}
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

      {/* ── Size & details — what this kind of item has ── */}
      {detailsTotal > 0 ? (
        <ExpandableSection
          title={profile && profile.key !== 'general' ? `${profile.label} details` : 'Size & details'}
          icon="resize-outline"
          filledCount={detailsFilled}
          totalCount={detailsTotal}
        >
          {shows('Size') ? (
            <>
              <SelectField
                label="Size system"
                value={sizeSystem}
                options={controlledSizeSystems}
                onChange={(value) => { setSizeSystem(value); setSizeValue(''); }}
                placeholder="Select sizing..."
              />
              {sizeSystem ? (
                <SelectField
                  label="Size"
                  value={sizeValue}
                  options={controlledSizes}
                  onChange={setSizeValue}
                  placeholder="Select size..."
                  searchable
                />
              ) : null}
            </>
          ) : null}
          {profileAttributes.map((attribute) => {
            const stored = attributes[attribute.key];
            const setAttribute = (value: string | string[]) => setAttributes((current) => ({ ...current, [attribute.key]: value }));
            const choices = (attribute.options ?? []).map((option) => ({ value: option, label: option }));
            if (isListAttribute(attribute)) {
              const values = Array.isArray(stored) ? stored : stored ? [stored] : [];
              return (
                <MultiSelectField
                  key={attribute.key}
                  label={attribute.label}
                  values={values}
                  options={referenceOptions(undefined, choices, values)}
                  max={attribute.maxValues}
                  onChange={setAttribute}
                  searchable
                />
              );
            }
            const value = Array.isArray(stored) ? (stored[0] ?? '') : (stored ?? '');
            return choices.length > 0 ? (
              <SelectField key={attribute.key} label={attribute.label} value={value} options={referenceOptions(undefined, choices, value)} onChange={setAttribute} searchable={choices.length > 8} />
            ) : (
              <TextField key={attribute.key} label={attribute.label} value={value} onChangeText={setAttribute} />
            );
          })}
        </ExpandableSection>
      ) : null}

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

  // Dynamic category fields
  // Margin bar
  marginBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
  },
});
