import { useEffect, useState } from 'react';
import { checkBuy } from './api';
import { describeFailure } from './failures';
import type { BuyCheck, BuyCheckRequest } from './types';

// Long enough that typing an asking price sends one request, short enough to feel immediate.
const SETTLE_MS = 400;

type BuyCheckState = { check: BuyCheck | null; error: string | null; loading: boolean };

/** Re-checks the deal whenever its inputs settle; the newest answer always wins. */
export function useBuyCheck(request: BuyCheckRequest | null): BuyCheckState {
  const [state, setState] = useState<BuyCheckState>({ check: null, error: null, loading: false });
  const key = request ? JSON.stringify(request) : null;

  useEffect(() => {
    if (!key) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      setState((current) => ({ ...current, loading: true }));
      checkBuy(JSON.parse(key) as BuyCheckRequest, controller.signal)
        .then((check) => {
          if (!controller.signal.aborted) setState({ check, error: null, loading: false });
        })
        .catch((error: unknown) => {
          if (!controller.signal.aborted) {
            setState((current) => ({ ...current, error: describeFailure(error).message, loading: false }));
          }
        });
    }, SETTLE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [key]);

  return state;
}
