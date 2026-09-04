import { ApiError } from '../api/apiError';
import { initialResourceState, isEmpty, resourceReducer, type ResourceState } from './resourceState';

type Item = { id: string };
const reduce = (state: ResourceState<Item[]>, action: Parameters<typeof resourceReducer<Item[]>>[1]) =>
  resourceReducer(state, action);

const loaded = (data: Item[]): ResourceState<Item[]> => ({
  data, error: null, isLoading: false, isRefreshing: false, failure: null,
});

describe('the first load', () => {
  it('starts with nothing and says it is loading', () => {
    const state = initialResourceState<Item[]>();
    expect(state).toEqual({ data: null, error: null, isLoading: true, isRefreshing: false, failure: null });
  });

  it('clears a previous error, so a retry does not show the old failure under the spinner', () => {
    const failed = { ...initialResourceState<Item[]>(), isLoading: false, error: 'Offline.', failure: 'offline' as const };
    expect(reduce(failed, { type: 'load' })).toMatchObject({ isLoading: true, error: null, failure: null });
  });
});

describe('a refresh', () => {
  it('keeps the data on screen while it reloads', () => {
    // Blanking here is what made every return to a screen flash empty.
    const next = reduce(loaded([{ id: 'a' }]), { type: 'refresh' });

    expect(next.data).toEqual([{ id: 'a' }]);
    expect(next.isRefreshing).toBe(true);
    expect(next.isLoading).toBe(false);
  });
});

describe('a success', () => {
  it('replaces the data and clears everything else', () => {
    const stale = { ...loaded([{ id: 'old' }]), error: 'Offline.', failure: 'offline' as const, isRefreshing: true };
    expect(reduce(stale, { type: 'resolved', data: [{ id: 'new' }] })).toEqual({
      data: [{ id: 'new' }], error: null, isLoading: false, isRefreshing: false, failure: null,
    });
  });
});

describe('a failure', () => {
  it('keeps what the operator was looking at', () => {
    // A failed refresh should say it failed, not throw away the data.
    const next = reduce(loaded([{ id: 'a' }]), { type: 'rejected', error: new Error('Network down') });

    expect(next.data).toEqual([{ id: 'a' }]);
    expect(next.error).toBe('Network down');
    expect(next.isLoading).toBe(false);
    expect(next.isRefreshing).toBe(false);
  });

  it('records why, so a screen can tell offline from a rejection', () => {
    const offline = reduce(initialResourceState<Item[]>(), {
      type: 'rejected',
      error: new ApiError({ reason: 'offline', message: "You're offline." }),
    });
    expect(offline.failure).toBe('offline');

    const rejected = reduce(initialResourceState<Item[]>(), {
      type: 'rejected',
      error: new ApiError({ reason: 'http', status: 404, message: 'No such customer.' }),
    });
    expect(rejected.failure).toBe('http');
  });

  it('has something to say about a thrown value that is not an Error', () => {
    expect(reduce(initialResourceState<Item[]>(), { type: 'rejected', error: 'boom' }).error)
      .toBe('Something went wrong.');
  });
});

describe('isEmpty', () => {
  it('is true only when there is nothing to show and nothing wrong', () => {
    expect(isEmpty(loaded([]))).toBe(true);
    expect(isEmpty(loaded([{ id: 'a' }]))).toBe(false);
  });

  it('is false while loading, because nothing is known yet', () => {
    expect(isEmpty(initialResourceState<Item[]>())).toBe(false);
  });

  it('is false when the list is empty because the request failed', () => {
    // An empty state and a failure say different things, and showing "nothing here yet" for an
    // outage tells the operator their data is gone.
    const failed = { ...loaded([]), error: 'Offline.' };
    expect(isEmpty(failed)).toBe(false);
  });
});
