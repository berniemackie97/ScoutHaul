import { useEffect, useRef } from 'react';
import * as Network from 'expo-network';
import { getPendingSyncCount, processSyncQueue } from '../db/localStore';

/**
 * Drains the offline queue when the connection comes back.
 *
 * Until now the queue was only processed at cold boot — and even that was skipped whenever the
 * workspace bootstrap threw — or by tapping a button buried in Settings. So the normal case, walking
 * out of a basement thrift store and back into signal, left everything sitting in the queue until the
 * app was killed and relaunched.
 *
 * Only the transition into connectivity triggers a run, so an already-online session does not
 * re-sync on every listener callback.
 */
export function useSyncOnReconnect(): void {
  const wasOnline = useRef<boolean | null>(null);
  const isDraining = useRef(false);

  useEffect(() => {
    let isMounted = true;

    async function drain() {
      if (isDraining.current) return;
      isDraining.current = true;
      try {
        if ((await getPendingSyncCount()) > 0) {
          await processSyncQueue();
        }
      } catch (error) {
        // A failed drain is not worth interrupting the user for; the queue keeps the work and the
        // next reconnect tries again.
        console.warn('Sync on reconnect failed:', error);
      } finally {
        isDraining.current = false;
      }
    }

    function onState(isOnline: boolean) {
      if (!isMounted) return;
      const cameBackOnline = wasOnline.current === false && isOnline;
      wasOnline.current = isOnline;
      if (cameBackOnline) void drain();
    }

    void Network.getNetworkStateAsync()
      .then((state) => onState(Boolean(state.isInternetReachable ?? state.isConnected ?? false)))
      .catch(() => {});

    const subscription = Network.addNetworkStateListener((state) =>
      onState(Boolean(state.isInternetReachable ?? state.isConnected ?? false)),
    );

    return () => {
      isMounted = false;
      subscription.remove();
    };
  }, []);
}
