import type { ScanFailure } from './failures';
import type { Appraisal, Identification } from './types';

export type ScanInput =
  | { kind: 'barcode'; barcode: string }
  | { kind: 'photo'; photoUri: string; details?: string }
  | { kind: 'details'; details: string };

export type AppraisalState =
  | { phase: 'ready' }
  | { phase: 'identifying'; input: ScanInput }
  | { phase: 'unidentified'; input: ScanInput; failure: ScanFailure }
  | { phase: 'appraising'; input: ScanInput; item: Identification }
  | { phase: 'appraised'; input: ScanInput; item: Identification; appraisal: Appraisal }
  | { phase: 'unpriced'; input: ScanInput; item: Identification; failure: ScanFailure };

export type AppraisalAction =
  | { type: 'identify'; input: ScanInput }
  | { type: 'identified'; item: Identification }
  | { type: 'identifyFailed'; failure: ScanFailure }
  | { type: 'reprice'; item: Identification }
  | { type: 'appraised'; appraisal: Appraisal }
  | { type: 'appraiseFailed'; failure: ScanFailure }
  | { type: 'linkProduct'; productIdentityId: string }
  | { type: 'reset' };

export const initialAppraisalState: AppraisalState = { phase: 'ready' };

/** Answers that arrive for a step the screen has already left are ignored. */
export function appraisalReducer(state: AppraisalState, action: AppraisalAction): AppraisalState {
  switch (action.type) {
    case 'identify':
      return { phase: 'identifying', input: action.input };

    case 'identified':
      return state.phase === 'identifying'
        ? { phase: 'appraising', input: state.input, item: action.item }
        : state;

    case 'identifyFailed':
      return state.phase === 'identifying'
        ? { phase: 'unidentified', input: state.input, failure: action.failure }
        : state;

    case 'reprice':
      return state.phase === 'ready' || state.phase === 'identifying' || state.phase === 'unidentified'
        ? state
        : { phase: 'appraising', input: state.input, item: action.item };

    case 'appraised':
      return state.phase === 'appraising'
        ? { phase: 'appraised', input: state.input, item: state.item, appraisal: action.appraisal }
        : state;

    case 'appraiseFailed':
      return state.phase === 'appraising'
        ? { phase: 'unpriced', input: state.input, item: state.item, failure: action.failure }
        : state;

    case 'linkProduct':
      return state.phase === 'appraising' || state.phase === 'appraised' || state.phase === 'unpriced'
        ? { ...state, item: { ...state.item, productIdentityId: action.productIdentityId } }
        : state;

    case 'reset':
      return initialAppraisalState;
  }
}

export function photoOf(state: AppraisalState): string | null {
  return state.phase !== 'ready' && state.input.kind === 'photo' ? state.input.photoUri : null;
}
