import { Ionicons } from '@expo/vector-icons';
import { recognizeText } from '@infinitered/react-native-mlkit-text-recognition';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { Image } from 'expo-image';
import * as Linking from 'expo-linking';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Animated,
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
import { TextField } from '../src/components/TextField';
import { ScanResults } from '../src/features/scanner/ScanResults';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { ScanMode, ScanState } from '../src/features/scanner/types';
import {
  buildSearchQuery,
  makeDoneError,
  makeDoneFromResult,
  resolveProductName,
} from '../src/features/scanner/types';
import { useHaptics } from '../src/hooks/useHaptics';

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

  useEffect(() => {
    if (!isScanning || mode !== 'barcode') return;
    const anim = Animated.loop(
      Animated.sequence([
        Animated.timing(scanLine, { toValue: 1, duration: 2000, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
        Animated.timing(scanLine, { toValue: 0, duration: 2000, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
      ]),
    );
    anim.start();
    return () => anim.stop();
  }, [isScanning, mode, scanLine]);

  async function lookupBarcode(barcode: string) {
    haptics.success();
    setState({ phase: 'searching', barcode });
    try {
      const result = await api.lookupBarcode(barcode);
      setState(makeDoneFromResult(barcode, result));
    } catch (e) {
      setState(makeDoneError(barcode, e));
    }
  }

  async function capturePhoto() {
    if (!cameraRef.current) return;
    setState({ phase: 'capturing' });
    haptics.medium();
    try {
      const photo = await cameraRef.current.takePictureAsync({ quality: 0.85 });
      if (!photo?.uri) {
        setState({ phase: 'scanning' });
        return;
      }

      setState({ phase: 'identifying', photoUri: photo.uri, ocrText: null });

      let ocrText = '';
      try {
        const ocrResult = await recognizeText(photo.uri);
        ocrText = ocrResult.text.trim();
      } catch {
        // OCR failed — still allow manual query
      }

      if (ocrText) {
        const query = buildSearchQuery(ocrText);
        setState({ phase: 'photo-review', photoUri: photo.uri, ocrText, query });
      } else {
        setState({
          phase: 'photo-review',
          photoUri: photo.uri,
          ocrText: '',
          query: '',
        });
      }
    } catch {
      setState({ phase: 'scanning' });
    }
  }

  async function searchQuery(query: string, photoUri?: string) {
    const q = query.trim();
    if (!q) return;
    setState({ phase: 'searching', barcode: q });
    try {
      const result = await api.lookupSearch(q);
      setState(makeDoneFromResult(q, result, photoUri));
    } catch (e) {
      setState(makeDoneError(q, e));
    }
  }

  function openGoogleLens(photoUri: string) {
    // Google Lens intent — opens the image in Google Lens for visual identification.
    // User identifies the product, comes back, types the name in the query field.
    if (Platform.OS === 'android') {
      Linking.openURL('https://lens.google.com');
    } else {
      Linking.openURL('https://lens.google.com');
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
          barcodeScannerSettings={mode === 'barcode' ? {
            barcodeTypes: ['upc_a', 'upc_e', 'ean13', 'ean8', 'code128', 'code39', 'qr'],
          } : undefined}
          onBarcodeScanned={
            isScanning && mode === 'barcode'
              ? (result) => { if (result.data) void lookupBarcode(result.data); }
              : undefined
          }
        />

        {/* Viewfinder overlay */}
        <View style={styles.overlay} pointerEvents="none">
          <View style={styles.dimTop} />
          <View style={styles.middleRow}>
            <View style={styles.dimSide} />
            <View style={[styles.viewfinder, mode === 'photo-identify' && styles.viewfinderPhoto]}>
              {(['TL', 'TR', 'BL', 'BR'] as const).map((pos) => (
                <View key={pos} style={[styles.corner, styles[`corner${pos}`], { borderColor: mode === 'photo-identify' ? c.accent : cornerColor }]} />
              ))}
              {isScanning && mode === 'barcode' ? (
                <Animated.View style={[styles.scanLine, { backgroundColor: c.primary, transform: [{ translateY: scanLineY }] }]} />
              ) : null}
              {mode === 'photo-identify' && isScanning ? (
                <View style={styles.photoTargetCenter}>
                  <Ionicons name="add" size={32} color={c.accent} style={{ opacity: 0.5 }} />
                </View>
              ) : null}
            </View>
            <View style={styles.dimSide} />
          </View>
          <View style={styles.dimBottom} />
        </View>

        {/* Mode toggle — floating above panel */}
        {isScanning ? (
          <View style={[styles.modeToggle, { backgroundColor: c.surface }]}>
            <Pressable
              onPress={() => switchMode('barcode')}
              style={[styles.modeTab, mode === 'barcode' && [styles.modeTabActive, { backgroundColor: c.primary }]]}
            >
              <Ionicons name="barcode-outline" size={18} color={mode === 'barcode' ? c.textInverse : c.textMuted} />
              <Text variant="caption" color={mode === 'barcode' ? c.textInverse : c.textMuted}>Barcode</Text>
            </Pressable>
            <Pressable
              onPress={() => switchMode('photo-identify')}
              style={[styles.modeTab, mode === 'photo-identify' && [styles.modeTabActive, { backgroundColor: c.accent }]]}
            >
              <Ionicons name="camera-outline" size={18} color={mode === 'photo-identify' ? c.textInverse : c.textMuted} />
              <Text variant="caption" color={mode === 'photo-identify' ? c.textInverse : c.textMuted}>Photo ID</Text>
            </Pressable>
          </View>
        ) : null}

        {/* Results panel */}
        <KeyboardAvoidingView
          style={[styles.panel, { backgroundColor: c.surface }]}
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          keyboardVerticalOffset={0}
        >
          {/* ── Scanning state ── */}
          {state.phase === 'scanning' && mode === 'barcode' ? (
            <View style={styles.panelInner}>
              <Text variant="bodyBold">Point at a barcode</Text>
              <Text variant="caption" color={c.textMuted}>UPC, EAN, ISBN, Code 128, or QR</Text>
            </View>
          ) : state.phase === 'scanning' && mode === 'photo-identify' ? (
            <View style={styles.panelInner}>
              <Text variant="bodyBold">Aim at the item</Text>
              <Text variant="caption" color={c.textMuted}>
                Center the product in the viewfinder. Works best with labels, boxes, or clear product shots.
              </Text>
              <Pressable
                onPress={() => void capturePhoto()}
                style={[styles.captureBtn, { backgroundColor: c.accent }]}
              >
                <Ionicons name="camera" size={24} color={c.textInverse} />
                <Text variant="bodyBold" color={c.textInverse}>Capture & Identify</Text>
              </Pressable>
            </View>

          /* ── Capturing / Processing ── */
          ) : state.phase === 'capturing' ? (
            <View style={styles.panelInner}>
              <View style={styles.searchingRow}>
                <ActivityIndicator size="small" color={c.accent} />
                <Text variant="bodyBold" color={c.accent}>Capturing...</Text>
              </View>
            </View>
          ) : state.phase === 'identifying' ? (
            <View style={styles.panelInner}>
              <View style={styles.identifyRow}>
                {state.photoUri ? (
                  <Image source={{ uri: state.photoUri }} style={styles.identifyThumb} contentFit="cover" />
                ) : null}
                <View style={styles.identifyText}>
                  <View style={styles.searchingRow}>
                    <ActivityIndicator size="small" color={c.primary} />
                    <Text variant="bodyBold" color={c.primary}>Reading text...</Text>
                  </View>
                  <Text variant="caption" color={c.textMuted}>Extracting product info from image</Text>
                </View>
              </View>
            </View>

          /* ── Photo Review ── */
          ) : state.phase === 'photo-review' ? (
            <ScrollView style={styles.panelScroll} showsVerticalScrollIndicator={false} bounces={false}>
              <View style={styles.panelInner}>
                <View style={styles.identifyRow}>
                  {state.photoUri ? (
                    <Image source={{ uri: state.photoUri }} style={styles.reviewThumb} contentFit="cover" />
                  ) : null}
                  <View style={styles.identifyText}>
                    <View style={styles.editHeader}>
                      <Text variant="bodyBold">
                        {state.ocrText ? 'Text detected' : 'No text found'}
                      </Text>
                      <Pressable onPress={resetToScanning} hitSlop={8}>
                        <Ionicons name="close" size={22} color={c.textMuted} />
                      </Pressable>
                    </View>
                    {state.ocrText ? (
                      <Text variant="caption" color={c.textSecondary} numberOfLines={2}>
                        {state.ocrText.slice(0, 80)}{state.ocrText.length > 80 ? '...' : ''}
                      </Text>
                    ) : (
                      <Text variant="caption" color={c.textMuted}>
                        Type a product name below, or use Google Lens to identify visually.
                      </Text>
                    )}
                  </View>
                </View>
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
                <View style={styles.altActions}>
                  <Pressable
                    onPress={() => openGoogleLens(state.photoUri)}
                    style={[styles.lensBtn, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}
                  >
                    <Ionicons name="eye-outline" size={20} color={c.textSecondary} />
                    <View style={styles.lensBtnText}>
                      <Text variant="bodyBold">Open Google Lens</Text>
                      <Text variant="caption" color={c.textMuted}>Visual identify, then type name above</Text>
                    </View>
                    <Ionicons name="open-outline" size={16} color={c.textMuted} />
                  </Pressable>
                  <Button label="Retake" variant="ghost" size="sm" icon="camera-outline" onPress={resetToScanning} />
                </View>
              </View>
            </ScrollView>

          /* ── Searching ── */
          ) : state.phase === 'searching' ? (
            <View style={styles.panelInner}>
              <View style={styles.barcodeRow}>
                <Ionicons name="search-outline" size={20} color={c.primary} />
                <Text variant="bodyBold" style={styles.mono} numberOfLines={1}>{state.barcode}</Text>
              </View>
              <View style={styles.searchingRow}>
                <ActivityIndicator size="small" color={c.info} />
                <Text variant="caption" color={c.info}>Checking pricing sources...</Text>
              </View>
            </View>

          /* ── Results ── */
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

const VF = 260;
const CS = 24;
const CW = 3;

const styles = StyleSheet.create({
  container: { flex: 1 },
  center: { flex: 1, justifyContent: 'center' },
  permContent: { alignItems: 'center', gap: spacing.md },
  camera: { flex: 1, backgroundColor: '#000' },
  overlay: { ...StyleSheet.absoluteFillObject },
  dimTop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.55)' },
  dimBottom: { flex: 1, backgroundColor: 'rgba(0,0,0,0.55)' },
  middleRow: { flexDirection: 'row', height: VF * 0.55 },
  dimSide: { flex: 1, backgroundColor: 'rgba(0,0,0,0.55)' },
  viewfinder: { width: VF, height: '100%', overflow: 'hidden' },
  viewfinderPhoto: { width: VF * 1.1, height: '120%' },
  corner: { position: 'absolute', width: CS, height: CS },
  cornerTL: { top: 0, left: 0, borderTopWidth: CW, borderLeftWidth: CW, borderTopLeftRadius: radius.sm },
  cornerTR: { top: 0, right: 0, borderTopWidth: CW, borderRightWidth: CW, borderTopRightRadius: radius.sm },
  cornerBL: { bottom: 0, left: 0, borderBottomWidth: CW, borderLeftWidth: CW, borderBottomLeftRadius: radius.sm },
  cornerBR: { bottom: 0, right: 0, borderBottomWidth: CW, borderRightWidth: CW, borderBottomRightRadius: radius.sm },
  scanLine: { position: 'absolute', left: spacing.md, right: spacing.md, height: 2, borderRadius: 1, top: 8 },
  photoTargetCenter: { position: 'absolute', top: '50%', left: '50%', marginTop: -16, marginLeft: -16 },

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

  panel: { borderTopLeftRadius: radius.xl, borderTopRightRadius: radius.xl, maxHeight: '65%' },
  panelScroll: { paddingHorizontal: spacing.lg, paddingTop: spacing.lg },
  panelInner: { gap: spacing.md, paddingHorizontal: spacing.lg, paddingTop: spacing.lg, paddingBottom: spacing.xl },

  captureBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.md,
    borderRadius: radius.lg,
    marginTop: spacing.xs,
  },

  identifyRow: { flexDirection: 'row', gap: spacing.md, alignItems: 'center' },
  identifyThumb: { width: 56, height: 56, borderRadius: radius.md },
  reviewThumb: { width: 72, height: 72, borderRadius: radius.md },
  identifyText: { flex: 1, gap: spacing.xs },

  altActions: { gap: spacing.sm, paddingTop: spacing.xs },
  lensBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.lg,
    borderWidth: 1,
  },
  lensBtnText: { flex: 1, gap: spacing.xxs },

  barcodeRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  searchingRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  mono: { fontVariant: ['tabular-nums'], letterSpacing: 0.5 },
  editHeader: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
});
