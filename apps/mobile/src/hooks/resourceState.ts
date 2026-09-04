import { ApiError } from '../api/apiError';

/**
 * The state of one thing fetched from the API.
 *
 * Twenty-nine screens each kept this as three or four separate useStates — data, error, isLoading,
 * refreshing — updated from a hand-written try/catch. Kept apart like that they can disagree: a
 * failed refresh would set the error and leave the previous data on screen, so the operator saw
 * stale numbers with no indication they were stale, or an error above data that still looked fine.
 *
 * Holding them together means the impossible combinations cannot be represented.
 */
export type ResourceState<T> = {
  data: T | null;
  error: string | null;
  /** First load, with nothing to show yet. */
  isLoading: boolean;
  /** A reload while data is already on screen — pull to refresh, or coming back to the screen. */
  isRefreshing: boolean;
  /** Why the last attempt failed, for callers that need to tell offline from a real rejection. */
  failure: ApiError['reason'] | null;
};

export type ResourceAction<T> =
  | { type: 'load' }
  | { type: 'refresh' }
  | { type: 'resolved'; data: T }
  | { type: 'rejected'; error: unknown };

export function initialResourceState<T>(): ResourceState<T> {
  return { data: null, error: null, isLoading: true, isRefreshing: false, failure: null };
}

function describe(error: unknown): { message: string; failure: ApiError['reason'] | null } {
  if (error instanceof ApiError) return { message: error.message, failure: error.reason };
  if (error instanceof Error) return { message: error.message, failure: null };
  return { message: 'Something went wrong.', failure: null };
}

export function resourceReducer<T>(state: ResourceState<T>, action: ResourceAction<T>): ResourceState<T> {
  switch (action.type) {
    case 'load':
      // A first load has nothing to show. Clear any previous error so a retry does not display the
      // old failure underneath the spinner.
      return { ...state, isLoading: true, isRefreshing: false, error: null, failure: null };

    case 'refresh':
      // Data stays on screen while it reloads — blanking it would make every return to a screen
      // flash empty. The error clears, because it is about to be answered either way.
      return { ...state, isRefreshing: true, error: null, failure: null };

    case 'resolved':
      return { data: action.data, error: null, isLoading: false, isRefreshing: false, failure: null };

    case 'rejected': {
      const { message, failure } = describe(action.error);
      // The data is deliberately left as it was. A failed refresh should say it failed, not throw
      // away what the operator was looking at.
      return { ...state, error: message, failure, isLoading: false, isRefreshing: false };
    }
  }
}

/** Whether there is nothing to show and nothing coming — the empty state, as opposed to a failure. */
export function isEmpty<T>(state: ResourceState<T>): boolean {
  return !state.isLoading && state.error === null && (state.data === null || isEmptyList(state.data));
}

function isEmptyList(data: unknown): boolean {
  return Array.isArray(data) && data.length === 0;
}
