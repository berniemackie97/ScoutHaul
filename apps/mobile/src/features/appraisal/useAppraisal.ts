import { useCallback, useEffect, useReducer, useRef } from 'react';
import { haptics } from '../../utils/haptics';
import { appraise, identify, toAppraiseRequest } from './api';
import { appraisalReducer, initialAppraisalState, type ScanInput } from './appraisalState';
import { describeFailure } from './failures';
import { preparePhoto } from './preparePhoto';
import type { Identification } from './types';

/** Identify, then price. One request at a time: a new scan or leaving the screen cancels the last. */
export function useAppraisal() {
  const [state, dispatch] = useReducer(appraisalReducer, initialAppraisalState);
  const inFlight = useRef<AbortController | null>(null);

  useEffect(() => () => inFlight.current?.abort(), []);

  const start = useCallback(() => {
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    return controller;
  }, []);

  const price = useCallback(async (item: Identification, controller: AbortController) => {
    try {
      const appraisal = await appraise(toAppraiseRequest(item), controller.signal);
      if (!controller.signal.aborted) dispatch({ type: 'appraised', appraisal });
    } catch (error) {
      if (controller.signal.aborted) return;
      haptics.warning();
      dispatch({ type: 'appraiseFailed', failure: describeFailure(error) });
    }
  }, []);

  const scan = useCallback(
    async (input: ScanInput) => {
      const controller = start();
      dispatch({ type: 'identify', input });
      try {
        const item = await identify(
          {
            photoUri: input.kind === 'photo' ? await preparePhoto(input.photoUri) : undefined,
            barcode: input.kind === 'barcode' ? input.barcode : undefined,
            details: input.kind === 'barcode' ? undefined : input.details,
          },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        haptics.success();
        dispatch({ type: 'identified', item });
        await price(item, controller);
      } catch (error) {
        if (controller.signal.aborted) return;
        haptics.warning();
        dispatch({ type: 'identifyFailed', failure: describeFailure(error) });
      }
    },
    [price, start],
  );

  const reprice = useCallback(
    (item: Identification) => {
      const controller = start();
      dispatch({ type: 'reprice', item });
      void price(item, controller);
    },
    [price, start],
  );

  const linkProduct = useCallback((productIdentityId: string) => dispatch({ type: 'linkProduct', productIdentityId }), []);

  const reset = useCallback(() => {
    inFlight.current?.abort();
    dispatch({ type: 'reset' });
  }, []);

  return { state, scan, reprice, linkProduct, reset };
}
