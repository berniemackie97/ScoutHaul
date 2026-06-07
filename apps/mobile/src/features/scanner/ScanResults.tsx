import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import { useState } from 'react';
import { Alert, Linking, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { api } from '../../api';
import { Button } from '../../components/Button';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { useHaptics } from '../../hooks/useHaptics';
import { formatMoney } from '../../utils/money';
import type { MarketplaceListing } from '../../domain/types';
import { CompSourcesPanel } from './CompSourcesPanel';
import type { ScanDoneState } from './types';
import { resolveProductImage, resolveProductName } from './types';
import type { VisionErrorKind } from './identifyHelpers';

type IdentifyErrorInfo = { icon: keyof typeof Ionicons.glyphMap; title: string; detail: string; retry: boolean };

// Specific, honest copy per failure — so the user knows WHAT happened, not a vague "no results".
function identifyErrorCopy(kind: VisionErrorKind): IdentifyErrorInfo {
  switch (kind) {
    case 'quota': return { icon: 'hourglass-outline', title: 'Daily AI limit reached', detail: 'Today’s free AI quota is used up (resets ~midnight PT). Enable billing for higher limits — this was not a failed match.', retry: false };
    case 'rate_limited': return { icon: 'time-outline', title: 'AI is busy', detail: 'Too many scans in a short time. Wait a few seconds and try again.', retry: true };
    case 'unavailable': return { icon: 'cloud-offline-outline', title: 'AI service busy', detail: 'The AI service is temporarily unavailable. Try again shortly.', retry: true };
    case 'timeout': return { icon: 'time-outline', title: 'Took too long', detail: 'The request timed out. Check your connection and try again.', retry: true };
    case 'offline': return { icon: 'wifi-outline', title: 'No connection', detail: 'Couldn’t reach the server. Check your internet and try again.', retry: true };
    case 'auth': return { icon: 'lock-closed-outline', title: 'Sign in required', detail: 'Please sign in to identify items.', retry: false };
    default: return { icon: 'alert-circle-outline', title: 'Identification failed', detail: 'Something went wrong analyzing the photo. Try again.', retry: true };
  }
}

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
  addInventoryLabel?: string;
  addInventoryIcon?: keyof typeof Ionicons.glyphMap;
  onResearch: () => void;
  onRefine: (query: string) => void;
  onSaveEstimate: () => Promise<boolean>;
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
  addInventoryLabel = 'Add to Inventory',
  addInventoryIcon = 'add-circle-outline',
  onResearch,
  onRefine,
  onSaveEstimate,
  onSaveProduct,
  onCancelEdit,
}: ScanResultsProps) {
  const { colors: c } = useAppTheme();
  const [estimateSaved, setEstimateSaved] = useState(false);
  const [savingEstimate, setSavingEstimate] = useState(false);
  const productName = resolveProductName(state);
  const heroImage = resolveProductImage(state);
  const hasIdentification = !!productName || !!state.productBrand || !!state.productCategory;
  const hasListings = state.listings.length > 0;
  const hasAnyResults = hasIdentification || hasListings;

  const [refining, setRefining] = useState(false);
  const [refineQuery, setRefineQuery] = useState(productName ?? state.barcode ?? '');

  // Comp search query — product name preferred; skip bare unresolved barcode numbers.
  const compQuery = productName
    ?? (/^\d{8,14}$/.test(state.barcode) ? '' : state.barcode);

  const errInfo = state.identifyError ? identifyErrorCopy(state.identifyError) : null;

  return (
    <ScrollView style={styles.scroll} showsVerticalScrollIndicator={false} bounces={false}>
      <View style={styles.content}>

        {/* ── Product Identification Hero ── */}
        <View style={styles.hero}>
          {heroImage ? (
            <Image source={{ uri: heroImage }} style={styles.heroImage} contentFit="cover" />
          ) : (
            <View style={[styles.heroImage, styles.heroPlaceholder, { backgroundColor: c.surfaceAlt }]}>
              <Ionicons name={state.photoUri ? 'camera-outline' : 'cube-outline'} size={28} color={c.textMuted} />
            </View>
          )}
          <View style={styles.heroInfo}>
            {productName ? (
              <Text variant="bodyBold" numberOfLines={2}>{productName}</Text>
            ) : (
              <Text variant="bodyBold" color={c.textMuted}>Unidentified item</Text>
            )}
            {(state.productBrand || state.productModel || state.productCategory) ? (
              <Text variant="caption" color={c.textSecondary} numberOfLines={1}>
                {[state.productBrand, state.productModel, state.productCategory].filter(Boolean).join(' · ')}
              </Text>
            ) : null}
            {(state.productColor || state.productCondition) ? (
              <Text variant="label" color={c.textMuted} numberOfLines={1}>
                {[state.productColor, state.productCondition].filter(Boolean).join(' · ')}
              </Text>
            ) : null}
            <View style={styles.heroMeta}>
              {hasIdentification ? (
                <StatusPill label="Identified" tone="success" size="sm" />
              ) : (
                <StatusPill label="Not identified" tone="neutral" size="sm" />
              )}
              {state.providersUsed.length > 0 ? (
                <Text variant="label" color={c.textMuted}>{state.providersUsed.join(' · ')}</Text>
              ) : null}
            </View>
          </View>
        </View>

        {/* ── Identify error — surface the real cause (quota/limit/outage), not "no results" ── */}
        {errInfo ? (
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12, padding: 14, borderRadius: 14, borderWidth: 1, backgroundColor: c.surface, borderColor: c.danger }}>
            <Ionicons name={errInfo.icon} size={22} color={c.danger} />
            <View style={{ flex: 1, gap: 2 }}>
              <Text variant="bodyBold" color={c.danger}>{errInfo.title}</Text>
              <Text variant="caption" color={c.textSecondary}>{errInfo.detail}</Text>
            </View>
            {errInfo.retry ? (
              <Pressable onPress={onRescan} hitSlop={8} style={{ paddingHorizontal: 12, paddingVertical: 6, borderRadius: 8, borderWidth: 1, borderColor: c.danger }}>
                <Text variant="label" color={c.danger}>Retry</Text>
              </Pressable>
            ) : null}
          </View>
        ) : null}

        {/* ── Refine — correct a wrong/generic identification and search again ── */}
        {refining ? (
          <View style={[styles.refineBox, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}>
            <Text variant="label" color={c.textMuted}>Correct the product, then search again</Text>
            <TextField
              label=""
              value={refineQuery}
              onChangeText={setRefineQuery}
              placeholder="e.g. Sony WH-1000XM4 headphones"
              autoFocus
              returnKeyType="search"
              onSubmitEditing={() => { if (refineQuery.trim()) { setRefining(false); onRefine(refineQuery.trim()); } }}
            />
            <View style={styles.refineActions}>
              <Button label="Cancel" variant="ghost" size="sm" onPress={() => setRefining(false)} />
              <Button
                label="Search Again"
                icon="search-outline"
                size="sm"
                disabled={!refineQuery.trim()}
                onPress={() => { setRefining(false); onRefine(refineQuery.trim()); }}
              />
            </View>
          </View>
        ) : (
          <Pressable
            onPress={() => { setRefineQuery(productName ?? state.barcode ?? ''); setRefining(true); }}
            style={styles.refineTrigger}
            accessibilityRole="button"
            accessibilityLabel="Refine identification and search again"
          >
            <Ionicons name="pencil-outline" size={14} color={c.textMuted} />
            <Text variant="label" color={c.textSecondary}>
              {hasIdentification ? 'Not quite right? Refine & search again' : 'Enter product name manually'}
            </Text>
          </Pressable>
        )}

        {/* ── Price Range — market data (green) or AI estimate (accent) ── */}
        {state.avgPrice != null ? (
          state.priceSource === 'estimate' ? (
            <View
              style={[styles.priceCard, { backgroundColor: c.accentLight, borderColor: c.accent + '30' }]}
              accessible
              accessibilityRole="text"
              accessibilityLabel={`AI resale estimate: ${formatMoney(state.lowPrice)} to ${formatMoney(state.highPrice)}, estimated from the photo`}
            >
              <View style={styles.priceHeader}>
                <Ionicons name="sparkles" size={16} color={c.accent} />
                <Text variant="bodyBold" color={c.accent} style={{ flex: 1 }}>AI Resale Estimate</Text>
                {state.valuationConfidence ? (
                  <StatusPill
                    label={`${state.valuationConfidence} confidence`}
                    tone={state.valuationConfidence === 'high' ? 'success' : state.valuationConfidence === 'medium' ? 'warning' : 'neutral'}
                    size="sm"
                  />
                ) : null}
              </View>
              <View style={styles.priceRange}>
                <Text variant="caption" color={c.textSecondary}>
                  {state.priceRangeType === 'wide' ? 'Varies widely' : state.priceRangeType === 'tight' ? 'Likely sells for' : 'Likely sells for'}
                </Text>
                <Text variant="metricSmall" color={c.accent}>
                  {formatMoney(state.lowPrice)} – {formatMoney(state.highPrice)}
                </Text>
              </View>
              <Text variant="label" color={c.textMuted}>
                {state.priceRationale ?? 'AI estimate · confirm with comps'}
              </Text>
              <Pressable
                onPress={estimateSaved || savingEstimate ? undefined : async () => {
                  setSavingEstimate(true);
                  const ok = await onSaveEstimate();
                  setSavingEstimate(false);
                  setEstimateSaved(ok);
                }}
                style={[styles.saveEstimateBtn, { borderColor: c.accent + '40' }]}
                accessibilityRole="button"
                accessibilityLabel={estimateSaved ? 'Estimate saved to price history' : 'Save estimate to price history'}
              >
                <Ionicons
                  name={estimateSaved ? 'checkmark-circle' : 'bookmark-outline'}
                  size={15}
                  color={estimateSaved ? c.success : c.accent}
                />
                <Text variant="label" color={estimateSaved ? c.success : c.accent} style={styles.noCaps}>
                  {estimateSaved ? 'Saved to price history' : savingEstimate ? 'Saving…' : 'Save to price history'}
                </Text>
              </Pressable>
            </View>
          ) : (
            <View
              style={[styles.priceCard, { backgroundColor: c.primaryLight, borderColor: c.primary + '20' }]}
              accessible
              accessibilityRole="text"
              accessibilityLabel={`Market value: typically ${formatMoney(state.lowPrice)} to ${formatMoney(state.highPrice)}, average ${formatMoney(state.avgPrice)}`}
            >
              <View style={styles.priceHeader}>
                <Ionicons name="pricetags" size={16} color={c.primary} />
                <Text variant="bodyBold" color={c.primary}>Compare Prices</Text>
              </View>
              <View style={styles.priceRange}>
                <Text variant="caption" color={c.textSecondary}>Typically</Text>
                <Text variant="metricSmall" color={c.primary}>
                  {formatMoney(state.lowPrice)} – {formatMoney(state.highPrice)}
                </Text>
              </View>
              <View style={styles.priceAvg}>
                <Text variant="label" color={c.textMuted}>AVG MARKET VALUE</Text>
                <Text variant="bodyBold" color={c.primary}>{formatMoney(state.avgPrice)}</Text>
              </View>
            </View>
          )
        ) : state.error ? (
          <View style={[styles.alertBox, { backgroundColor: c.dangerLight, borderColor: c.danger + '30' }]}>
            <Ionicons name="alert-circle" size={18} color={c.danger} />
            <Text variant="caption" color={c.danger} style={{ flex: 1 }}>{state.error}</Text>
          </View>
        ) : null}

        {/* ── Comps — real listings found via Google Search grounding (tap to open) ── */}
        {state.comps.length > 0 ? (
          <View style={styles.section}>
            <View style={styles.sectionHeader}>
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.xs }}>
                <Ionicons name={state.sources.length > 0 ? 'globe-outline' : 'sparkles-outline'} size={15} color={state.sources.length > 0 ? c.info : c.accent} />
                <Text variant="bodyBold" color={c.textSecondary}>Comparable Prices</Text>
              </View>
              <Text variant="label" color={c.textMuted}>{state.sources.length > 0 ? 'Live · web search' : 'AI estimate'}</Text>
            </View>
            {(['sold', 'active'] as const).map((status) => {
              const rows = state.comps.filter((cm) => cm.status === status);
              if (rows.length === 0) return null;
              return (
                <View key={status} style={styles.compGroup}>
                  <Text variant="label" color={status === 'sold' ? c.success : c.textMuted}>
                    {status === 'sold' ? 'Recently sold' : 'Active asking'}
                  </Text>
                  {rows.slice(0, 6).map((cm, i) => {
                    const body = (
                      <>
                        <Text variant="bodyBold" color={status === 'sold' ? c.success : c.text}>
                          {formatMoney(cm.price)}
                        </Text>
                        <Text variant="caption" color={c.textMuted} numberOfLines={1} style={{ flex: 1 }}>
                          {[cm.condition, cm.note].filter(Boolean).join(' · ')}
                        </Text>
                        {cm.sourceUrl ? <Ionicons name="open-outline" size={13} color={c.textMuted} /> : null}
                      </>
                    );
                    return cm.sourceUrl ? (
                      <Pressable
                        key={i}
                        onPress={() => void Linking.openURL(cm.sourceUrl!)}
                        style={({ pressed }) => [styles.compRow, { borderColor: c.borderLight }, pressed && { backgroundColor: c.surfaceAlt }]}
                        accessibilityRole="link"
                        accessibilityLabel={`${cm.status} ${formatMoney(cm.price)} ${cm.condition ?? ''} — open listing`}
                      >
                        {body}
                      </Pressable>
                    ) : (
                      <View key={i} style={[styles.compRow, { borderColor: c.borderLight }]}>{body}</View>
                    );
                  })}
                </View>
              );
            })}
            <Text variant="label" color={c.textMuted} style={styles.noCaps}>
              {state.sources.length > 0
                ? 'Real listings found via Google Search. Tap any comp or source to open.'
                : 'Estimated from market knowledge — tap a source below to verify.'}
            </Text>
          </View>
        ) : null}

        {/* ── Sources — the web pages the grounded search used ── */}
        {state.sources.length > 0 ? (
          <View style={styles.section}>
            <Text variant="label" color={c.textMuted}>Sources</Text>
            <View style={styles.sourceChips}>
              {state.sources.slice(0, 6).map((s, i) => (
                <Pressable
                  key={i}
                  onPress={() => void Linking.openURL(s.url)}
                  style={({ pressed }) => [styles.sourceChip, { borderColor: c.borderLight }, pressed && { backgroundColor: c.surfaceAlt }]}
                  accessibilityRole="link"
                  accessibilityLabel={`Open source: ${s.title ?? s.url}`}
                >
                  <Ionicons name="link-outline" size={12} color={c.info} />
                  <Text variant="label" color={c.info} numberOfLines={1} style={[styles.noCaps, { maxWidth: 160 }]}>
                    {s.title ?? 'source'}
                  </Text>
                </Pressable>
              ))}
            </View>
          </View>
        ) : null}

        {/* ── Appraisal detail — what drives value + how to firm up the price ── */}
        {(state.keyValueFactors.length > 0 || state.missingInformation.length > 0 || state.recommendedNextSteps.length > 0) ? (
          <View style={[styles.appraisalBox, { borderColor: c.borderLight }]}>
            {state.keyValueFactors.length > 0 ? (
              <View style={styles.appraisalGroup}>
                <Text variant="label" color={c.textMuted}>What drives value</Text>
                <View style={styles.factorChips}>
                  {state.keyValueFactors.slice(0, 6).map((f, i) => (
                    <View key={i} style={[styles.factorChip, { backgroundColor: c.surfaceAlt }]}>
                      <Text variant="label" color={c.textSecondary} style={styles.noCaps}>{f}</Text>
                    </View>
                  ))}
                </View>
              </View>
            ) : null}
            {state.missingInformation.length > 0 ? (
              <View style={styles.appraisalGroup}>
                <Text variant="label" color={c.warning}>To narrow the price, confirm</Text>
                {state.missingInformation.slice(0, 4).map((m, i) => (
                  <View key={i} style={styles.appraisalRow}>
                    <Ionicons name="help-circle-outline" size={14} color={c.warning} />
                    <Text variant="caption" color={c.textSecondary} style={{ flex: 1 }}>{m}</Text>
                  </View>
                ))}
              </View>
            ) : null}
            {state.recommendedNextSteps.length > 0 ? (
              <View style={styles.appraisalGroup}>
                <Text variant="label" color={c.textMuted}>Next steps</Text>
                {state.recommendedNextSteps.slice(0, 4).map((s, i) => (
                  <View key={i} style={styles.appraisalRow}>
                    <Ionicons name="arrow-forward-circle-outline" size={14} color={c.primary} />
                    <Text variant="caption" color={c.textSecondary} style={{ flex: 1 }}>{s}</Text>
                  </View>
                ))}
              </View>
            ) : null}
          </View>
        ) : null}

        {/* ── Find Comps — deep-links to real sold/active resale sources (the live workflow) ── */}
        {compQuery ? <CompSourcesPanel query={compQuery} /> : null}

        {/* ── Marketplace Listings (Google Lens "Compare Prices" list style) ── */}
        {hasListings ? (
          <View style={styles.section}>
            <View style={styles.sectionHeader}>
              <Text variant="bodyBold" color={c.textSecondary}>
                {state.listings.some((l) => l.isSold) ? 'Sold & Active Listings' : 'Active Listings'}
              </Text>
              <Text variant="label" color={c.textMuted}>{state.listings.length}</Text>
            </View>
            {state.listings.slice(0, 10).map((listing, i) => (
              <ListingRow key={i} listing={listing} productIdentityId={state.productIdentityId} />
            ))}
          </View>
        ) : null}

        {/* ── No Results State (genuine no-match only; errors show the banner above instead) ── */}
        {!hasAnyResults && !state.identifyError ? (
          <View style={[styles.emptyState, { backgroundColor: c.surfaceAlt }]}>
            <Ionicons name="search-outline" size={32} color={c.textMuted} />
            <Text variant="bodyBold" color={c.textSecondary}>No results found</Text>
            <Text variant="caption" color={c.textMuted} style={{ textAlign: 'center' }}>
              Try taking a clearer photo with visible branding, text, or packaging.
              You can also add this item to inventory manually.
            </Text>
          </View>
        ) : null}

        {/* ── Primary Actions ── */}
        <View style={styles.actions}>
          <Button
            label={addInventoryLabel}
            icon={addInventoryIcon}
            size="lg"
            fullWidth
            onPress={onAddInventory}
          />
          <View style={styles.secondaryActions}>
            <Pressable
              onPress={onResearch}
              style={[styles.secondaryBtn, { borderColor: c.borderLight }]}
              accessibilityLabel="Research more"
            >
              <Ionicons name="search-outline" size={18} color={c.primary} />
              <Text variant="caption" color={c.primary}>Research</Text>
            </Pressable>
            <Pressable
              onPress={onEdit}
              style={[styles.secondaryBtn, { borderColor: c.borderLight }]}
              accessibilityLabel="Edit product identity"
            >
              <Ionicons name="create-outline" size={18} color={c.primary} />
              <Text variant="caption" color={c.primary}>{state.productIdentityId ? 'Edit' : 'Save'} Product</Text>
            </Pressable>
            <Pressable
              onPress={onRescan}
              style={[styles.secondaryBtn, { borderColor: c.borderLight }]}
              accessibilityLabel="Scan again"
            >
              <Ionicons name="camera-outline" size={18} color={c.textMuted} />
              <Text variant="caption" color={c.textMuted}>Retake</Text>
            </Pressable>
          </View>
        </View>

        {/* ── Product Edit Form (inline) ── */}
        {editing ? (
          <View style={[styles.editSection, { borderColor: c.border }]}>
            <View style={styles.editHeader}>
              <View style={{ flex: 1 }}>
                <Text variant="bodyBold">{state.productIdentityId ? 'Correct Product' : 'Save Product'}</Text>
                <Text variant="label" color={c.textMuted}>Stored for future scans of this item</Text>
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

// ── Listing Row (Google Lens "Compare Prices" style) ──

// "Sold Jun 2" for realized comps, "Listed Jun 1" for active — the recency a reseller needs.
function compDateLabel(listing: MarketplaceListing): string | null {
  const iso = listing.isSold ? listing.soldDate : listing.listedDate;
  if (!iso) return null;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  const when = d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  return `${listing.isSold ? 'Sold' : 'Listed'} ${when}`;
}

function ListingRow({ listing, productIdentityId }: { listing: MarketplaceListing; productIdentityId: string | null }) {
  const { colors: c } = useAppTheme();
  const haptics = useHaptics();
  const [savedAsComp, setSavedAsComp] = useState(false);

  async function saveAsComp() {
    try {
      await api.addComp({
        productIdentityId: productIdentityId ?? null,
        inventoryItemId: null,
        observationType: listing.isSold ? 'Sold' : 'ActiveListing',
        source: listing.source,
        sourceUrl: listing.itemUrl ?? null,
        title: listing.title,
        price: listing.price,
        shippingPrice: listing.shippingCost ?? null,
        condition: listing.condition ?? null,
        observedAt: new Date().toISOString(),
        soldAt: listing.soldDate ?? null,
        notes: null,
      });
      setSavedAsComp(true);
      haptics.success();
    } catch {
      Alert.alert('Save Failed', 'Could not save this listing as a comp.');
    }
  }

  const totalPrice = listing.price + (listing.shippingCost ?? 0);

  return (
    <View style={[styles.listingRow, { borderColor: c.borderLight }]}>
      <Pressable
        onPress={() => listing.itemUrl ? void Linking.openURL(listing.itemUrl) : undefined}
        style={styles.listingMain}
        disabled={!listing.itemUrl}
        accessibilityRole="link"
        accessibilityLabel={`${listing.title}, ${formatMoney(totalPrice)}${listing.isSold ? ', sold' : ''} on ${listing.source}`}
      >
        {listing.imageUrl ? (
          <Image source={{ uri: listing.imageUrl }} style={styles.listingThumb} contentFit="cover" />
        ) : (
          <View style={[styles.listingThumb, { backgroundColor: c.surfaceAlt, alignItems: 'center', justifyContent: 'center' }]}>
            <Ionicons name="image-outline" size={16} color={c.textMuted} />
          </View>
        )}
        <View style={styles.listingContent}>
          <Text variant="caption" numberOfLines={2}>{listing.title}</Text>
          <View style={styles.listingMeta}>
            <Text variant="label" color={c.textMuted}>{listing.source}</Text>
            {listing.isSold ? (
              <StatusPill label="SOLD" tone="success" size="sm" />
            ) : (
              <Text variant="label" color={c.textMuted}>{listing.condition}</Text>
            )}
            {compDateLabel(listing) ? (
              <Text variant="label" color={c.textMuted}>· {compDateLabel(listing)}</Text>
            ) : null}
          </View>
        </View>
        <View style={styles.listingPrice}>
          <Text variant="bodyBold" color={listing.isSold ? c.success : c.primary}>
            {formatMoney(totalPrice)}
          </Text>
          {listing.shippingCost != null && listing.shippingCost > 0 ? (
            <Text variant="label" color={c.textMuted}>+{formatMoney(listing.shippingCost)} ship</Text>
          ) : listing.shippingCost === 0 ? (
            <Text variant="label" color={c.success}>Free ship</Text>
          ) : null}
        </View>
      </Pressable>
      {/* Save as comp button */}
      <Pressable
        onPress={savedAsComp ? undefined : () => void saveAsComp()}
        hitSlop={6}
        style={[styles.compBtn, savedAsComp && { opacity: 0.5 }]}
        accessibilityLabel={savedAsComp ? 'Saved as comp' : 'Save as comp'}
      >
        <Ionicons
          name={savedAsComp ? 'checkmark-circle' : 'bookmark-outline'}
          size={16}
          color={savedAsComp ? c.success : c.textMuted}
        />
      </Pressable>
    </View>
  );
}

// ── Styles ──

const styles = StyleSheet.create({
  scroll: { paddingHorizontal: spacing.lg, paddingTop: spacing.lg },
  content: { gap: spacing.md, paddingBottom: spacing.xxl },

  // Hero
  hero: { flexDirection: 'row', gap: spacing.md, alignItems: 'center' },
  heroImage: { width: 72, height: 72, borderRadius: radius.md },
  heroPlaceholder: { alignItems: 'center', justifyContent: 'center' },
  heroInfo: { flex: 1, gap: spacing.xs },
  heroMeta: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, flexWrap: 'wrap' },

  // Price card
  priceCard: {
    borderRadius: radius.lg,
    padding: spacing.lg,
    borderWidth: 1,
    gap: spacing.sm,
  },
  priceHeader: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  priceRange: { flexDirection: 'row', alignItems: 'baseline', gap: spacing.sm },
  priceAvg: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  noCaps: { textTransform: 'none', letterSpacing: 0 },
  saveEstimateBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: spacing.xs,
    paddingVertical: spacing.xs,
    paddingHorizontal: spacing.sm,
    borderRadius: radius.full,
    borderWidth: 1,
    marginTop: spacing.xxs,
  },

  // Alert / Info
  alertBox: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  infoBox: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
  },

  // Refine
  refineTrigger: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.xs,
  },
  refineBox: {
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  refineActions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: spacing.sm,
  },

  // Sections
  section: { gap: spacing.sm },
  sectionHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },

  // Listing rows (Google Lens style)
  listingRow: {
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1,
    borderRadius: radius.md,
    overflow: 'hidden',
  },
  listingMain: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.sm,
    paddingLeft: spacing.sm,
  },
  listingThumb: { width: 44, height: 44, borderRadius: radius.sm },
  listingContent: { flex: 1, gap: spacing.xxs },
  listingMeta: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  listingPrice: { alignItems: 'flex-end', paddingRight: spacing.xs },
  compBtn: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.md,
    alignSelf: 'stretch',
    justifyContent: 'center',
  },

  // Comps + sources
  sourceChips: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  sourceChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xxs,
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
    borderRadius: radius.full,
    borderWidth: 1,
  },
  compGroup: { gap: spacing.xxs },
  compRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.xs,
    paddingHorizontal: spacing.sm,
    borderBottomWidth: 1,
  },

  // Appraisal detail
  appraisalBox: {
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  appraisalGroup: { gap: spacing.xs },
  factorChips: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  factorChip: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
    borderRadius: radius.full,
  },
  appraisalRow: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.xs },

  // Empty state
  emptyState: {
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.xl,
    borderRadius: radius.lg,
  },

  // Actions
  actions: { gap: spacing.sm },
  secondaryActions: { flexDirection: 'row', gap: spacing.sm },
  secondaryBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xs,
    paddingVertical: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
  },

  // Edit form
  editSection: { gap: spacing.sm, borderTopWidth: 1, paddingTop: spacing.md },
  editHeader: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
  editRow: { flexDirection: 'row', gap: spacing.sm },
  editActions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm, paddingTop: spacing.xs },
});
