import { Ionicons } from '@expo/vector-icons';
import { recognizeText } from '@infinitered/react-native-mlkit-text-recognition';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Animated, Easing, KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { useHaptics } from '../src/hooks/useHaptics';
import { Card } from '../src/components/Card';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { MarketplaceListing } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

type ScanState =
  | { phase: 'scanning' }
  | { phase: 'searching'; barcode: string }
  | { phase: 'ocr-processing' }
  | { phase: 'ocr-review'; extractedText: string; ocrQuery: string }
  | {
      phase: 'done';
      barcode: string;
      listings: MarketplaceListing[];
      avgPrice: number | null;
      lowPrice: number | null;
      highPrice: number | null;
      totalResults: number;
      error: string | null;
      productTitle: string | null;
      productBrand: string | null;
      productImageUrl: string | null;
      productCategory: string | null;
      productIdentityId: string | null;
      savedObservationCount: number;
      providersUsed: string[];
      providerWarnings: string[];
    };

/** Get best product name — prefer barcode provider, fall back to first listing title */
function resolveProductName(state: Extract<ScanState, { phase: 'done' }>): string | null {
  if (state.productTitle) return state.productTitle;
  if (state.listings.length === 0) return null;
  const raw = state.listings[0].title;
  return raw
    .replace(/\s*-\s*(New|Used|Open Box|Sealed|Free Shipping|Fast Ship|US Seller|Brand New).*/i, '')
    .replace(/\s*\(.*\)\s*$/, '')
    .trim() || raw;
}

/** Get best product image — prefer barcode provider, fall back to first listing image */
function resolveProductImage(state: Extract<ScanState, { phase: 'done' }>): string | null {
  return state.productImageUrl ?? state.listings[0]?.imageUrl ?? null;
}

/**
 * Build a search query from raw OCR text.
 * Prefers lines that look like product names (longer, mixed case, no URLs).
 * Falls back to first meaningful chunk.
 */
