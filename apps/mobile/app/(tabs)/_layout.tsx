import { Tabs } from 'expo-router';
import { useCallback, useState } from 'react';
import { useFocusEffect } from 'expo-router';
import { CustomTabBar } from '../../src/components/CustomTabBar';
import { getPendingSyncCount } from '../../src/db/localStore';

export default function TabLayout() {
  const [pendingSync, setPendingSync] = useState(0);

  useFocusEffect(
    useCallback(() => {
      void getPendingSyncCount().then(setPendingSync).catch(() => {});
    }, []),
  );

  return (
    <Tabs
      screenOptions={{ headerShown: false }}
      tabBar={(props) => (
        <CustomTabBar
          {...props}
          badgeCounts={{ pendingSync }}
        />
      )}
    >
      <Tabs.Screen name="index" options={{ title: 'Home' }} />
      <Tabs.Screen name="inventory" options={{ title: 'Inventory' }} />
      <Tabs.Screen name="orders" options={{ title: 'Orders' }} />
      <Tabs.Screen name="scout" options={{ title: 'Scout' }} />
      <Tabs.Screen name="more" options={{ title: 'More' }} />
    </Tabs>
  );
}
