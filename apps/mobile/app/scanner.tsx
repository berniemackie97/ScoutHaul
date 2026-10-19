import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Alert, View } from 'react-native';
import { api } from '../src/api';
import type { FoundComp } from '../src/features/appraisal/AddCompCard';
import { AppraisalView, type ResultAction } from '../src/features/appraisal/AppraisalView';
import type { AppraisalState } from '../src/features/appraisal/appraisalState';
import type { InventoryItem } from '../src/domain/types';
import { formatMoney } from '../src/utils/money';
import { DetailsEntry } from '../src/features/appraisal/DetailsEntry';
import type { FailureRemedy } from '../src/features/appraisal/failures';
import { handOffScan } from '../src/features/appraisal/handoff';
import { conditionFromGrade } from '../src/features/appraisal/labels';
import { ScoutCamera, type CaptureMode } from '../src/features/appraisal/ScoutCamera';
import type { Identification } from '../src/features/appraisal/types';
import { useAppraisal } from '../src/features/appraisal/useAppraisal';
import { describeCompOutcome, saveComp } from '../src/services/saveComp';

type Entry = { kind: 'camera' } | { kind: 'type'; photoUri: string | null };

type ScannerParams = {
  mode?: 'barcode' | 'photo' | 'details' | 'item';
  /** With mode=item: price this item in stock and keep the appraisal in its history. */
  inventoryItemId?: string;
  /** Typed details to identify straight away. */
  details?: string;
  /** Facts the caller already knows, sent along with a photo. */
  context?: string;
  /** Opened from the item editor: hand the result back instead of creating an item. */
  fill?: '1';
  /** With fill, replace the editor's fields rather than only filling blanks. */
  regenerate?: '1';
  sourcingTripId?: string;
  sourcingTripStopId?: string;
};

