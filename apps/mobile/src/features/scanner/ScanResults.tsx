import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { formatMoney } from '../../utils/money';
import type { ScanDoneState } from './types';
import { resolveProductImage, resolveProductName } from './types';

type ScanResultsProps = {
  state: ScanDoneState;
  editing: boolean;
  editForm: { title: string; brand: string; model: string; category: string; upc: string; ean: string };
  saving: boolean;
  saveError: string | null;
  onEditForm: (updater: (f: ScanResultsProps['editForm']) => ScanResultsProps['editForm']) => void;
  onRescan: () => void;
  onEdit: () => void;
  onAddInventory: () => void;
  onResearch: () => void;
  onSaveProduct: () => void;
  onCancelEdit: () => void;
};

export function ScanResults({
  state,
  editing,
  editForm,
  saving,
  saveError,
  onEditForm,
  onRescan,
  onEdit,
  onAddInventory,
  onResearch,
  onSaveProduct,
  onCancelEdit,
}: ScanResultsProps) {
  const { colors: c } = useAppTheme();
  const hasResults = state.listings.length > 0 || state.productTitle != null;
  const productName = resolveProductName(state);
  const heroImage = resolveProductImage(state);

  return (
    <ScrollView style={styles.panelScroll} showsVerticalScrollIndicator={false} bounces={false}>
      <View style={styles.panelInner}>
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
                <Ionicons name={state.photoUri ? 'camera-outline' : 'barcode-outline'} size={14} color={c.textMuted} />
                <Text variant="label" style={styles.mono} color={c.textMuted} numberOfLines={1}>
                  {state.barcode.length > 30 ? state.barcode.slice(0, 30) + '...' : state.barcode}
                </Text>
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
                <Text variant="label" color={c.warning} numberOfLines={2}>{state.providerWarnings[0]}</Text>
              ) : null}
            </View>
          </View>
        ) : (
          <View style={styles.barcodeRow}>
            <Ionicons name={state.photoUri ? 'camera-outline' : 'barcode-outline'} size={18} color={c.warning} />
            <Text variant="caption" style={styles.mono} color={c.textMuted} numberOfLines={1}>{state.barcode}</Text>
          </View>
        )}

        {/* Price summary */}
        {state.avgPrice != null ? (
          <View style={[styles.priceSummary, { backgroundColor: c.primaryLight }]}>
            <View style={styles.priceCol}>
              <Text variant="label" color={c.textMuted}>LOW</Text>
              <Text variant="bodyBold" color={c.text}>{formatMoney(state.lowPrice)}</Text>
            </View>
            <View style={[styles.priceCol, styles.priceColCenter, { borderColor: c.border }]}>
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
                {state.productTitle ? 'Product identified but no marketplace listings matched.' : 'Try searching by name instead.'}
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
          <Pressable onPress={onRescan} style={[styles.iconBtn, { backgroundColor: c.surfaceAlt }]} accessibilityLabel="Rescan">
            <Ionicons name="refresh-outline" size={22} color={c.textSecondary} />
          </Pressable>
          <Pressable onPress={onEdit} style={[styles.iconBtn, { backgroundColor: c.primaryLight }]} accessibilityLabel="Edit product">
            <Ionicons name={state.productIdentityId ? 'create-outline' : 'add-outline'} size={22} color={c.primary} />
          </Pressable>
          <Pressable onPress={onAddInventory} style={[styles.iconBtn, { backgroundColor: c.successLight }]} accessibilityLabel="Add to inventory">
            <Ionicons name="add-circle-outline" size={22} color={c.success} />
          </Pressable>
          <Pressable onPress={onResearch} style={[styles.continueBtn, { backgroundColor: c.primary }]}>
            <Text variant="bodyBold" color={c.textInverse}>Research</Text>
            <Ionicons name="arrow-forward" size={20} color={c.textInverse} />
          </Pressable>
        </View>

        {/* Product edit form */}
        {editing ? (
          <View style={[styles.editSection, { borderColor: c.border }]}>
            <View style={styles.editHeader}>
              <View style={{ flex: 1 }}>
                <Text variant="bodyBold">{state.productIdentityId ? 'Correct Product' : 'Add Product'}</Text>
                <Text variant="label" color={c.textMuted}>Saved as verified data for future scans</Text>
              </View>
              <Pressable onPress={onCancelEdit} hitSlop={8}>
                <Ionicons name="close" size={22} color={c.textMuted} />
              </Pressable>
            </View>
            <TextField label="Title *" value={editForm.title} onChangeText={(v) => onEditForm((f) => ({ ...f, title: v }))} placeholder="e.g. Sony WH-1000XM5" error={saveError && !editForm.title.trim() ? 'Required' : null} />
            <TextField label="Brand" value={editForm.brand} onChangeText={(v) => onEditForm((f) => ({ ...f, brand: v }))} placeholder="e.g. Sony" />
            <TextField label="Model" value={editForm.model} onChangeText={(v) => onEditForm((f) => ({ ...f, model: v }))} placeholder="Optional" />
            <TextField label="Category" value={editForm.category} onChangeText={(v) => onEditForm((f) => ({ ...f, category: v }))} placeholder="e.g. Electronics" />
            <View style={styles.editRow}>
              <TextField label="UPC" value={editForm.upc} onChangeText={(v) => onEditForm((f) => ({ ...f, upc: v }))} keyboardType="number-pad" containerStyle={{ flex: 1 }} />
              <TextField label="EAN" value={editForm.ean} onChangeText={(v) => onEditForm((f) => ({ ...f, ean: v }))} keyboardType="number-pad" containerStyle={{ flex: 1 }} />
            </View>
            {saveError && editForm.title.trim() ? <Text variant="caption" color={c.danger}>{saveError}</Text> : null}
            <View style={styles.editActions}>
              <Button label="Cancel" variant="ghost" size="sm" onPress={onCancelEdit} />
              <Button label={saving ? 'Saving...' : 'Save Product'} icon={saving ? undefined : 'checkmark-outline'} size="sm" disabled={saving} onPress={onSaveProduct} />
            </View>
          </View>
        ) : null}
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  panelScroll: { paddingHorizontal: spacing.lg, paddingTop: spacing.lg },
  panelInner: { gap: spacing.md, paddingBottom: spacing.xl },
  productHero: { flexDirection: 'row', gap: spacing.md, alignItems: 'center' },
  heroImage: { width: 72, height: 72, borderRadius: radius.md },
  heroPlaceholder: { alignItems: 'center', justifyContent: 'center' },
  heroInfo: { flex: 1, gap: spacing.xs },
  barcodeRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  mono: { fontVariant: ['tabular-nums'], letterSpacing: 0.5 },
  priceSummary: { flexDirection: 'row', borderRadius: radius.lg, paddingVertical: spacing.md, paddingHorizontal: spacing.lg },
  priceCol: { flex: 1, alignItems: 'center', gap: spacing.xxs },
  priceColCenter: { borderLeftWidth: 1, borderRightWidth: 1 },
  listingsScroll: { marginHorizontal: -spacing.lg },
  listingsContent: { paddingHorizontal: spacing.lg, gap: spacing.sm },
  listingCard: { width: 140, borderRadius: radius.lg, borderWidth: 1, overflow: 'hidden' },
  listingImage: { width: '100%', height: 100 },
  listingNoImage: { alignItems: 'center', justifyContent: 'center' },
  listingInfo: { padding: spacing.sm, gap: spacing.xxs },
  listingTitle: { lineHeight: 16 },
  noPriceBox: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm, padding: spacing.md, borderRadius: radius.md, borderWidth: 1 },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  iconBtn: { width: 44, height: 44, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center' },
  continueBtn: { flex: 1, flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.sm, height: 44, borderRadius: radius.md },
  editSection: { gap: spacing.sm, borderTopWidth: 1, paddingTop: spacing.md },
  editHeader: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
  editRow: { flexDirection: 'row', gap: spacing.sm },
  editActions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm, paddingTop: spacing.xs },
});
