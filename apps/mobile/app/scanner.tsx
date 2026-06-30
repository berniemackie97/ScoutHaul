import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Animated,
  Dimensions,
  Easing,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  View,
} from 'react-native';
import { api } from '../src/api';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Screen } from '../src/components/Screen';
import { Text } from '../src/components/Text';
import { ScanResults } from '../src/features/scanner/ScanResults';
import type { IdentifyResult } from '../src/features/scanner/photoIdentify';
import { identifyFromPhoto } from '../src/features/scanner/photoIdentify';
import { setIdentifyHandoff } from '../src/features/scanner/identifyHandoff';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { ScanMode, ScanState, VisionOverrides } from '../src/features/scanner/types';
import {
  makeDoneError,
  makeDoneFromResult,
  resolveProductName,
} from '../src/features/scanner/types';
import { useHaptics } from '../src/hooks/useHaptics';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

const SCREEN_WIDTH = Dimensions.get('window').width;
const BARCODE_VF = 260;
const PHOTO_VF = SCREEN_WIDTH - 48;

// Barcode: thin precision corners. Photo: bold AI-lens brackets.
const BARCODE_CS = 20;
const BARCODE_CW = 2;
const PHOTO_CS = 28;
const PHOTO_CW = 3.5;

// Carry Gemini's photo-identify metadata into the shared results state (used by both the auto-search
// and manual-refine paths, so it lives in one place).
function toVisionOverrides(r: IdentifyResult): VisionOverrides {
  return {
    brand: r.brand,
    model: r.model,
    category: r.category,
    color: r.color,
    condition: r.condition,
    title: r.title,
    estimatedResaleLow: r.estimatedResaleLow,
    estimatedResaleHigh: r.estimatedResaleHigh,
    idConfidence: r.confidence,
    valuationConfidence: r.valuationConfidence,
    priceRangeType: r.priceRangeType,
    keyValueFactors: r.keyValueFactors,
    missingInformation: r.missingInformation,
    recommendedNextSteps: r.recommendedNextSteps,
    rationale: r.rationale,
    comps: r.comps,
    errorKind: r.errorKind,
    conditionQuality: r.conditionQuality,
  };
}

