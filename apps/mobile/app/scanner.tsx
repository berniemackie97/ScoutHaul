import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { Alert } from 'react-native';
import { api } from '../src/api';
import { AppraisalView, type ResultAction } from '../src/features/appraisal/AppraisalView';
import type { AppraisalState } from '../src/features/appraisal/appraisalState';
import { DetailsEntry } from '../src/features/appraisal/DetailsEntry';
import type { FailureRemedy } from '../src/features/appraisal/failures';
import { handOffScan } from '../src/features/appraisal/handoff';
import { conditionFromGrade } from '../src/features/appraisal/labels';
import { ScoutCamera, type CaptureMode } from '../src/features/appraisal/ScoutCamera';
import type { Identification } from '../src/features/appraisal/types';
import { useAppraisal } from '../src/features/appraisal/useAppraisal';

type Entry = { kind: 'camera' } | { kind: 'type'; photoUri: string | null };

type ScannerParams = {
  mode?: 'barcode' | 'photo' | 'details';
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
  const { state, scan, reprice, reset } = useAppraisal();
  const [mode, setMode] = useState<CaptureMode>(params.mode === 'photo' ? 'photo' : 'barcode');
  const [entry, setEntry] = useState<Entry>(params.mode === 'details' ? { kind: 'type', photoUri: null } : { kind: 'camera' });
  const [rememberedBarcode, setRememberedBarcode] = useState<string | null>(null);
  const startedFromParams = useRef(false);
  const fillMode = params.fill === '1';
  const context = params.context?.trim() || undefined;

  useEffect(() => {
    const details = params.details?.trim();
    if (startedFromParams.current || !details) return;
    startedFromParams.current = true;
    void scan({ kind: 'details', details });
  }, [params.details, scan]);

  function startOver(next: Entry = { kind: 'camera' }) {
    reset();
    setRememberedBarcode(null);
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
        if (state.phase === 'unpriced') reprice(state.item);
        else void scan(state.input);
    }
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

  return (
    <AppraisalView
      state={state}
      primaryAction={{
        label: fillMode ? 'Use these details' : 'Add to inventory',
        icon: fillMode ? 'checkmark' : 'add-circle-outline',
        onPress: applyResult,
      }}
      secondaryActions={secondaryActions(state, item, listPrice, rememberedBarcode, setRememberedBarcode)}
      fallbackAction={
        fillMode
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
      onCorrect={reprice}
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
  onRemembered: (barcode: string) => void,
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
            productIdentityId: item.productIdentityId,
            query: state.phase === 'appraised' ? state.appraisal.searchText : item.title,
          }),
        }),
    });
  }

  const barcode = item.barcode;
  if (barcode && !item.productIdentityId && item.source !== 'Catalog') {
    const remembered = rememberedBarcode === barcode;
    actions.push({
      label: remembered ? 'Barcode remembered' : 'Remember this barcode',
      icon: remembered ? 'checkmark-circle-outline' : 'bookmark-outline',
      disabled: remembered,
      onPress: () =>
        void api
          .createProductIdentity({
            title: item.title,
            brand: item.brand,
            model: item.model,
            category: item.category,
            upc: barcode.length === 8 || barcode.length === 12 ? barcode : null,
            ean: barcode.length === 13 || barcode.length === 14 ? barcode : null,
            searchQuery: item.title,
          })
          .then(() => onRemembered(barcode))
          .catch((error: unknown) =>
            Alert.alert("Couldn't remember the barcode", error instanceof Error ? error.message : 'Try again.'),
          ),
    });
  }

  return actions;
}

function definedOnly(values: Record<string, string | null | undefined>): Record<string, string> {
  return Object.fromEntries(Object.entries(values).filter((entry): entry is [string, string] => Boolean(entry[1])));
}