export default function ScannerScreen() {
  const params = useLocalSearchParams<ScannerParams>();
  const { state, scan, reprice, appraiseKnown, linkProduct, reset } = useAppraisal();
  const [mode, setMode] = useState<CaptureMode>(params.mode === 'photo' ? 'photo' : 'barcode');
  const [entry, setEntry] = useState<Entry>(params.mode === 'details' ? { kind: 'type', photoUri: null } : { kind: 'camera' });
  const [rememberedBarcode, setRememberedBarcode] = useState<string | null>(null);
  const [askingPrice, setAskingPrice] = useState('');
  const startedFromParams = useRef(false);
  const fillMode = params.fill === '1';
  const context = params.context?.trim() || undefined;

  useEffect(() => {
    const details = params.details?.trim();
    if (startedFromParams.current || !details) return;
    startedFromParams.current = true;
    void scan({ kind: 'details', details });
  }, [params.details, scan]);

  useEffect(() => {
    const inventoryItemId = params.inventoryItemId;
    if (startedFromParams.current || params.mode !== 'item' || !inventoryItemId) return;
    startedFromParams.current = true;
    api
      .inventoryItem(inventoryItemId)
      .then((stocked) => appraiseKnown(identificationOf(stocked), { kind: 'item', inventoryItemId, title: stocked.title }))
      .catch((error: unknown) => {
        Alert.alert("Couldn't open the item", error instanceof Error ? error.message : 'Try again.');
        router.back();
      });
  }, [params.mode, params.inventoryItemId, appraiseKnown]);

  function startOver(next: Entry = { kind: 'camera' }) {
    reset();
    setRememberedBarcode(null);
    setAskingPrice('');
    setEntry(next);
  }

  function handleRemedy(remedy: FailureRemedy) {
    if (state.phase === 'ready') return;
    switch (remedy) {
      case 'photo':
        setMode('photo');
        startOver();
        return;
      case 'details':
        startOver({ kind: 'type', photoUri: state.input.kind === 'photo' ? state.input.photoUri : null });
        return;
      case 'signIn':
        router.push({ pathname: '/account', params: { mode: 'signin' } });
        return;
      case 'retry':
      case 'wait':
        if (state.phase === 'unpriced') reprice(state.item, state.input);
        else void scan(state.input);
    }
  }

  if (state.phase === 'ready' && params.mode === 'item') {
    return (
      <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator />
      </View>
    );
  }

  if (state.phase === 'ready') {
    return entry.kind === 'type' ? (
      <DetailsEntry
        initial={entry.photoUri ? '' : context ?? ''}
        photoUri={entry.photoUri}
        onSubmit={(details) =>
          void scan(entry.photoUri ? { kind: 'photo', photoUri: entry.photoUri, details } : { kind: 'details', details })
        }
        onCamera={() => setEntry({ kind: 'camera' })}
        onClose={() => router.back()}
      />
    ) : (
      <ScoutCamera
        mode={mode}
        onModeChange={setMode}
        onBarcode={(barcode) => void scan({ kind: 'barcode', barcode })}
        onPhoto={(photoUri) => void scan({ kind: 'photo', photoUri, details: context })}
        onType={() => setEntry({ kind: 'type', photoUri: null })}
        onClose={() => router.back()}
      />
    );
  }

  const item = state.phase === 'appraised' || state.phase === 'unpriced' || state.phase === 'appraising' ? state.item : null;
  const listPrice = state.phase === 'appraised' ? state.appraisal.price.list : null;
  const photoUri = state.input.kind === 'photo' ? state.input.photoUri : null;

  function applyResult() {
    if (!item) return;
    const condition = conditionFromGrade(item.conditionGrade) ?? item.condition;

    if (fillMode) {
      handOffScan({
        title: item.title,
        brand: item.brand,
        model: item.model,
        category: item.category,
        color: item.color,
        condition,
        barcode: item.barcode,
        estimatedSalePrice: listPrice,
        photoUri,
        overwrite: params.regenerate === '1',
      });
      router.back();
      return;
    }

    router.push({
      pathname: '/inventory/edit',
      params: definedOnly({
        title: item.title,
        brand: item.brand,
        model: item.model,
        category: item.category,
        color: item.color,
        condition,
        barcode: item.barcode,
        estimatedSalePrice: listPrice === null ? null : String(listPrice),
        photoUri,
        productIdentityId: item.productIdentityId,
        sourcingTripId: params.sourcingTripId,
        sourcingTripStopId: params.sourcingTripStopId,
      }),
    });
  }

  async function addComp(comp: FoundComp): Promise<string> {
    if (!item || state.phase === 'ready') throw new Error('Identify the item before adding comps.');
    const productIdentityId = item.productIdentityId ?? (await rememberProduct(item)).id;
    const outcome = await saveComp({
      productIdentityId,
      inventoryItemId: null,
      observationType: comp.kind === 'Sold' ? 'ManualSold' : 'ManualActive',
      source: comp.source,
      sourceUrl: comp.url,
      title: item.title,
      price: comp.price,
      shippingPrice: comp.shipping,
      condition: conditionFromGrade(item.conditionGrade) ?? item.condition,
      observedAt: new Date().toISOString(),
      soldAt: comp.soldAt,
      notes: null,
    });
    if (outcome.status === 'rejected') throw new Error(outcome.message);
    if (outcome.status === 'saved') reprice({ ...item, productIdentityId }, state.input);
    else linkProduct(productIdentityId);
    return describeCompOutcome(outcome);
  }

  return (
    <AppraisalView
      state={state}
      primaryAction={
        state.input.kind === 'item'
          ? expectedPriceAction(state.input.inventoryItemId, listPrice)
          : {
              label: fillMode ? 'Use these details' : 'Add to inventory',
              icon: fillMode ? 'checkmark' : 'add-circle-outline',
              onPress: applyResult,
            }
      }
      secondaryActions={secondaryActions(state, item, listPrice, rememberedBarcode, (barcode, productIdentityId) => {
        setRememberedBarcode(barcode);
        linkProduct(productIdentityId);
      })}
      fallbackAction={
        fillMode || state.input.kind === 'item'
          ? undefined
          : {
              label: 'Add it without a match',
              icon: 'add-outline',
              onPress: () =>
                router.push({
                  pathname: '/inventory/edit',
                  params: definedOnly({
                    barcode: state.input.kind === 'barcode' ? state.input.barcode : null,
                    title: state.input.kind === 'details' ? state.input.details : null,
                    photoUri,
                    sourcingTripId: params.sourcingTripId,
                    sourcingTripStopId: params.sourcingTripStopId,
                  }),
                }),
            }
      }
      onCorrect={(corrected) => reprice(corrected, state.input)}
      onAddComp={addComp}
      askingPrice={askingPrice}
      onAskingPriceChange={setAskingPrice}
      onRemedy={handleRemedy}
      onNewScan={() => startOver()}
      onClose={() => router.back()}
    />
  );
}