function buildOcrQuery(raw: string): string {
  const lines = raw
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 2 && !l.match(/^https?:\/\//i) && !l.match(/^\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}$/));

  // Score lines: prefer longer, mixed-case, no all-caps short fragments
  const scored = lines.map((line) => {
    let score = line.length;
    if (line.length > 8 && line.length < 80) score += 20;
    if (/[a-z]/.test(line) && /[A-Z]/.test(line)) score += 10; // mixed case
    if (/^\d+$/.test(line)) score -= 50; // pure numbers
    if (/^[A-Z\s]{1,6}$/.test(line)) score -= 20; // short all-caps
    return { line, score };
  });

  scored.sort((a, b) => b.score - a.score);
  const best = scored.slice(0, 3).map((s) => s.line);
  return best.join(' ').slice(0, 120).trim();
}

export default function BarcodeScannerScreen() {
  const { colors: c } = useAppTheme();
  const haptics = useHaptics();
  const routeParams = useLocalSearchParams<{ sourcingTripId?: string; sourcingTripStopId?: string }>();
  const [permission, requestPermission] = useCameraPermissions();
  const [state, setState] = useState<ScanState>({ phase: 'scanning' });
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [editForm, setEditForm] = useState({ title: '', brand: '', model: '', category: '', upc: '', ean: '' });
  const cameraRef = useRef<CameraView>(null);
  const scanLine = useRef(new Animated.Value(0)).current;
  const isScanning = state.phase === 'scanning';

  useEffect(() => {
    if (!isScanning) return;
    const anim = Animated.loop(
      Animated.sequence([
        Animated.timing(scanLine, { toValue: 1, duration: 2000, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
        Animated.timing(scanLine, { toValue: 0, duration: 2000, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
      ]),
    );
    anim.start();
    return () => anim.stop();
  }, [isScanning, scanLine]);

  async function lookupBarcode(barcode: string) {
    haptics.success();
    setState({ phase: 'searching', barcode });
    try {
      const result = await api.lookupBarcode(barcode);
      setState({
        phase: 'done',
        barcode,
        listings: result.listings,
        avgPrice: result.averageSoldPrice ?? null,
        lowPrice: result.lowestPrice ?? null,
        highPrice: result.highestPrice ?? null,
        totalResults: result.totalResults,
        error: null,
        productTitle: result.productTitle ?? null,
        productBrand: result.productBrand ?? null,
        productImageUrl: result.productImageUrl ?? null,
        productCategory: result.productCategory ?? null,
        productIdentityId: result.productIdentityId ?? null,
        savedObservationCount: result.savedObservationCount ?? 0,
        providersUsed: result.providersUsed ?? [],
        providerWarnings: result.providerWarnings ?? [],
      });
    } catch (e) {
      setState({
        phase: 'done',
        barcode,
        listings: [],
        avgPrice: null,
        lowPrice: null,
        highPrice: null,
        totalResults: 0,
        error: e instanceof Error ? e.message : 'Lookup failed',
        productTitle: null,
        productBrand: null,
        productImageUrl: null,
        productCategory: null,
        productIdentityId: null,
        savedObservationCount: 0,
        providersUsed: [],
        providerWarnings: [],
      });
    }
  }

  async function captureAndOcr() {
    if (!cameraRef.current) return;
    setState({ phase: 'ocr-processing' });
    try {
      const photo = await cameraRef.current.takePictureAsync({ quality: 0.8, skipProcessing: true });
      if (!photo?.uri) {
        setState({ phase: 'scanning' });
        return;
      }
      const result = await recognizeText(photo.uri);
      const raw = result.text.trim();
      if (!raw) {
        setState({ phase: 'scanning' });
        return;
      }
      // Build a smart query: take first ~120 chars, prefer lines that look like product names
      const query = buildOcrQuery(raw);
      setState({ phase: 'ocr-review', extractedText: raw, ocrQuery: query });
    } catch (e) {
      console.warn('OCR failed:', e);
      setState({ phase: 'scanning' });
    }
  }

  async function searchOcrText(query: string) {
    const q = query.trim();
    if (!q) return;
    setState({ phase: 'searching', barcode: `OCR: ${q.slice(0, 40)}` });
    try {
      const result = await api.lookupSearch(q);
      setState({
        phase: 'done',
        barcode: q,
        listings: result.listings,
        avgPrice: result.averageSoldPrice ?? null,
        lowPrice: result.lowestPrice ?? null,
        highPrice: result.highestPrice ?? null,
        totalResults: result.totalResults,
        error: null,
        productTitle: result.productTitle ?? null,
        productBrand: result.productBrand ?? null,
        productImageUrl: result.productImageUrl ?? null,
        productCategory: result.productCategory ?? null,
        productIdentityId: result.productIdentityId ?? null,
        savedObservationCount: result.savedObservationCount ?? 0,
        providersUsed: result.providersUsed ?? [],
        providerWarnings: result.providerWarnings ?? [],
      });
    } catch (e) {
      setState({
        phase: 'done',
        barcode: q,
        listings: [],
        avgPrice: null,
        lowPrice: null,
        highPrice: null,
        totalResults: 0,
        error: e instanceof Error ? e.message : 'Search failed',
        productTitle: null,
        productBrand: null,
        productImageUrl: null,
        productCategory: null,
        productIdentityId: null,
        savedObservationCount: 0,
        providersUsed: [],
        providerWarnings: [],
      });
    }
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
    if (!title) {
      setSaveError('Product title is required.');
      return;
    }

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
        const updated = await api.updateProductIdentity(state.productIdentityId, body);
        savedId = updated.id;
      } else {
        const created = await api.createProductIdentity(body);
        savedId = created.id;
      }

      // Update scan state with corrected product info
      setState({
        ...state,
        productTitle: title,
        productBrand: body.brand,
        productCategory: body.category,
        productIdentityId: savedId,
      });
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
              <Text variant="caption">Camera permission is needed to scan barcodes.</Text>
              <Button label="Allow Camera" icon="camera-outline" size="lg" fullWidth onPress={() => void requestPermission()} />
            </View>
          </Card>
        </View>
      </Screen>
    );
  }

  const scanLineY = scanLine.interpolate({ inputRange: [0, 1], outputRange: [0, 120] });
  const hasResults = state.phase === 'done' && (state.listings.length > 0 || state.productTitle != null);
  const cornerColor = hasResults ? c.success : state.phase === 'done' ? c.warning : c.primary;
  const productName = state.phase === 'done' ? resolveProductName(state) : null;
  const heroImage = state.phase === 'done' ? resolveProductImage(state) : null;

  return (
    <Screen scroll={false} edges={['bottom']}>
      <View style={styles.container}>
        <CameraView
          ref={cameraRef}
          style={styles.camera}
          facing="back"
          barcodeScannerSettings={{
            barcodeTypes: ['upc_a', 'upc_e', 'ean13', 'ean8', 'code128', 'code39', 'qr'],
          }}
          onBarcodeScanned={
            isScanning
              ? (result) => { if (result.data) void lookupBarcode(result.data); }
              : undefined
          }
        />

        {/* Viewfinder overlay */}
        <View style={styles.overlay} pointerEvents="none">
          <View style={styles.dimTop} />
          <View style={styles.middleRow}>
            <View style={styles.dimSide} />
            <View style={styles.viewfinder}>
              {(['TL', 'TR', 'BL', 'BR'] as const).map((pos) => (
                <View key={pos} style={[styles.corner, styles[`corner${pos}`], { borderColor: cornerColor }]} />
              ))}
              {isScanning ? (
                <Animated.View style={[styles.scanLine, { backgroundColor: c.primary, transform: [{ translateY: scanLineY }] }]} />
              ) : null}
            </View>
            <View style={styles.dimSide} />
          </View>
          <View style={styles.dimBottom} />
        </View>

        {/* Results panel */}
        <KeyboardAvoidingView
          style={[styles.panel, { backgroundColor: c.surface }]}
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          keyboardVerticalOffset={0}
        >
          {state.phase === 'scanning' ? (
            <View style={styles.panelInner}>
              <Text variant="bodyBold">Point at a barcode</Text>
              <Text variant="caption" color={c.textMuted}>UPC, EAN, ISBN, Code 128, or QR</Text>
              <View style={styles.ocrDivider}>
                <View style={[styles.ocrDividerLine, { backgroundColor: c.border }]} />
                <Text variant="label" color={c.textMuted}>or</Text>
                <View style={[styles.ocrDividerLine, { backgroundColor: c.border }]} />
              </View>
              <Pressable
                onPress={() => void captureAndOcr()}
                style={[styles.ocrBtn, { backgroundColor: c.primaryLight }]}
              >
                <Ionicons name="text-outline" size={20} color={c.primary} />
                <Text variant="bodyBold" color={c.primary}>Photo Scan (OCR)</Text>
                <Text variant="label" color={c.textMuted}>Read text from label or packaging</Text>
              </Pressable>
            </View>
          ) : state.phase === 'ocr-processing' ? (
            <View style={styles.panelInner}>
              <View style={styles.searchingRow}>
                <ActivityIndicator size="small" color={c.primary} />
                <Text variant="bodyBold" color={c.primary}>Reading text from image...</Text>
              </View>
            </View>
          ) : state.phase === 'ocr-review' ? (
            <ScrollView style={styles.panelScroll} showsVerticalScrollIndicator={false} bounces={false}>
              <View style={styles.panelInner}>
                <View style={styles.editHeader}>
                  <View style={{ flex: 1 }}>
                    <Text variant="bodyBold">Text detected — review & search</Text>
                    <Text variant="label" color={c.textMuted}>Edit query before searching</Text>
                  </View>
                  <Pressable onPress={() => setState({ phase: 'scanning' })} hitSlop={8}>
                    <Ionicons name="close" size={22} color={c.textMuted} />
                  </Pressable>
                </View>
                <View style={[styles.ocrTextBox, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}>
                  <Text variant="label" color={c.textMuted}>Raw OCR text:</Text>
                  <Text variant="caption" color={c.textSecondary} numberOfLines={4}>{state.extractedText}</Text>
                </View>
                <TextField
                  label="Search query"
                  value={state.ocrQuery}
                  onChangeText={(v) => setState({ ...state, ocrQuery: v })}
                  placeholder="Product name, brand, model..."
                  returnKeyType="search"
                  onSubmitEditing={() => void searchOcrText(state.ocrQuery)}
                />
                <View style={styles.editActions}>
                  <Button label="Back" variant="ghost" size="sm" onPress={() => setState({ phase: 'scanning' })} />
                  <Button
                    label="Search"
                    icon="search-outline"
                    variant="primary"
                    size="sm"
                    onPress={() => void searchOcrText(state.ocrQuery)}
                  />
                </View>
              </View>
            </ScrollView>
          ) : state.phase === 'searching' ? (
            <View style={styles.panelInner}>
              <View style={styles.barcodeRow}>
                <Ionicons name={state.barcode.startsWith('OCR:') ? 'text-outline' : 'barcode-outline'} size={20} color={c.primary} />
                <Text variant="bodyBold" style={styles.mono} numberOfLines={1}>{state.barcode}</Text>
              </View>
              <View style={styles.searchingRow}>
                <ActivityIndicator size="small" color={c.info} />
                <Text variant="caption" color={c.info}>Checking pricing sources...</Text>
              </View>
            </View>
          ) : (
            <ScrollView style={styles.panelScroll} showsVerticalScrollIndicator={false} bounces={false}>
              <View style={styles.panelInner}>
                {/* Product identity — hero image + resolved name */}
                {hasResults ? (
                  <View style={styles.productHero}>
                    {heroImage ? (
                      <Image source={{ uri: heroImage }} style={styles.heroImage} contentFit="cover" />
                    ) : (
                      <View style={[styles.heroImage, styles.heroPlaceholder, { backgroundColor: c.borderLight }]}>
                        <Ionicons name="image-outline" size={32} color={c.textMuted} />
                      </View>
                    )}
                    <View style={styles.heroInfo}>
                      <Text variant="bodyBold" numberOfLines={2}>{productName}</Text>
                      {state.productBrand || state.productCategory ? (
                        <Text variant="caption" color={c.textSecondary} numberOfLines={1}>
                          {[state.productBrand, state.productCategory].filter(Boolean).join(' · ')}
                        </Text>
                      ) : null}
                      <View style={styles.barcodeRow}>
                        <Ionicons name="barcode-outline" size={14} color={c.textMuted} />
                        <Text variant="label" style={styles.mono} color={c.textMuted}>{state.barcode}</Text>
                      </View>
                      {state.totalResults > 0 ? (
                        <StatusPill label={`${state.totalResults} signals`} tone="success" size="sm" />
                      ) : (
                        <StatusPill label="Product found" tone="info" size="sm" />
                      )}
                      {state.providersUsed.length > 0 ? (
                        <Text variant="label" color={c.textMuted} numberOfLines={2}>
                          {state.providersUsed.join(' · ')}
                          {state.savedObservationCount > 0 ? ` · ${state.savedObservationCount} saved` : ''}
                        </Text>
                      ) : null}
                      {state.providerWarnings.length > 0 ? (
                        <Text variant="label" color={c.warning} numberOfLines={2}>
                          {state.providerWarnings[0]}
                        </Text>
                      ) : null}
                    </View>
                  </View>
                ) : (
                  <View style={styles.barcodeRow}>
                    <Ionicons name="barcode-outline" size={18} color={c.warning} />
                    <Text variant="caption" style={styles.mono} color={c.textMuted}>{state.barcode}</Text>
                  </View>
                )}

                {/* Price summary bar */}
                {state.avgPrice != null ? (
                  <View style={[styles.priceSummary, { backgroundColor: c.primaryLight }]}>
                    <View style={styles.priceCol}>
                      <Text variant="label" color={c.textMuted}>LOW</Text>
                      <Text variant="bodyBold" color={c.text}>{formatMoney(state.lowPrice)}</Text>
                    </View>
                    <View style={[styles.priceCol, styles.priceColCenter]}>
                      <Text variant="label" color={c.primary}>AVG</Text>
                      <Text variant="metricSmall" color={c.primary}>{formatMoney(state.avgPrice)}</Text>
                    </View>
                    <View style={styles.priceCol}>
                      <Text variant="label" color={c.textMuted}>HIGH</Text>
                      <Text variant="bodyBold" color={c.text}>{formatMoney(state.highPrice)}</Text>
                    </View>
                  </View>
                ) : state.error ? (
                  <View style={[styles.noPriceBox, { backgroundColor: c.dangerLight, borderColor: c.danger }]}>
                    <Ionicons name="alert-circle-outline" size={18} color={c.danger} />
                    <Text variant="caption" color={c.danger} style={{ flex: 1 }}>{state.error}</Text>
                  </View>
                ) : (
                  <View style={[styles.noPriceBox, { backgroundColor: c.warningLight, borderColor: c.warning }]}>
                    <Ionicons name="information-circle-outline" size={18} color={c.warning} />
                    <View style={{ flex: 1 }}>
                      <Text variant="caption" color={c.text}>No pricing data found</Text>
                      <Text variant="label" color={c.textMuted}>
                        {state.productTitle
                          ? 'Product identified but no marketplace listings matched.'
                          : 'Barcode not recognized. Try searching by name instead.'}
                      </Text>
                    </View>
                  </View>
                )}

                {/* Listing cards */}
                {state.listings.length > 0 ? (
                  <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.listingsScroll} contentContainerStyle={styles.listingsContent}>
                    {state.listings.slice(0, 8).map((listing, i) => (
                      <View key={i} style={[styles.listingCard, { backgroundColor: c.surfaceAlt, borderColor: c.borderLight }]}>
                        {listing.imageUrl ? (
                          <Image source={{ uri: listing.imageUrl }} style={styles.listingImage} contentFit="cover" />
                        ) : (
                          <View style={[styles.listingImage, styles.listingNoImage, { backgroundColor: c.borderLight }]}>
                            <Ionicons name="image-outline" size={24} color={c.textMuted} />
                          </View>
                        )}
                        <View style={styles.listingInfo}>
                          <Text variant="caption" numberOfLines={2} style={styles.listingTitle}>{listing.title}</Text>
                          <Text variant="bodyBold" color={c.primary}>{formatMoney(listing.price)}</Text>
                          <Text variant="label" color={c.textMuted}>{listing.condition}</Text>
                        </View>
                      </View>
                    ))}
                  </ScrollView>
                ) : null}

                {/* Actions */}
                <View style={styles.actions}>
                  <Pressable
                    onPress={() => { setEditing(false); setState({ phase: 'scanning' }); }}
                    style={[styles.iconBtn, { backgroundColor: c.surfaceAlt }]}
                    accessibilityLabel="Rescan"
                  >
                    <Ionicons name="refresh-outline" size={22} color={c.textSecondary} />
                  </Pressable>
                  <Pressable
                    onPress={openEditForm}
                    style={[styles.iconBtn, { backgroundColor: c.primaryLight }]}
                    accessibilityLabel={state.productIdentityId ? 'Correct product info' : 'Add product info'}
                  >
                    <Ionicons name={state.productIdentityId ? 'create-outline' : 'add-outline'} size={22} color={c.primary} />
                  </Pressable>
                  <Pressable
                    onPress={addToInventory}
                    style={[styles.iconBtn, { backgroundColor: c.successLight }]}
                    accessibilityLabel="Quick add to inventory"
                  >
                    <Ionicons name="add-circle-outline" size={22} color={c.success} />
                  </Pressable>
                  <Pressable onPress={continueToSearch} style={[styles.continueBtn, { backgroundColor: c.primary }]}>
                    <Text variant="bodyBold" color={c.textInverse}>Research</Text>
                    <Ionicons name="arrow-forward" size={20} color={c.textInverse} />
                  </Pressable>
                </View>

                {/* Inline product edit form */}
                {editing ? (
                  <View style={[styles.editSection, { borderColor: c.border }]}>
                    <View style={styles.editHeader}>
                      <View style={{ flex: 1 }}>
                        <Text variant="bodyBold">
                          {state.productIdentityId ? 'Correct Product' : 'Add Product'}
                        </Text>
                        <Text variant="label" color={c.textMuted}>
                          Saved as verified data for future scans
                        </Text>
                      </View>
                      <Pressable onPress={() => setEditing(false)} hitSlop={8}>
                        <Ionicons name="close" size={22} color={c.textMuted} />
                      </Pressable>
                    </View>
                    <TextField
                      label="Title *"
                      value={editForm.title}
                      onChangeText={(v) => setEditForm((f) => ({ ...f, title: v }))}
                      placeholder="e.g. Ranch Dressing 16oz"
                      returnKeyType="next"
                      error={saveError && !editForm.title.trim() ? 'Required' : null}
                    />
                    <TextField
                      label="Brand"
                      value={editForm.brand}
                      onChangeText={(v) => setEditForm((f) => ({ ...f, brand: v }))}
                      placeholder="e.g. Hidden Valley"
                    />
                    <TextField
                      label="Model"
                      value={editForm.model}
                      onChangeText={(v) => setEditForm((f) => ({ ...f, model: v }))}
                      placeholder="Optional"
                    />
                    <TextField
                      label="Category"
                      value={editForm.category}
                      onChangeText={(v) => setEditForm((f) => ({ ...f, category: v }))}
                      placeholder="e.g. Food & Beverage"
                    />
                    <View style={styles.editRow}>
                      <TextField
                        label="UPC"
                        value={editForm.upc}
                        onChangeText={(v) => setEditForm((f) => ({ ...f, upc: v }))}
                        keyboardType="number-pad"
                        containerStyle={{ flex: 1 }}
                      />
                      <TextField
                        label="EAN"
                        value={editForm.ean}
                        onChangeText={(v) => setEditForm((f) => ({ ...f, ean: v }))}
                        keyboardType="number-pad"
                        containerStyle={{ flex: 1 }}
                      />
                    </View>
                    {saveError && editForm.title.trim() ? (
                      <Text variant="caption" color={c.danger}>{saveError}</Text>
                    ) : null}
                    <View style={styles.editActions}>
                      <Button
                        label="Cancel"
                        variant="ghost"
                        size="sm"
                        onPress={() => setEditing(false)}
                      />
                      <Button
                        label={saving ? 'Saving...' : 'Save Product'}
                        icon={saving ? undefined : 'checkmark-outline'}
                        variant="primary"
                        size="sm"
                        disabled={saving}
                        onPress={() => void saveProduct()}
                      />
                    </View>
                  </View>
                ) : null}
              </View>
            </ScrollView>
          )}
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
  corner: { position: 'absolute', width: CS, height: CS },
  cornerTL: { top: 0, left: 0, borderTopWidth: CW, borderLeftWidth: CW, borderTopLeftRadius: radius.sm },
  cornerTR: { top: 0, right: 0, borderTopWidth: CW, borderRightWidth: CW, borderTopRightRadius: radius.sm },
  cornerBL: { bottom: 0, left: 0, borderBottomWidth: CW, borderLeftWidth: CW, borderBottomLeftRadius: radius.sm },
  cornerBR: { bottom: 0, right: 0, borderBottomWidth: CW, borderRightWidth: CW, borderBottomRightRadius: radius.sm },
  scanLine: { position: 'absolute', left: spacing.md, right: spacing.md, height: 2, borderRadius: 1, top: 8 },
  panel: { borderTopLeftRadius: radius.xl, borderTopRightRadius: radius.xl, maxHeight: '65%' },
  panelScroll: { paddingHorizontal: spacing.lg, paddingTop: spacing.lg },
  panelInner: { gap: spacing.md, paddingBottom: spacing.xl },
  productHero: { flexDirection: 'row', gap: spacing.md, alignItems: 'center' },
  heroImage: { width: 72, height: 72, borderRadius: radius.md },
  heroPlaceholder: { alignItems: 'center', justifyContent: 'center' },
  heroInfo: { flex: 1, gap: spacing.xs },
  barcodeRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  searchingRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  mono: { fontVariant: ['tabular-nums'], letterSpacing: 0.5 },
  priceSummary: {
    flexDirection: 'row',
    borderRadius: radius.lg,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
  },
  priceCol: { flex: 1, alignItems: 'center', gap: spacing.xxs },
  priceColCenter: { borderLeftWidth: 1, borderRightWidth: 1, borderColor: 'rgba(0,0,0,0.06)' },
  listingsScroll: { marginHorizontal: -spacing.lg },
  listingsContent: { paddingHorizontal: spacing.lg, gap: spacing.sm },
  listingCard: {
    width: 140,
    borderRadius: radius.lg,
    borderWidth: 1,
    overflow: 'hidden',
  },
  listingImage: { width: '100%', height: 100 },
  listingNoImage: { alignItems: 'center', justifyContent: 'center' },
  listingInfo: { padding: spacing.sm, gap: spacing.xxs },
  listingTitle: { lineHeight: 16 },
  noPriceBox: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  iconBtn: {
    width: 44,
    height: 44,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  continueBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    height: 44,
    borderRadius: radius.md,
  },
  ocrDivider: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.xs,
  },
  ocrDividerLine: {
    flex: 1,
    height: 1,
  },
  ocrBtn: {
    alignItems: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.md,
    borderRadius: radius.lg,
  },
  ocrTextBox: {
    gap: spacing.xs,
    padding: spacing.sm,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  editSection: {
    gap: spacing.sm,
    borderTopWidth: 1,
    paddingTop: spacing.md,
  },
  editHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
  editRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  editActions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: spacing.sm,
    paddingTop: spacing.xs,
  },
});
