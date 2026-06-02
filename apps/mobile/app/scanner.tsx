import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
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
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { ScanResults } from '../src/features/scanner/ScanResults';
import { identifyFromPhoto } from '../src/features/scanner/photoIdentify';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { ScanMode, ScanState } from '../src/features/scanner/types';
import {
  makeDoneError,
  makeDoneFromResult,
  resolveProductName,
} from '../src/features/scanner/types';
import { useHaptics } from '../src/hooks/useHaptics';

const SCREEN_WIDTH = Dimensions.get('window').width;
const BARCODE_VF = 260;
const PHOTO_VF = SCREEN_WIDTH - 48;
const CS = 24;
const CW = 3;

export default function BarcodeScannerScreen() {
  const { colors: c } = useAppTheme();
  const haptics = useHaptics();
  const routeParams = useLocalSearchParams<{ sourcingTripId?: string; sourcingTripStopId?: string }>();
  const [permission, requestPermission] = useCameraPermissions();
  const [state, setState] = useState<ScanState>({ phase: 'scanning' });
  const [mode, setMode] = useState<ScanMode>('barcode');
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [editForm, setEditForm] = useState({ title: '', brand: '', model: '', category: '', upc: '', ean: '' });
  const cameraRef = useRef<CameraView>(null);
  const scanLine = useRef(new Animated.Value(0)).current;
  const isScanning = state.phase === 'scanning';
  const isPhotoMode = mode === 'photo-identify';

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
    setState({ phase: 'searching', barcode });
    try {
      setState(makeDoneFromResult(barcode, await api.lookupBarcode(barcode)));
    } catch (e) {
      setState(makeDoneError(barcode, e));
    }
  }

  // ── Photo identify — Cloud Vision + OCR in parallel, then auto-search ──
  async function captureAndIdentify() {
    if (!cameraRef.current) return;
    setState({ phase: 'capturing' });
    haptics.medium();

    try {
      const photo = await cameraRef.current.takePictureAsync({ quality: 0.85 });
      if (!photo?.uri) { setState({ phase: 'scanning' }); return; }

      setState({ phase: 'identifying', photoUri: photo.uri, ocrText: null });

      const identified = await identifyFromPhoto(photo.uri);

      if (identified.confidence === 'high' && identified.searchQuery) {
        // High confidence — auto-search immediately
        haptics.success();
        setState({ phase: 'searching', barcode: identified.searchQuery });
        try {
          const result = await api.lookupSearch(identified.searchQuery);
          setState(makeDoneFromResult(identified.searchQuery, result, photo.uri));
        } catch (e) {
          setState(makeDoneError(identified.searchQuery, e));
        }
      } else {
        // Show review with pre-filled query for user confirmation
        setState({
          phase: 'photo-review',
          photoUri: photo.uri,
          ocrText: identified.rawText,
          query: identified.searchQuery,
          identifyResult: identified,
        });
      }
    } catch {
      setState({ phase: 'scanning' });
    }
  }

  // ── Manual search from review ──
  async function searchQuery(query: string, photoUri?: string) {
    const q = query.trim();
    if (!q) return;
    setState({ phase: 'searching', barcode: q });
    try {
      setState(makeDoneFromResult(q, await api.lookupSearch(q), photoUri));
    } catch (e) {
      setState(makeDoneError(q, e));
    }
  }

  function resetToScanning() {
    setEditing(false);
    setState({ phase: 'scanning' });
  }

  function switchMode(newMode: ScanMode) {
    setMode(newMode);
    resetToScanning();
  }

  function continueToSearch() {
    if (state.phase !== 'done') return;
    const productName = resolveProductName(state);
    router.push({
      pathname: '/manual-search',
      params: {
        query: productName ?? state.barcode,
        barcode: state.barcode,
        ...(productName ? { productName } : {}),
        ...(state.avgPrice != null ? { avgPrice: String(state.avgPrice) } : {}),
        ...(state.productIdentityId ? { productIdentityId: state.productIdentityId } : {}),
        ...(routeParams.sourcingTripId ? { sourcingTripId: routeParams.sourcingTripId } : {}),
        ...(routeParams.sourcingTripStopId ? { sourcingTripStopId: routeParams.sourcingTripStopId } : {}),
      },
    });
  }

  function addToInventory() {
    if (state.phase !== 'done') return;
    const productName = resolveProductName(state);
    const firstListing = state.listings[0];
    const params: Record<string, string> = { barcode: state.barcode };
    if (productName) params.title = productName;
    if (state.avgPrice != null) params.estimatedSalePrice = String(state.avgPrice);
    if (state.productIdentityId) params.productIdentityId = state.productIdentityId;
    if (firstListing?.condition) params.condition = firstListing.condition;
    if (routeParams.sourcingTripId) params.sourcingTripId = routeParams.sourcingTripId;
    if (routeParams.sourcingTripStopId) params.sourcingTripStopId = routeParams.sourcingTripStopId;
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
  const vfWidth = isPhotoMode ? PHOTO_VF : BARCODE_VF;

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

        {/* Viewfinder overlay */}
        <View style={styles.overlay} pointerEvents="none">
          <View style={isPhotoMode ? styles.dimTopPhoto : styles.dimTop} />
          <View style={[styles.middleRow, isPhotoMode && styles.middleRowPhoto]}>
            <View style={isPhotoMode ? styles.dimSidePhoto : styles.dimSide} />
            <View style={[styles.viewfinder, { width: vfWidth }]}>
              {(['TL', 'TR', 'BL', 'BR'] as const).map((pos) => (
                <View key={pos} style={[styles.corner, styles[`corner${pos}`], { borderColor: isPhotoMode ? c.accent : cornerColor }]} />
              ))}
              {isScanning && !isPhotoMode ? (
                <Animated.View style={[styles.scanLine, { backgroundColor: c.primary, transform: [{ translateY: scanLineY }] }]} />
              ) : null}
              {isPhotoMode && isScanning ? (
                <View style={styles.crosshair}>
                  <View style={[styles.crosshairH, { backgroundColor: c.accent }]} />
                  <View style={[styles.crosshairV, { backgroundColor: c.accent }]} />
                </View>
              ) : null}
            </View>
            <View style={isPhotoMode ? styles.dimSidePhoto : styles.dimSide} />
          </View>
          <View style={isPhotoMode ? styles.dimBottomPhoto : styles.dimBottom} />
        </View>

        {/* Mode toggle */}
        {isScanning ? (
          <View style={[styles.modeToggle, { backgroundColor: c.surface }]}>
            <Pressable
              onPress={() => switchMode('barcode')}
              style={[styles.modeTab, !isPhotoMode && [styles.modeTabActive, { backgroundColor: c.primary }]]}
            >
              <Ionicons name="barcode-outline" size={18} color={!isPhotoMode ? c.textInverse : c.textMuted} />
              <Text variant="caption" color={!isPhotoMode ? c.textInverse : c.textMuted}>Barcode</Text>
            </Pressable>
            <Pressable
              onPress={() => switchMode('photo-identify')}
              style={[styles.modeTab, isPhotoMode && [styles.modeTabActive, { backgroundColor: c.accent }]]}
            >
              <Ionicons name="eye-outline" size={18} color={isPhotoMode ? c.textInverse : c.textMuted} />
              <Text variant="caption" color={isPhotoMode ? c.textInverse : c.textMuted}>Identify</Text>
            </Pressable>
          </View>
        ) : null}

        {/* Results panel */}
        <KeyboardAvoidingView
          style={[styles.panel, { backgroundColor: c.surface }]}
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          keyboardVerticalOffset={0}
        >
          {/* Barcode scanning */}
          {state.phase === 'scanning' && !isPhotoMode ? (
            <View style={styles.panelInner}>
              <Text variant="bodyBold">Point at a barcode</Text>
              <Text variant="caption" color={c.textMuted}>UPC, EAN, ISBN, Code 128, or QR</Text>
            </View>

          /* Photo identify — scanning */
          ) : state.phase === 'scanning' && isPhotoMode ? (
            <View style={styles.panelInner}>
              <View style={styles.photoModeHeader}>
                <Ionicons name="eye" size={22} color={c.accent} />
                <Text variant="bodyBold">Visual Product Identification</Text>
              </View>
              <Text variant="caption" color={c.textMuted}>
                Center the product, label, or packaging. AI reads text and identifies the item visually.
              </Text>
              <Pressable
                onPress={() => void captureAndIdentify()}
                style={[styles.captureBtn, { backgroundColor: c.accent }]}
              >
                <View style={[styles.captureDot, { borderColor: c.textInverse }]}>
                  <Ionicons name="camera" size={28} color={c.textInverse} />
                </View>
                <Text variant="bodyBold" color={c.textInverse}>Capture & Identify</Text>
              </Pressable>
            </View>

          /* Capturing */
          ) : state.phase === 'capturing' ? (
            <View style={styles.panelInner}>
              <View style={styles.processingRow}>
                <ActivityIndicator size="small" color={c.accent} />
                <Text variant="bodyBold" color={c.accent}>Capturing...</Text>
              </View>
            </View>

          /* Identifying — ML Kit running */
          ) : state.phase === 'identifying' ? (
            <View style={styles.panelInner}>
              <View style={styles.identifyRow}>
                {state.photoUri ? (
                  <Image source={{ uri: state.photoUri }} style={styles.identifyThumb} contentFit="cover" />
                ) : null}
                <View style={styles.identifyText}>
                  <View style={styles.processingRow}>
                    <ActivityIndicator size="small" color={c.primary} />
                    <Text variant="bodyBold" color={c.primary}>Identifying item...</Text>
                  </View>
                  <Text variant="caption" color={c.textMuted}>Reading text and identifying product</Text>
                </View>
              </View>
            </View>

          /* Photo review — show what was found, let user edit/confirm */
          ) : state.phase === 'photo-review' ? (
            <ScrollView style={styles.panelScroll} showsVerticalScrollIndicator={false} bounces={false}>
              <View style={styles.panelInner}>
                <View style={styles.identifyRow}>
                  {state.photoUri ? (
                    <Image source={{ uri: state.photoUri }} style={styles.reviewThumb} contentFit="cover" />
                  ) : null}
                  <View style={styles.identifyText}>
                    <View style={styles.reviewHeader}>
                      <View style={{ flex: 1 }}>
                        <Text variant="bodyBold">
                          {state.identifyResult?.bestGuessLabel
                            ? 'Product identified'
                            : state.identifyResult?.confidence === 'medium'
                              ? 'Partial match'
                              : state.query
                                ? 'Text detected'
                                : 'No product identified'}
                        </Text>
                        {state.identifyResult ? (
                          <StatusPill
                            label={state.identifyResult.confidence === 'high' ? 'High' : state.identifyResult.confidence === 'medium' ? 'Medium' : 'Low'}
                            tone={state.identifyResult.confidence === 'high' ? 'success' : state.identifyResult.confidence === 'medium' ? 'warning' : 'neutral'}
                            size="sm"
                          />
                        ) : null}
                      </View>
                      <Pressable onPress={resetToScanning} hitSlop={8}>
                        <Ionicons name="close" size={22} color={c.textMuted} />
                      </Pressable>
                    </View>
                  </View>
                </View>

                {/* Detected fields from identification */}
                {state.identifyResult?.brand || state.identifyResult?.category ? (
                  <View style={[styles.detectedFields, { backgroundColor: c.primaryLight }]}>
                    {state.identifyResult.brand ? (
                      <View style={styles.detectedField}>
                        <Text variant="label" color={c.textMuted}>Brand</Text>
                        <Text variant="bodyBold" color={c.primary}>{state.identifyResult.brand}</Text>
                      </View>
                    ) : null}
                    {state.identifyResult.category ? (
                      <View style={styles.detectedField}>
                        <Text variant="label" color={c.textMuted}>Category</Text>
                        <Text variant="bodyBold" color={c.primary}>{state.identifyResult.category}</Text>
                      </View>
                    ) : null}
                    {state.identifyResult.model ? (
                      <View style={styles.detectedField}>
                        <Text variant="label" color={c.textMuted}>Model</Text>
                        <Text variant="bodyBold" color={c.primary}>{state.identifyResult.model}</Text>
                      </View>
                    ) : null}
                  </View>
                ) : null}

                <TextField
                  label="Search marketplace"
                  value={state.query}
                  onChangeText={(v) => setState({ ...state, query: v })}
                  placeholder="Product name, brand, model..."
                  returnKeyType="search"
                  onSubmitEditing={() => void searchQuery(state.query, state.photoUri)}
                />
                <Button
                  label="Search Marketplaces"
                  icon="search-outline"
                  size="lg"
                  fullWidth
                  disabled={!state.query.trim()}
                  onPress={() => void searchQuery(state.query, state.photoUri)}
                />
                <Button label="Retake" variant="ghost" size="sm" icon="camera-outline" onPress={resetToScanning} />
              </View>
            </ScrollView>

          /* Searching */
          ) : state.phase === 'searching' ? (
            <View style={styles.panelInner}>
              <View style={styles.processingRow}>
                <Ionicons name="search-outline" size={20} color={c.primary} />
                <Text variant="bodyBold" style={styles.mono} numberOfLines={1}>{state.barcode}</Text>
              </View>
              <View style={styles.processingRow}>
                <ActivityIndicator size="small" color={c.info} />
                <Text variant="caption" color={c.info}>Checking pricing sources...</Text>
              </View>
            </View>

          /* Results */
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
              onResearch={continueToSearch}
              onSaveProduct={() => void saveProduct()}
              onCancelEdit={() => setEditing(false)}
            />
          ) : null}
        </KeyboardAvoidingView>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  center: { flex: 1, justifyContent: 'center' },
  permContent: { alignItems: 'center', gap: spacing.md },
  camera: { flex: 1, backgroundColor: '#000' },
  overlay: { ...StyleSheet.absoluteFillObject },

  // Barcode mode — small viewfinder, heavy dim
  dimTop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.55)' },
  dimBottom: { flex: 1, backgroundColor: 'rgba(0,0,0,0.55)' },
  dimSide: { flex: 1, backgroundColor: 'rgba(0,0,0,0.55)' },
  middleRow: { flexDirection: 'row', height: BARCODE_VF * 0.55 },

  // Photo mode — large viewfinder, light dim
  dimTopPhoto: { flex: 0.5, backgroundColor: 'rgba(0,0,0,0.35)' },
  dimBottomPhoto: { flex: 1, backgroundColor: 'rgba(0,0,0,0.35)' },
  dimSidePhoto: { width: 24, backgroundColor: 'rgba(0,0,0,0.35)' },
  middleRowPhoto: { flexDirection: 'row', height: PHOTO_VF * 0.85 },

  viewfinder: { height: '100%', overflow: 'hidden' },
  corner: { position: 'absolute', width: CS, height: CS },
  cornerTL: { top: 0, left: 0, borderTopWidth: CW, borderLeftWidth: CW, borderTopLeftRadius: radius.sm },
  cornerTR: { top: 0, right: 0, borderTopWidth: CW, borderRightWidth: CW, borderTopRightRadius: radius.sm },
  cornerBL: { bottom: 0, left: 0, borderBottomWidth: CW, borderLeftWidth: CW, borderBottomLeftRadius: radius.sm },
  cornerBR: { bottom: 0, right: 0, borderBottomWidth: CW, borderRightWidth: CW, borderBottomRightRadius: radius.sm },
  scanLine: { position: 'absolute', left: spacing.md, right: spacing.md, height: 2, borderRadius: 1, top: 8 },

  crosshair: { position: 'absolute', top: '50%', left: '50%', width: 40, height: 40, marginTop: -20, marginLeft: -20 },
  crosshairH: { position: 'absolute', top: 19, left: 4, right: 4, height: 2, borderRadius: 1, opacity: 0.4 },
  crosshairV: { position: 'absolute', left: 19, top: 4, bottom: 4, width: 2, borderRadius: 1, opacity: 0.4 },

  modeToggle: {
    position: 'absolute',
    bottom: '35%',
    alignSelf: 'center',
    flexDirection: 'row',
    borderRadius: radius.full,
    padding: 3,
    gap: 2,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.15,
    shadowRadius: 8,
    elevation: 5,
  },
  modeTab: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.full,
  },
  modeTabActive: {},

  panel: { borderTopLeftRadius: radius.xl, borderTopRightRadius: radius.xl, maxHeight: '55%' },
  panelScroll: { paddingHorizontal: spacing.lg, paddingTop: spacing.lg },
  panelInner: { gap: spacing.md, paddingHorizontal: spacing.lg, paddingTop: spacing.lg, paddingBottom: spacing.xl },

  photoModeHeader: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },

  captureBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.md,
    paddingVertical: spacing.lg,
    borderRadius: radius.xl,
  },
  captureDot: {
    width: 52,
    height: 52,
    borderRadius: 26,
    borderWidth: 3,
    alignItems: 'center',
    justifyContent: 'center',
  },

  processingRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  identifyRow: { flexDirection: 'row', gap: spacing.md, alignItems: 'center' },
  identifyThumb: { width: 56, height: 56, borderRadius: radius.md },
  reviewThumb: { width: 72, height: 72, borderRadius: radius.md },
  identifyText: { flex: 1, gap: spacing.xs },
  reviewHeader: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },

  detectedFields: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
  },
  detectedField: { gap: spacing.xxs },

  mono: { fontVariant: ['tabular-nums'], letterSpacing: 0.5 },
});
