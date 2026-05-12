import * as Network from 'expo-network';
import { useEffect, useState } from 'react';

export type ConnectivityState = {
  isOnline: boolean;
  label: 'Online' | 'Offline';
};

export function useConnectivity(): ConnectivityState {
  const [isOnline, setIsOnline] = useState(true);

  useEffect(() => {
    let isMounted = true;

    async function check() {
      const state = await Network.getNetworkStateAsync();
      const reachable = state.isInternetReachable ?? state.isConnected ?? false;
      if (isMounted) {
        setIsOnline(Boolean(reachable));
      }
    }

    void check();
    const intervalId = setInterval(check, 10000);

    return () => {
      isMounted = false;
      clearInterval(intervalId);
    };
  }, []);

  return { isOnline, label: isOnline ? 'Online' : 'Offline' };
}
