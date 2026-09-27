import { useCallback, useEffect, useReducer, useState } from 'react';
import type { CallOptions } from '../api/client';
import { initialSearchState, searchReducer, type SearchState } from './searchState';

export const SEARCH_DEBOUNCE_MS = 250;

/**
 * Searches on the server as the query changes: a beat after typing pauses, cancelling the request
 * before it. `search` must be stable — a module function, or wrapped in useCallback.
 */
export function useServerSearch<T>(
  query: string,
  enabled: boolean,
  search: (query: string, options: CallOptions) => Promise<T[]>,
): SearchState<T> & { retry: () => void } {
  const [state, dispatch] = useReducer(searchReducer<T>, undefined, initialSearchState<T>);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!enabled) return;
    const typed = query.trim();
    dispatch({ type: 'search', query: typed });
    const controller = new AbortController();
    const timer = setTimeout(() => {
      search(typed, { signal: controller.signal })
        .then((results) => dispatch({ type: 'resolved', query: typed, results }))
        .catch((error: unknown) => {
          if (!controller.signal.aborted) dispatch({ type: 'rejected', query: typed, error });
        });
    }, SEARCH_DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [attempt, enabled, query, search]);

  const retry = useCallback(() => setAttempt((current) => current + 1), []);
  return { ...state, retry };
}
