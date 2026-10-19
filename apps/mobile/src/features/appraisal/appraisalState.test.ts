import { appraisalReducer, initialAppraisalState, photoOf, type AppraisalState } from './appraisalState';
import type { ScanFailure } from './failures';
import type { Appraisal, Identification } from './types';

const item: Identification = {
  title: 'Sony WH-1000XM5',
  brand: 'Sony',
  model: 'WH-1000XM5',
  category: null,
  color: null,
  condition: null,
  conditionGrade: null,
  barcode: null,
  identifiers: [],
  certainty: 'High',
  source: 'Photo',
  productIdentityId: null,
  imageUrl: null,
  missingInformation: [],
  estimate: null,
};

const appraisal = { searchText: 'Sony WH-1000XM5' } as Appraisal;
const failure: ScanFailure = { title: 'Took too long', message: 'Try again.', remedies: ['retry'] };
const photo = { kind: 'photo', photoUri: 'file:///item.jpg' } as const;

describe('appraisalReducer', () => {
  it('walks a scan from identifying through pricing to an appraisal', () => {
    let state = appraisalReducer(initialAppraisalState, { type: 'identify', input: photo });
    state = appraisalReducer(state, { type: 'identified', item });
    expect(state).toEqual({ phase: 'appraising', input: photo, item });

    state = appraisalReducer(state, { type: 'appraised', appraisal });
    expect(state).toEqual({ phase: 'appraised', input: photo, item, appraisal });
  });

  it('ignores an answer for a step the screen has already left', () => {
    const appraising: AppraisalState = { phase: 'appraising', input: photo, item };

    expect(appraisalReducer(appraising, { type: 'identified', item })).toBe(appraising);
    expect(appraisalReducer(initialAppraisalState, { type: 'appraised', appraisal })).toBe(initialAppraisalState);
    expect(appraisalReducer(initialAppraisalState, { type: 'identifyFailed', failure })).toBe(initialAppraisalState);
  });

  it('re-prices a corrected item from an appraisal or a failed pricing, keeping the photo', () => {
    const corrected = { ...item, model: 'WH-1000XM4' };
    const appraised: AppraisalState = { phase: 'appraised', input: photo, item, appraisal };
    const unpriced: AppraisalState = { phase: 'unpriced', input: photo, item, failure };

    expect(appraisalReducer(appraised, { type: 'reprice', item: corrected })).toEqual({ phase: 'appraising', input: photo, item: corrected });
    expect(appraisalReducer(unpriced, { type: 'reprice', item: corrected })).toEqual({ phase: 'appraising', input: photo, item: corrected });
  });

  it('cannot price something that was never identified', () => {
    const unidentified: AppraisalState = { phase: 'unidentified', input: photo, failure };

    expect(appraisalReducer(unidentified, { type: 'reprice', item })).toBe(unidentified);
  });

  it('prices an item in stock straight away, from any step', () => {
    const input = { kind: 'item', inventoryItemId: 'item-1', title: 'Sony WH-1000XM5' } as const;

    expect(appraisalReducer(initialAppraisalState, { type: 'appraiseKnown', input, item })).toEqual({ phase: 'appraising', input, item });
  });

  it('keeps the photo across failures so a retry does not need a new one', () => {
    const state = appraisalReducer({ phase: 'identifying', input: photo }, { type: 'identifyFailed', failure });

    expect(photoOf(state)).toBe('file:///item.jpg');
    expect(photoOf(initialAppraisalState)).toBeNull();
  });
});
