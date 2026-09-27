import { ApiError } from '../api/apiError';

/**
 * A search asked of the server as someone types. Only the answer to the latest query may land, so a
 * slow reply to an earlier query can never replace the results for what is in the box now.
 */
export type SearchState<T> = {
  query: string;
  results: T[];
  status: 'idle' | 'searching' | 'ready' | 'failed';
  error: string | null;
  failure: ApiError['reason'] | null;
};

export type SearchAction<T> =
  | { type: 'search'; query: string }
  | { type: 'resolved'; query: string; results: T[] }
  | { type: 'rejected'; query: string; error: unknown };

export function initialSearchState<T>(): SearchState<T> {
  return { query: '', results: [], status: 'idle', error: null, failure: null };
}

export function searchReducer<T>(state: SearchState<T>, action: SearchAction<T>): SearchState<T> {
  switch (action.type) {
    case 'search':
      // The previous matches stay up while the new ones come, so the list does not flash empty per keystroke.
      return { ...state, query: action.query, status: 'searching', error: null, failure: null };

    case 'resolved':
      if (action.query !== state.query) return state;
      return { ...state, results: action.results, status: 'ready', error: null, failure: null };

    case 'rejected': {
      if (action.query !== state.query) return state;
      const failure = action.error instanceof ApiError ? action.error.reason : null;
      const error = action.error instanceof Error && action.error.message ? action.error.message : 'The search could not be run.';
      return { ...state, results: [], status: 'failed', error, failure };
    }
  }
}

/** What to say when a finished search found nothing, or null while there is something else to show. */
export function emptyMessage<T>(state: SearchState<T>, noun: string): string | null {
  if (state.status !== 'ready' || state.results.length > 0) return null;
  const typed = state.query.trim();
  return typed ? `No ${noun} matches “${typed}”.` : `No ${noun} to choose from yet.`;
}