export default function BarcodeScannerScreen() {
  const { colors: c } = useAppTheme();
  const haptics = useHaptics();
  const routeParams = useLocalSearchParams<{ sourcingTripId?: string; sourcingTripStopId?: string; mode?: ScanMode; fill?: string; context?: string; regenerate?: string }>();
  // "fill" mode: launched from an existing form (e.g. inventory edit) that wants
  // the identified fields handed *back* rather than pushed into a new record.
  const isFillMode = routeParams.fill === '1';
  const [permission, requestPermission] = useCameraPermissions();
  const [state, setState] = useState<ScanState>({ phase: 'scanning' });
  const [mode, setMode] = useState<ScanMode>(routeParams.mode === 'photo-identify' ? 'photo-identify' : 'barcode');
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [editForm, setEditForm] = useState({ title: '', brand: '', model: '', category: '', upc: '', ean: '' });
  const cameraRef = useRef<CameraView>(null);
  const scanLine = useRef(new Animated.Value(0)).current;
  const shutterFlash = useRef(new Animated.Value(0)).current;
  const processingPulse = useRef(new Animated.Value(0)).current;
  const [capturedPhotoUri, setCapturedPhotoUri] = useState<string | null>(null);
  const [showCaptureOverlay, setShowCaptureOverlay] = useState(false);
  const identifyResultRef = useRef<IdentifyResult | null>(null);
  const hasProvidersRef = useRef<boolean | null>(null);

  // Check provider status once on mount — cached for session
  useEffect(() => {
    api.providerStatus()
      .then((s) => { hasProvidersRef.current = s.hasProviders; })
      .catch(() => { hasProvidersRef.current = false; });
  }, []);
  const insets = useSafeAreaInsets();
  const isScanning = state.phase === 'scanning';
  const isPhotoMode = mode === 'photo-identify';
  const showPhotoOverlay = (showCaptureOverlay || !!capturedPhotoUri) && state.phase !== 'scanning';
  const modeColor = isPhotoMode ? c.accent : c.primary;

  // Clear photo overlay when reaching results
  useEffect(() => {
    if (state.phase === 'done') {
      setCapturedPhotoUri(null);
      setShowCaptureOverlay(false);
    }
  }, [state.phase]);

  // Processing pulse animation — runs during capturing/identifying/searching
  useEffect(() => {
    if (state.phase === 'capturing' || state.phase === 'identifying' || (state.phase === 'searching' && capturedPhotoUri)) {
      const anim = Animated.loop(
        Animated.sequence([
          Animated.timing(processingPulse, { toValue: 1, duration: 1500, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
          Animated.timing(processingPulse, { toValue: 0, duration: 1500, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
        ]),
      );
      anim.start();
      return () => anim.stop();
    }
  }, [state.phase, capturedPhotoUri, processingPulse]);

  useEffect(() => {
    if (!isScanning || isPhotoMode) return;
    const anim = Animated.loop(
      Animated.sequence([
        Animated.timing(scanLine, { toValue: 1, duration: 2000, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
        Animated.timing(scanLine, { toValue: 0, duration: 2000, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
      ]),
    );
    anim.start();
    return () => anim.stop();
  }, [isScanning, isPhotoMode, scanLine]);

  // ── Barcode lookup ──
  async function lookupBarcode(barcode: string) {
    haptics.success();
    setState({ phase: 'searching', barcode, step: 'Looking up product…' });

    // Staged label + minimum display so the lookup feels intentional, not a flash (parity
    // with the photo analyze step). The single server call does product + value estimate.
    const start = Date.now();
    const MIN_MS = 1100;
    const stepTimer = setTimeout(() => {
      setState((s) => (s.phase === 'searching' ? { ...s, step: 'Checking resale value…' } : s));
    }, 550);

    try {
      const result = await api.lookupBarcode(barcode);
      clearTimeout(stepTimer);
      const elapsed = Date.now() - start;
      if (elapsed < MIN_MS) await new Promise((r) => setTimeout(r, MIN_MS - elapsed));
      setState(makeDoneFromResult(barcode, result));
    } catch (e) {
      clearTimeout(stepTimer);
      setState(makeDoneError(barcode, e));
    }
  }

  // ── Shutter flash — holds white until overlay is ready, then fades ──
  const fireShutter = useCallback(() => {
    shutterFlash.setValue(1);
    // Hold white for 400ms (covers takePictureAsync latency), then fade
    Animated.sequence([
      Animated.delay(400),
      Animated.timing(shutterFlash, {
        toValue: 0,
        duration: 300,
        easing: Easing.out(Easing.ease),
        useNativeDriver: true,
      }),
    ]).start();
  }, [shutterFlash]);

  // ── Build empty marketplace result shell (no API call needed) ──
  function makeEmptyResult(query: string): import('../src/domain/types').MarketplaceLookupResult {
    return {
      query, listings: [], totalResults: 0,
      averageSoldPrice: null, lowestPrice: null, highestPrice: null,
      productTitle: null, productBrand: null, productImageUrl: null, productCategory: null,
      productIdentityId: null, savedObservationCount: 0, providersUsed: [], providerWarnings: [],
    };
  }

  // ── Marketplace search — skips entirely when no providers configured ──
  async function searchWithRetry(
    primary: string,
    alternates: string[],
    photoUri: string,
    overrides?: VisionOverrides,
  ) {
    // No providers → skip API call entirely, go straight to results with Gemini data
    if (hasProvidersRef.current === false) {
      setState(makeDoneFromResult(primary, makeEmptyResult(primary), photoUri, overrides));
      return;
    }

    setState({ phase: 'searching', barcode: primary, photoUri, step: 'Searching marketplaces...' });

    // Try primary query
    let primaryResult: Awaited<ReturnType<typeof api.lookupSearch>> | null = null;
    try {
      primaryResult = await api.lookupSearch(primary);
      if (primaryResult.totalResults > 0 || primaryResult.productTitle || !primaryResult.providersUsed?.length) {
        setState(makeDoneFromResult(primary, primaryResult, photoUri, overrides));
        return;
      }
    } catch {
      // API failed — show Gemini-only results
      setState(makeDoneFromResult(primary, makeEmptyResult(primary), photoUri, overrides));
      return;
    }

    // Providers exist but returned nothing — retry with alternates
    for (const alt of alternates) {
      setState({ phase: 'searching', barcode: alt, photoUri, step: `Trying "${alt.slice(0, 30)}${alt.length > 30 ? '...' : ''}"` });
      try {
        const result = await api.lookupSearch(alt);
        if (result.totalResults > 0 || result.productTitle) {
          setState(makeDoneFromResult(alt, result, photoUri, overrides));
          return;
        }
      } catch { /* continue */ }
    }

    setState(makeDoneFromResult(primary, primaryResult, photoUri, overrides));
  }

  // ── Photo identify — Gemini multimodal, then auto-search with retries ──
  async function captureAndIdentify() {
    if (!cameraRef.current) return;

    // Immediately show overlay — don't wait for photo to save to disk
    setShowCaptureOverlay(true);
    setState({ phase: 'capturing' });
    haptics.medium();
    fireShutter();

    try {
      const photo = await cameraRef.current.takePictureAsync({ quality: 0.8 });
      if (!photo?.uri) {
        setShowCaptureOverlay(false);
        setState({ phase: 'scanning' });
        return;
      }

      setCapturedPhotoUri(photo.uri);
      setState({ phase: 'identifying', photoUri: photo.uri, step: 'Analyzing image…' });

      // Min display time so the analyze step feels deliberate, never a flash.
      const analyzeStart = Date.now();
      const MIN_ANALYZE_MS = 1600;

      // Cycle the step label while the model works (gives a sense of progress).
      const stepTimer = setTimeout(() => {
        setState((s) => s.phase === 'identifying'
          ? { ...s, step: 'Reading text & details…' }
          : s);
      }, 700);

      let identified: IdentifyResult;
      try {
        identified = await identifyFromPhoto(photo.uri, routeParams.context);
      } finally {
        clearTimeout(stepTimer);
      }
      identifyResultRef.current = identified;

      setState({ phase: 'identifying', photoUri: photo.uri, step: 'Identifying product…' });

      // Hold until min display reached — smooth, not abrupt.
      const elapsed = Date.now() - analyzeStart;
      if (elapsed < MIN_ANALYZE_MS) {
        await new Promise((r) => setTimeout(r, MIN_ANALYZE_MS - elapsed));
      }

      // Gemini metadata carried through to results (id, value, confidences, factors).
      const overrides = toVisionOverrides(identified);

      // ALWAYS go to the rich results screen (comps, estimate, refine) — regardless of
      // confidence. Low confidence just shows a "Low" badge + the Refine control; users still
      // get comp buttons and a value estimate instead of a dead-end review screen.
      // Identify itself failed (quota/limit/outage/timeout) — land on results showing the real
      // cause + CTA, don't blind-search marketplaces with an empty query.
      if (identified.errorKind) {
        haptics.error();
        setState(makeDoneFromResult('', makeEmptyResult(''), photo.uri, overrides));
        return;
      }

      const query = identified.searchQuery || identified.title || '';
      identified.confidence === 'high' ? haptics.success() : haptics.medium();

      if (query) {
        await searchWithRetry(query, identified.alternateQueries, photo.uri, overrides);
      } else {
        // Nothing usable to search — still land on results with the photo + Refine to type a name.
        setState(makeDoneFromResult('', makeEmptyResult(''), photo.uri, overrides));
      }
    } catch {
      setCapturedPhotoUri(null);
      setShowCaptureOverlay(false);
      setState({ phase: 'scanning' });
    }
  }

  // ── Manual search from review ──
  async function searchManual(query: string, photoUri?: string) {
    const q = query.trim();
    if (!q) return;
    const identified = identifyResultRef.current;
    const overrides = identified ? toVisionOverrides(identified) : undefined;
    setCapturedPhotoUri(photoUri ?? null);

    // No providers → skip API call
    if (hasProvidersRef.current === false) {
      setState(makeDoneFromResult(q, makeEmptyResult(q), photoUri, overrides));
      return;
    }

    setState({ phase: 'searching', barcode: q, photoUri, step: 'Searching marketplaces...' });
    try {
      const result = await api.lookupSearch(q);
      if (result.totalResults > 0 || result.productTitle || !result.providersUsed?.length) {
        setState(makeDoneFromResult(q, result, photoUri, overrides));
        return;
      }
      // Providers returned nothing — try alternates
      if (identified?.alternateQueries) {
        for (const alt of identified.alternateQueries) {
          if (alt === q) continue;
          setState({ phase: 'searching', barcode: alt, photoUri, step: `Trying "${alt.slice(0, 30)}..."` });
          try {
            const altResult = await api.lookupSearch(alt);
            if (altResult.totalResults > 0 || altResult.productTitle) {
              setState(makeDoneFromResult(alt, altResult, photoUri, overrides));
              return;
            }
          } catch { /* continue */ }
        }
      }
      setState(makeDoneFromResult(q, result, photoUri, overrides));
    } catch {
      setState(makeDoneFromResult(q, makeEmptyResult(q), photoUri, overrides));
    }
  }

  function resetToScanning() {
    setEditing(false);
    setCapturedPhotoUri(null);
    setShowCaptureOverlay(false);
    identifyResultRef.current = null;
    setState({ phase: 'scanning' });
  }

  function switchMode(newMode: ScanMode) {
    setMode(newMode);
    resetToScanning();
  }

  // Re-run the valuation for the current product on demand (reuses the manual-search path, which
  // rebuilds the Gemini overrides from the identify ref + re-fetches grounded comps).
  function reestimate() {
    if (state.phase !== 'done') return;
    const q = resolveProductName(state) ?? state.barcode;
    if (q) void searchManual(q, state.photoUri ?? undefined);
  }

  function continueToSearch() {
    if (state.phase !== 'done') return;
    const productName = resolveProductName(state);
    const identified = identifyResultRef.current;
    router.push({
      pathname: '/manual-search',
      params: {
        query: productName ?? state.barcode,
        barcode: identified?.barcode ?? ((/^\d{8,14}$/.test(state.barcode)) ? state.barcode : ''),
        ...(productName ? { productName } : {}),
        ...(state.avgPrice != null ? { avgPrice: String(state.avgPrice) } : {}),
        ...(state.productIdentityId ? { productIdentityId: state.productIdentityId } : {}),
        ...(state.productBrand ? { brand: state.productBrand } : {}),
        ...(state.productCategory ? { category: state.productCategory } : {}),
        ...(routeParams.sourcingTripId ? { sourcingTripId: routeParams.sourcingTripId } : {}),
        ...(routeParams.sourcingTripStopId ? { sourcingTripStopId: routeParams.sourcingTripStopId } : {}),
      },
    });
  }

  function addToInventory() {
    if (state.phase !== 'done') return;
    const productName = resolveProductName(state);
    const firstListing = state.listings[0];
    const identified = identifyResultRef.current;
    const params: Record<string, string> = {};

    // Title — product name from marketplace or Vision identification
    if (productName) params.title = productName;

    // Barcode — prefer real barcode from OCR, fall back to search query
    if (identified?.barcode) {
      params.barcode = identified.barcode;
    } else if (state.barcode && /^\d{8,14}$/.test(state.barcode)) {
      params.barcode = state.barcode;
    }

    // Pricing — marketplace avg or Gemini resale estimate
    if (state.avgPrice != null) params.estimatedSalePrice = String(state.avgPrice);

    // Condition — marketplace listing, else Gemini's read of the photo
    const condition = firstListing?.condition ?? identified?.condition ?? state.productCondition;
    if (condition) params.condition = condition;

    // Vision-identified metadata — pre-fill everything Gemini saw
    if (identified?.brand ?? state.productBrand) params.brand = (identified?.brand ?? state.productBrand)!;
    if (identified?.category ?? state.productCategory) params.category = (identified?.category ?? state.productCategory)!;
    if (identified?.model ?? state.productModel) params.model = (identified?.model ?? state.productModel)!;
    if (identified?.color ?? state.productColor) params.color = (identified?.color ?? state.productColor)!;

    // Captured photo — will be auto-added as hero in edit screen
    if (state.photoUri) params.photoUri = state.photoUri;

    // IDs
    if (state.productIdentityId) params.productIdentityId = state.productIdentityId;
    if (routeParams.sourcingTripId) params.sourcingTripId = routeParams.sourcingTripId;
    if (routeParams.sourcingTripStopId) params.sourcingTripStopId = routeParams.sourcingTripStopId;

    // Fill mode: hand the resolved fields back to the caller form and pop, so we
    // don't create a duplicate edit screen on top of the one that launched us.
    if (isFillMode) {
      // Explicit "regenerate" from an existing record asks the AI to refresh the details, so the
      // caller should overwrite its current fields rather than only fill blanks.
      if (routeParams.regenerate === '1') params._overwrite = '1';
      setIdentifyHandoff(params);
      router.back();
      return;
    }

    router.push({ pathname: '/inventory/edit', params });
  }

  function openEditForm() {
    if (state.phase !== 'done') return;
    const barcode = state.barcode.trim();
    const isUpc = /^\d{8}$|^\d{12}$/.test(barcode);
    const isEan = /^\d{13}$|^\d{14}$/.test(barcode);
    setEditForm({
      title: resolveProductName(state) ?? '',
      brand: state.productBrand ?? '',
      model: '',
      category: state.productCategory ?? '',
      upc: isUpc ? barcode : '',
      ean: isEan ? barcode : '',
    });
    setSaveError(null);
    setEditing(true);
  }

  async function saveProduct() {
    if (state.phase !== 'done') return;
    const title = editForm.title.trim();
    if (!title) { setSaveError('Product title is required.'); return; }
    setSaving(true);
    setSaveError(null);
    try {
      const body = {
        title,
        brand: editForm.brand.trim() || null,
        model: editForm.model.trim() || null,
        category: editForm.category.trim() || null,
        upc: editForm.upc.trim() || null,
        ean: editForm.ean.trim() || null,
        searchQuery: state.barcode,
      };
      let savedId: string;
      if (state.productIdentityId) {
        savedId = (await api.updateProductIdentity(state.productIdentityId, body)).id;
      } else {
        savedId = (await api.createProductIdentity(body)).id;
      }
      setState({ ...state, productTitle: title, productBrand: body.brand, productCategory: body.category, productIdentityId: savedId });
      setEditing(false);
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Save failed');
    } finally {
      setSaving(false);
    }
  }

  // ── Persist the AI resale estimate into the product's price history ──
  async function saveEstimateAsComp(): Promise<boolean> {
    if (state.phase !== 'done' || state.priceSource !== 'estimate') return false;
    const low = state.lowPrice;
    const high = state.highPrice;
    const mid = state.avgPrice ?? (low != null && high != null ? Math.round((low + high) / 2) : (low ?? high));
    if (mid == null) return false;

    const title = resolveProductName(state) ?? state.productTitle ?? state.barcode;
    try {
      // Ensure a ProductIdentity exists to attach the observation to.
      let productIdentityId = state.productIdentityId;
      if (!productIdentityId) {
        const barcode = state.barcode.trim();
        const isUpc = /^\d{8}$|^\d{12}$/.test(barcode);
        const isEan = /^\d{13}$|^\d{14}$/.test(barcode);
        productIdentityId = (await api.createProductIdentity({
          title,
          brand: state.productBrand ?? null,
          category: state.productCategory ?? null,
          upc: isUpc ? barcode : null,
          ean: isEan ? barcode : null,
          searchQuery: state.barcode,
        })).id;
        setState((s) => (s.phase === 'done' ? { ...s, productIdentityId } : s));
      }

      await api.addComp({
        productIdentityId,
        inventoryItemId: null,
        observationType: 'Estimate',
        source: 'AI Estimate',
        sourceUrl: null,
        title,
        price: mid,
        shippingPrice: null,
        condition: state.productCondition ?? null,
        observedAt: new Date().toISOString(),
        soldAt: null,
        notes: state.priceRationale ?? `AI estimate: $${low ?? '?'}–$${high ?? '?'}`,
      });

      // Also persist the AI's individual comp data points (sold/active) as observations so the item
      // builds a real price-history trail — not just the single estimate midpoint. Tagged with an
      // "AI Comp" source so they're distinguishable from API-verified comps. Best-effort per comp.
      const observedAt = new Date().toISOString();
      await Promise.all(
        state.comps.map((comp) =>
          api.addComp({
            productIdentityId,
            inventoryItemId: null,
            observationType: comp.status === 'sold' ? 'Sold' : 'ActiveListing',
            source: comp.sourceUrl ? 'AI Comp (web)' : 'AI Comp',
            sourceUrl: comp.sourceUrl ?? null,
            title,
            price: comp.price,
            shippingPrice: null,
            condition: comp.condition ?? state.productCondition ?? null,
            observedAt,
            soldAt: comp.status === 'sold' ? observedAt : null,
            notes: comp.note ?? null,
          }).catch(() => null),
        ),
      );
      haptics.success();
      return true;
    } catch {
      haptics.error();
      return false;
    }
  }

  // ── Permissions ──
  if (!permission) return <Screen><Text variant="body">Checking camera...</Text></Screen>;
  if (!permission.granted) {
    return (
      <Screen>
        <View style={styles.center}>
          <Card>
            <View style={styles.permContent}>
              <Ionicons name="camera-outline" size={48} color={c.textMuted} />
              <Text variant="bodyBold">Camera Access Required</Text>
              <Text variant="caption">Camera permission is needed to scan barcodes and identify items.</Text>
              <Button label="Allow Camera" icon="camera-outline" size="lg" fullWidth onPress={() => void requestPermission()} />
            </View>
          </Card>
        </View>
      </Screen>
    );
  }

  // ── Computed ──
  const scanLineY = scanLine.interpolate({ inputRange: [0, 1], outputRange: [0, 120] });
  const doneState = state.phase === 'done' ? state : null;
  const cornerColor = doneState
    ? (doneState.listings.length > 0 || doneState.productTitle ? c.success : c.warning)
    : c.primary;
  return (
    <Screen scroll={false} edges={['bottom']} keyboardAvoiding={false}>
      <View style={styles.container}>
        <CameraView
          ref={cameraRef}
          style={styles.camera}
          facing="back"
          barcodeScannerSettings={!isPhotoMode ? {
            barcodeTypes: ['upc_a', 'upc_e', 'ean13', 'ean8', 'code128', 'code39', 'qr'],
          } : undefined}
          onBarcodeScanned={
            isScanning && !isPhotoMode
              ? (result) => { if (result.data) void lookupBarcode(result.data); }
              : undefined
          }
        />

        {/* Viewfinder overlay — distinct per mode */}
        <View style={styles.overlay} pointerEvents="none">
          {isPhotoMode ? (
            <>
              <View style={styles.dimTopPhoto} />
              <View style={styles.middleRowPhoto}>
                <View style={styles.dimSidePhoto} />
                <View style={[styles.viewfinder, { width: PHOTO_VF }]}>
                  {(['TL', 'TR', 'BL', 'BR'] as const).map((pos) => (
                    <View key={pos} style={[styles.cornerPhoto, photoCornerStyles[pos], { borderColor: c.accent }]} />
                  ))}
                  {isScanning ? (
                    <View style={styles.crosshair}>
                      <View style={[styles.crosshairH, { backgroundColor: c.accent }]} />
                      <View style={[styles.crosshairV, { backgroundColor: c.accent }]} />
                    </View>
                  ) : null}
                </View>
                <View style={styles.dimSidePhoto} />
              </View>
              <View style={styles.dimBottomPhoto} />
            </>
          ) : (
            <>
              <View style={styles.dimTop} />
              <View style={styles.middleRow}>
                <View style={styles.dimSide} />
                <View style={[styles.viewfinder, { width: BARCODE_VF }]}>
                  {(['TL', 'TR', 'BL', 'BR'] as const).map((pos) => (
                    <View key={pos} style={[styles.cornerBarcode, barcodeCornerStyles[pos], { borderColor: cornerColor }]} />
                  ))}
                  {isScanning ? (
                    <Animated.View style={[styles.scanLine, { backgroundColor: c.primary, transform: [{ translateY: scanLineY }] }]} />
                  ) : null}
                </View>
                <View style={styles.dimSide} />
              </View>
              <View style={styles.dimBottom} />
            </>
          )}
        </View>

        {/* Top header — mode identity + subtle switch */}
        <View style={[styles.topBar, { paddingTop: insets.top + spacing.xs }]}>
          <Pressable onPress={() => router.back()} hitSlop={12} style={styles.topBackBtn}>
            <Ionicons name="chevron-back" size={22} color="#fff" />
          </Pressable>
          <View style={styles.topTitleRow}>
            <Ionicons
              name={isPhotoMode ? 'scan-outline' : 'barcode-outline'}
              size={16}
              color={isPhotoMode ? c.accent : c.primary}
            />
            <Text variant="caption" color="#fff">
              {isPhotoMode ? 'Visual Search' : 'Barcode Scanner'}
            </Text>
          </View>
          {isScanning ? (
            <Pressable
              onPress={() => switchMode(isPhotoMode ? 'barcode' : 'photo-identify')}
              style={[styles.switchPill, { borderColor: 'rgba(255,255,255,0.25)' }]}
              hitSlop={8}
            >
              <Ionicons
                name={isPhotoMode ? 'barcode-outline' : 'camera-outline'}
                size={14}
                color="rgba(255,255,255,0.7)"
              />
              <Text variant="label" color="rgba(255,255,255,0.7)" style={{ textTransform: 'none', letterSpacing: 0 }}>
                {isPhotoMode ? 'Barcode' : 'Photo ID'}
              </Text>
            </Pressable>
          ) : <View style={{ width: 70 }} />}
        </View>

        {/* Shutter flash */}
        <Animated.View
          style={[styles.shutterFlash, { opacity: shutterFlash }]}
          pointerEvents="none"
        />

        {/* Captured photo overlay — freezes view during processing */}
        {showPhotoOverlay ? (
          <View style={styles.photoOverlay}>
            {capturedPhotoUri ? (
              <Image source={{ uri: capturedPhotoUri }} style={styles.photoOverlayImage} contentFit="cover" />
            ) : (
              <View style={styles.photoOverlaySolid} />
            )}
            <Animated.View
              style={[
                styles.photoOverlayGradient,
                {
                  opacity: processingPulse.interpolate({
                    inputRange: [0, 1],
                    outputRange: [0.3, 0.5],
                  }),
                },
              ]}
            />
            <Animated.View
              style={[
                styles.photoScanLine,
                {
                  backgroundColor: c.accent,
                  transform: [{
                    translateY: processingPulse.interpolate({
                      inputRange: [0, 1],
                      outputRange: [0, Dimensions.get('window').height * 0.5],
                    }),
                  }],
                },
              ]}
            />
            {(state.phase === 'identifying' || state.phase === 'searching') && 'step' in state && state.step ? (
              <View style={[styles.processingBadge, { backgroundColor: c.surface }]}>
                <ActivityIndicator size="small" color={c.accent} />
                <Text variant="caption" color={c.accent}>{state.step}</Text>
              </View>
            ) : null}
          </View>
        ) : null}

        {/* Results panel */}
        <KeyboardAvoidingView
          style={[styles.panel, { backgroundColor: c.surface }]}
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          keyboardVerticalOffset={0}
        >
          {/* Barcode scanning — clinical, focused */}
          {state.phase === 'scanning' && !isPhotoMode ? (
            <View style={styles.panelInner}>
              <View style={styles.barcodePanelRow}>
                <View style={[styles.barcodeIndicator, { backgroundColor: c.primaryLight }]}>
                  <View style={[styles.barcodeIndicatorDot, { backgroundColor: c.primary }]} />
                </View>
                <View style={{ flex: 1 }}>
                  <Text variant="bodyBold">Ready to scan</Text>
                  <Text variant="caption" color={c.textMuted}>Point at any barcode — auto-detects format</Text>
                </View>
              </View>
              <View style={styles.barcodeFormats}>
                {['UPC', 'EAN', 'ISBN', 'QR', 'Code 128'].map((fmt) => (
                  <View key={fmt} style={[styles.formatChip, { backgroundColor: c.surfaceAlt }]}>
                    <Text variant="label" color={c.textMuted}>{fmt}</Text>
                  </View>
                ))}
              </View>
            </View>

          /* Photo identify — premium AI lens */
          ) : state.phase === 'scanning' && isPhotoMode ? (
            <View style={styles.panelInner}>
              <Text variant="caption" color={c.textMuted} style={{ textAlign: 'center' }}>
                Center the product, label, or packaging
              </Text>
              <Pressable
                onPress={() => void captureAndIdentify()}
                style={({ pressed }) => [
                  styles.captureBtn,
                  { backgroundColor: c.accent },
                  pressed && { opacity: 0.9, transform: [{ scale: 0.98 }] },
                ]}
              >
                <View style={[styles.captureDot, { borderColor: 'rgba(255,255,255,0.4)' }]}>
                  <Ionicons name="scan" size={26} color={c.textInverse} />
                </View>
                <View>
                  <Text variant="bodyBold" color={c.textInverse}>Identify Product</Text>
                  <Text variant="label" color="rgba(255,255,255,0.6)" style={{ textTransform: 'none', letterSpacing: 0 }}>AI reads text, logos & visuals</Text>
                </View>
              </Pressable>
            </View>

          /* Capturing / Identifying / Searching (photo mode with overlay active) */
          ) : (state.phase === 'capturing' || state.phase === 'identifying') ? (
            <View style={styles.panelInner}>
              <View style={styles.stepIndicator}>
                <StepDot active={state.phase === 'capturing'} done={state.phase === 'identifying'} color={c.accent} />
                <View style={[styles.stepLine, { backgroundColor: state.phase === 'identifying' ? c.accent : c.borderLight }]} />
                <StepDot active={state.phase === 'identifying'} done={false} color={c.accent} />
                <View style={[styles.stepLine, { backgroundColor: c.borderLight }]} />
                <StepDot active={false} done={false} color={c.accent} />
              </View>
              <View style={styles.stepLabels}>
                <Text variant="label" color={state.phase === 'capturing' ? c.accent : c.textMuted}>Capture</Text>
                <Text variant="label" color={state.phase === 'identifying' ? c.accent : c.textMuted}>Analyze</Text>
                <Text variant="label" color={c.textMuted}>Search</Text>
              </View>
              <Text variant="caption" color={c.textMuted} style={{ textAlign: 'center' }}>
                {state.phase === 'capturing' ? 'Taking photo...' : 'AI is reading text and identifying the product'}
              </Text>
            </View>

          /* Searching */
          ) : state.phase === 'searching' ? (
            <View style={styles.panelInner}>
              {capturedPhotoUri ? (
                <>
                  <View style={styles.stepIndicator}>
                    <StepDot active={false} done color={c.accent} />
                    <View style={[styles.stepLine, { backgroundColor: c.accent }]} />
                    <StepDot active={false} done color={c.accent} />
                    <View style={[styles.stepLine, { backgroundColor: c.accent }]} />
                    <StepDot active done={false} color={c.accent} />
                  </View>
                  <View style={styles.stepLabels}>
                    <Text variant="label" color={c.textMuted}>Capture</Text>
                    <Text variant="label" color={c.textMuted}>Analyze</Text>
                    <Text variant="label" color={c.accent}>Search</Text>
                  </View>
                </>
              ) : (
                /* Barcode lookup — branded header with the detected code */
                <View style={styles.barcodeFoundRow}>
                  <View style={[styles.barcodeFoundIcon, { backgroundColor: c.primaryLight }]}>
                    <Ionicons name="barcode" size={22} color={c.primary} />
                  </View>
                  <View style={{ flex: 1 }}>
                    <Text variant="label" color={c.textMuted}>Barcode detected</Text>
                    <Text variant="bodyBold" style={styles.mono} numberOfLines={1}>{state.barcode}</Text>
                  </View>
                  <Ionicons name="checkmark-circle" size={20} color={c.success} />
                </View>
              )}
              <View style={styles.processingRow}>
                <ActivityIndicator size="small" color={c.info} />
                <Text variant="caption" color={c.info}>
                  {state.step ?? 'Checking pricing sources…'}
                </Text>
              </View>
            </View>

          /* Results — clear photo overlay */
          ) : state.phase === 'done' ? (
            <ScanResults
              state={state}
              editing={editing}
              editForm={editForm}
              saving={saving}
              saveError={saveError}
              onEditForm={setEditForm}
              onRescan={resetToScanning}
              onEdit={openEditForm}
              onAddInventory={addToInventory}
              addInventoryLabel={isFillMode ? 'Use These Details' : 'Add to Inventory'}
              addInventoryIcon={isFillMode ? 'checkmark-circle-outline' : 'add-circle-outline'}
              onResearch={continueToSearch}
              onRefine={(q) => void searchManual(q, state.photoUri ?? undefined)}
              onReestimate={reestimate}
              onSaveEstimate={saveEstimateAsComp}
              onSaveProduct={() => void saveProduct()}
              onCancelEdit={() => setEditing(false)}
            />
          ) : null}
        </KeyboardAvoidingView>
      </View>
    </Screen>
  );
}

function StepDot({ active, done, color }: { active: boolean; done: boolean; color: string }) {
  const { colors: c } = useAppTheme();
  return (
    <View
      style={[
        stepStyles.dot,
        done && { backgroundColor: color, borderColor: color },
        active && { borderColor: color, borderWidth: 2.5 },
        !done && !active && { borderColor: c.borderLight },
      ]}
    >
      {done ? <Ionicons name="checkmark" size={10} color="#fff" /> : null}
      {active ? <View style={[stepStyles.dotInner, { backgroundColor: color }]} /> : null}
    </View>
  );
}

const stepStyles = StyleSheet.create({
  dot: {
    width: 22,
    height: 22,
    borderRadius: 11,
    borderWidth: 2,
    alignItems: 'center',
    justifyContent: 'center',
  },
  dotInner: {
    width: 8,
    height: 8,
    borderRadius: 4,
  },
});

// Mode-specific corner styles
const barcodeCornerStyles = StyleSheet.create({
  TL: { top: 0, left: 0, borderTopWidth: BARCODE_CW, borderLeftWidth: BARCODE_CW, borderTopLeftRadius: 4 },
  TR: { top: 0, right: 0, borderTopWidth: BARCODE_CW, borderRightWidth: BARCODE_CW, borderTopRightRadius: 4 },
  BL: { bottom: 0, left: 0, borderBottomWidth: BARCODE_CW, borderLeftWidth: BARCODE_CW, borderBottomLeftRadius: 4 },
  BR: { bottom: 0, right: 0, borderBottomWidth: BARCODE_CW, borderRightWidth: BARCODE_CW, borderBottomRightRadius: 4 },
});

const photoCornerStyles = StyleSheet.create({
  TL: { top: 0, left: 0, borderTopWidth: PHOTO_CW, borderLeftWidth: PHOTO_CW, borderTopLeftRadius: radius.md },
  TR: { top: 0, right: 0, borderTopWidth: PHOTO_CW, borderRightWidth: PHOTO_CW, borderTopRightRadius: radius.md },
  BL: { bottom: 0, left: 0, borderBottomWidth: PHOTO_CW, borderLeftWidth: PHOTO_CW, borderBottomLeftRadius: radius.md },
  BR: { bottom: 0, right: 0, borderBottomWidth: PHOTO_CW, borderRightWidth: PHOTO_CW, borderBottomRightRadius: radius.md },
});

const styles = StyleSheet.create({
  container: { flex: 1 },
  center: { flex: 1, justifyContent: 'center' },
  permContent: { alignItems: 'center', gap: spacing.md },
  camera: { flex: 1, backgroundColor: '#000' },
  overlay: { ...StyleSheet.absoluteFillObject },

  // Barcode mode — tight, clinical, heavy dim
  dimTop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)' },
  dimBottom: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)' },
  dimSide: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)' },
  middleRow: { flexDirection: 'row', height: BARCODE_VF * 0.55 },

  // Photo mode — expansive, warmer, lighter dim
  dimTopPhoto: { flex: 0.4, backgroundColor: 'rgba(0,0,0,0.3)' },
  dimBottomPhoto: { flex: 1, backgroundColor: 'rgba(0,0,0,0.3)' },
  dimSidePhoto: { width: 24, backgroundColor: 'rgba(0,0,0,0.3)' },
  middleRowPhoto: { flexDirection: 'row', height: PHOTO_VF * 0.85 },

  viewfinder: { height: '100%', overflow: 'hidden' },
  cornerBarcode: { position: 'absolute', width: BARCODE_CS, height: BARCODE_CS },
  cornerPhoto: { position: 'absolute', width: PHOTO_CS, height: PHOTO_CS },
  scanLine: { position: 'absolute', left: spacing.md, right: spacing.md, height: 2, borderRadius: 1, top: 8 },

  crosshair: { position: 'absolute', top: '50%', left: '50%', width: 44, height: 44, marginTop: -22, marginLeft: -22 },
  crosshairH: { position: 'absolute', top: 21, left: 4, right: 4, height: 1.5, borderRadius: 1, opacity: 0.35 },
  crosshairV: { position: 'absolute', left: 21, top: 4, bottom: 4, width: 1.5, borderRadius: 1, opacity: 0.35 },

  // Top header bar
  topBar: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    zIndex: 8,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.md,
    paddingBottom: spacing.sm,
  },
  topBackBtn: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: 'rgba(0,0,0,0.3)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  topTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  switchPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.full,
    borderWidth: 1,
  },

  panel: { borderTopLeftRadius: radius.xl, borderTopRightRadius: radius.xl, maxHeight: '65%' },
  panelScroll: { paddingHorizontal: spacing.lg, paddingTop: spacing.lg },
  panelInner: { gap: spacing.md, paddingHorizontal: spacing.lg, paddingTop: spacing.lg, paddingBottom: spacing.xl },

  // Barcode panel
  barcodePanelRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  barcodeIndicator: {
    width: 40,
    height: 40,
    borderRadius: 20,
    alignItems: 'center',
    justifyContent: 'center',
  },
  barcodeIndicatorDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
  },
  barcodeFormats: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
  },
  formatChip: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xs,
    borderRadius: radius.sm,
  },

  // Photo capture button
  captureBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.md,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.xl,
  },
  captureDot: {
    width: 48,
    height: 48,
    borderRadius: 24,
    borderWidth: 2.5,
    alignItems: 'center',
    justifyContent: 'center',
  },

  processingRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  barcodeFoundRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  barcodeFoundIcon: {
    width: 44,
    height: 44,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  mono: { fontVariant: ['tabular-nums'], letterSpacing: 0.5 },

  // Shutter flash
  shutterFlash: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: '#fff',
    zIndex: 10,
  },

  // Captured photo overlay
  photoOverlay: {
    ...StyleSheet.absoluteFillObject,
    zIndex: 5,
  },
  photoOverlayImage: {
    ...StyleSheet.absoluteFillObject,
  },
  photoOverlaySolid: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: '#1a1a1a',
  },
  photoOverlayGradient: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: '#000',
  },
  photoScanLine: {
    position: 'absolute',
    left: 0,
    right: 0,
    height: 2,
    opacity: 0.6,
  },
  processingBadge: {
    position: 'absolute',
    top: '40%',
    alignSelf: 'center',
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
    borderRadius: radius.full,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.2,
    shadowRadius: 12,
    elevation: 8,
  },

  // Step indicator
  stepIndicator: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 0,
  },
  stepLine: {
    height: 2,
    width: 40,
    borderRadius: 1,
  },
  stepLabels: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
  },
});