function secondaryActions(
  state: AppraisalState,
  item: Identification | null,
  listPrice: number | null,
  rememberedBarcode: string | null,
  onRemembered: (barcode: string, productIdentityId: string) => void,
): ResultAction[] {
  if (!item) return [];
  const actions: ResultAction[] = [];

  if (listPrice !== null) {
    actions.push({
      label: 'Check the deal',
      icon: 'calculator-outline',
      onPress: () =>
        router.push({
          pathname: '/deal-analyzer',
          params: definedOnly({
            expectedSalePrice: String(listPrice),
            inventoryItemId: state.phase !== 'ready' && state.input.kind === 'item' ? state.input.inventoryItemId : null,
            productIdentityId: item.productIdentityId,
            query: state.phase === 'appraised' ? state.appraisal.searchText : item.title,
          }),
        }),
    });
  }

  const barcode = item.barcode;
  const remembered = barcode !== null && rememberedBarcode === barcode;
  if (barcode && item.source !== 'Catalog' && (!item.productIdentityId || remembered)) {
    actions.push({
      label: remembered ? 'Barcode remembered' : 'Remember this barcode',
      icon: remembered ? 'checkmark-circle-outline' : 'bookmark-outline',
      disabled: remembered,
      onPress: () =>
        void rememberProduct(item)
          .then((product) => onRemembered(barcode, product.id))
          .catch((error: unknown) =>
            Alert.alert("Couldn't remember the barcode", error instanceof Error ? error.message : 'Try again.'),
          ),
    });
  }

  return actions;
}

function expectedPriceAction(inventoryItemId: string, listPrice: number | null): ResultAction {
  return {
    label: listPrice === null ? 'No price to use' : `Use ${formatMoney(listPrice)} as expected price`,
    icon: 'checkmark',
    disabled: listPrice === null,
    onPress: () => {
      if (listPrice === null) return;
      void api
        .setExpectedPrice(inventoryItemId, listPrice)
        .then(() => router.back())
        .catch((error: unknown) =>
          Alert.alert("Couldn't save the price", error instanceof Error ? error.message : 'Try again.'),
        );
    },
  };
}

// An item in stock is already identified; its own record is the identification.
function identificationOf(item: InventoryItem): Identification {
  return {
    title: item.title,
    brand: item.brand ?? null,
    model: item.model ?? null,
    category: item.category ?? null,
    color: item.colors?.[0] ?? null,
    condition: item.condition ?? null,
    conditionGrade: null,
    barcode: item.barcode ?? null,
    identifiers: item.model ? [item.model] : [],
    certainty: 'High',
    source: 'Catalog',
    productIdentityId: item.productIdentityId ?? null,
    imageUrl: item.heroPhotoThumbnailUrl ?? null,
    missingInformation: [],
    estimate: null,
  };
}

// Saves what this item is so its barcode is recognised next time and comps can be filed against it.
function rememberProduct(item: Identification) {
  const barcode = item.barcode ?? '';
  return api.createProductIdentity({
    title: item.title,
    brand: item.brand,
    model: item.model,
    category: item.category,
    upc: barcode.length === 8 || barcode.length === 12 ? barcode : null,
    ean: barcode.length === 13 || barcode.length === 14 ? barcode : null,
    searchQuery: item.title,
  });
}

function definedOnly(values: Record<string, string | null | undefined>): Record<string, string> {
  return Object.fromEntries(Object.entries(values).filter((entry): entry is [string, string] => Boolean(entry[1])));
}
