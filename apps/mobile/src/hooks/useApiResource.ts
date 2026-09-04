import { useCallback, useEffect, useReducer, useRef } from 'react';
import { useFocusEffect } from 'expo-router';
import {
  initialResourceState,
  resourceReducer,
  type ResourceState,
} from './resourceState';

export type ApiResource<T> = ResourceState<T> & {
  /** Reload with the data still on screen. What pull-to-refresh calls. */
  refresh: () => void;
  /** Reload from nothing — for a retry after a failure, where a spinner is the honest thing to show. */
  reload: () => void;
};

/**
 * Fetches one thing from the API and keeps its state honest.
 *
 * Every screen used to write this by hand, and each copy had the same three holes: a response that
 * arrived after the screen closed still called setState; a slow response could land after a newer
 * one and overwrite it; and there was nothing distinguishing a first load from a reload, so coming
 * back to a screen either flashed a spinner or silently showed stale data.
 *
 * `fetcher` must be stable — wrap it in useCallback, keyed on whatever it reads.
 */
export function useApiResource<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  options: { refetchOnFocus?: boolean } = {},
): ApiResource<T> {
  const { refetchOnFocus = true } = options;
  const [state, dispatch] = useReducer(resourceReducer<T>, undefined, initialResourceState<T>);

  // Every request gets a number. Only the newest one is allowed to write, so a slow answer that
  // arrives after a newer one is dropped instead of overwriting it.
  const latestRequest = useRef(0);
  const inFlight = useRef<AbortController | null>(null);
  const isMounted = useRef(true);

  useEffect(() => {
    isMounted.current = true;
    return () => {
      isMounted.current = false;
      inFlight.current?.abort();
    };
  }, []);

  const run = useCallback(
    async (mode: 'load' | 'refresh') => {
      inFlight.current?.abort();
      const controller = new AbortController();
      inFlight.current = controller;

      const request = ++latestRequest.current;
      dispatch({ type: mode });

      try {
        const data = await fetcher(controller.signal);
        if (isMounted.current && request === latestRequest.current) {
          dispatch({ type: 'resolved', data });
        }
      } catch (error) {
        // A request we cancelled ourselves is not a failure to report.
        if (controller.signal.aborted) return;
        if (isMounted.current && request === latestRequest.current) {
          dispatch({ type: 'rejected', error });
        }
      }
    },
    [fetcher],
  );

  const refresh = useCallback(() => void run('refresh'), [run]);
  const reload = useCallback(() => void run('load'), [run]);

  // The first visit loads; every later visit refreshes, so returning to a screen shows what was
  // there while the new answer arrives rather than blanking it.
  const hasLoaded = useRef(false);
  useFocusEffect(
    useCallback(() => {
      if (!hasLoaded.current) {
        hasLoaded.current = true;
        void run('load');
        return;
      }
      if (refetchOnFocus) void run('refresh');
    }, [run, refetchOnFocus]),
  );

  return { ...state, refresh, reload };
}
