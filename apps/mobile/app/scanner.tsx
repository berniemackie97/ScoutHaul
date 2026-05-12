import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { Image } from 'expo-image';
import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { Animated, Easing, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import type { MarketplaceListing } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';

type ScanState =
  | { phase: 'scanning' }
  | { phase: 'searching'; barcode: string }
  | {
      phase: 'done';
      barcode: string;
      listings: MarketplaceListing[];
      avgPrice: number | null;
      lowPrice: number | null;
      highPrice: number | null;
      totalResults: number;
      error: string | null;
      /** Product identity from UPCitemdb */
      productTitle: string | null;
      productBrand: string | null;
      productImageUrl: string | null;
      productCategory: string | null;
    };

/** Get best product name — prefer UPCitemdb, fall back to eBay listing title */
function resolveProductName(state: Extract<ScanState, { phase: 'done' }>): string | null {
  if (state.productTitle) return state.productTitle;
  if (state.listings.length === 0) return null;
  const raw = state.listings[0].title;
  return raw
    .replace(/\s*-\s*(New|Used|Open Box|Sealed|Free Shipping|Fast Ship|US Seller|Brand New).*/i, '')
    .replace(/\s*\(.*\)\s*$/, '')
    .trim() || raw;
}

/** Get best product image — prefer UPCitemdb, fall back to first eBay listing */
function resolveProductImage(state: Extract<ScanState, { phase: 'done' }>): string | null {
  return state.productImageUrl ?? state.listings[0]?.imageUrl ?? null;
}

export default function BarcodeScannerScreen() {
  const { colors: c } = useAppTheme();
  const [permission, requestPermission] = useCameraPermissions();
  const [state, setState] = useState<ScanState>({ phase: 'scanning' });
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
    if (firstListing?.condition) params.condition = firstListing.condition;
    router.push({ pathname: '/inventory/edit', params });
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
        <View style={[styles.panel, { backgroundColor: c.surface }]}>
          {state.phase === 'scanning' ? (
            <View style={styles.panelInner}>
              <Text variant="bodyBold">Point at a barcode</Text>
              <Text variant="caption" color={c.textMuted}>UPC, EAN, ISBN, Code 128, or QR</Text>
            </View>
          ) : state.phase === 'searching' ? (
            <View style={styles.panelInner}>
              <View style={styles.barcodeRow}>
                <Ionicons name="barcode-outline" size={20} color={c.primary} />
                <Text variant="bodyBold" style={styles.mono}>{state.barcode}</Text>
              </View>
              <View style={styles.searchingRow}>
                <Ionicons name="search-outline" size={16} color={c.info} />
                <Text variant="caption" color={c.info}>Searching eBay...</Text>
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
                        <StatusPill label={`${state.totalResults} listed`} tone="success" size="sm" />
                      ) : (
                        <StatusPill label="Product found" tone="info" size="sm" />
                      )}
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
                  <Text variant="caption" color={c.danger}>{state.error}</Text>
                ) : (
                  <Text variant="caption" color={c.textMuted}>No marketplace results for this barcode</Text>
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
                    onPress={() => setState({ phase: 'scanning' })}
                    style={[styles.iconBtn, { backgroundColor: c.surfaceAlt }]}
                    accessibilityLabel="Rescan"
                  >
                    <Ionicons name="refresh-outline" size={22} color={c.textSecondary} />
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
              </View>
            </ScrollView>
          )}
        </View>
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
  panel: { borderTopLeftRadius: radius.xl, borderTopRightRadius: radius.xl, maxHeight: '55%' },
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
});
