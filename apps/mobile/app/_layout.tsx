import { Stack } from 'expo-router';
import { useEffect } from 'react';
import { initializeLocalDatabase } from '../src/db/localStore';
import { ThemeProvider, useColors } from '../src/context/ThemeContext';

function RootStack() {
  const c = useColors();
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: c.surface },
        headerTitleStyle: { fontWeight: '700', fontSize: 17 },
        headerShadowVisible: false,
        headerTintColor: c.text,
        headerBackTitle: '',
        contentStyle: { backgroundColor: c.background },
      }}
    >
      <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
      <Stack.Screen name="scanner" options={{ title: 'Scan Barcode', presentation: 'fullScreenModal' }} />
      <Stack.Screen name="manual-search" options={{ title: 'Search' }} />
      <Stack.Screen name="marketplace-links" options={{ title: 'Marketplace Search' }} />
      <Stack.Screen name="comp-capture" options={{ title: 'Add Comp' }} />
      <Stack.Screen name="deal-analyzer" options={{ title: 'Deal Analyzer' }} />
      <Stack.Screen name="inventory/[id]" options={{ title: 'Item Detail' }} />
      <Stack.Screen name="inventory/edit" options={{ title: 'Add Item' }} />
      <Stack.Screen name="trips/[id]" options={{ title: 'Trip Detail' }} />
      <Stack.Screen name="budget" options={{ title: 'Budget' }} />
      <Stack.Screen name="listing-queue" options={{ title: 'Listing Queue' }} />
      <Stack.Screen name="tasks" options={{ title: 'Tasks' }} />
      <Stack.Screen name="settings" options={{ title: 'Settings' }} />
      <Stack.Screen name="search" options={{ title: 'Search', presentation: 'modal' }} />
      <Stack.Screen name="active-route" options={{ title: 'Active Route', presentation: 'fullScreenModal' }} />
    </Stack>
  );
}

export default function RootLayout() {
  useEffect(() => {
    void initializeLocalDatabase();
  }, []);

  return (
    <ThemeProvider>
      <RootStack />
    </ThemeProvider>
  );
}
