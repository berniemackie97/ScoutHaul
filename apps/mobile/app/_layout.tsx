import { Ionicons } from '@expo/vector-icons';
import { Stack, usePathname, useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { ActivityIndicator, View } from 'react-native';
import { getPendingSyncCount, getWorkspaceProfile, initializeLocalDatabase, processSyncQueue } from '../src/db/localStore';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { ThemeProvider, useColors } from '../src/context/ThemeContext';
import { initializeAuthSession, authStore } from '../src/services/auth';
import { bootstrapSignedInWorkspace } from '../src/services/sessionBootstrap';

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
      <Stack.Screen name="onboarding" options={{ headerShown: false, presentation: 'fullScreenModal' }} />
      <Stack.Screen name="account" options={{ headerShown: false, presentation: 'fullScreenModal' }} />
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
      <Stack.Screen name="sales" options={{ title: 'Sales & Shipping' }} />
      <Stack.Screen name="record-sale" options={{ title: 'Record Sale' }} />
      <Stack.Screen name="tasks" options={{ title: 'Tasks' }} />
      <Stack.Screen name="settings" options={{ title: 'Settings' }} />
      <Stack.Screen name="workspace-admin" options={{ title: 'Workspace Admin' }} />
      <Stack.Screen name="search" options={{ title: 'Search', presentation: 'modal' }} />
      <Stack.Screen name="active-route" options={{ title: 'Active Route', presentation: 'fullScreenModal' }} />
    </Stack>
  );
}

function StartupGate() {
  const c = useColors();
  const router = useRouter();
  const pathname = usePathname();
  const [isReady, setIsReady] = useState(false);

  useEffect(() => {
    let isMounted = true;
    void (async () => {
      await initializeLocalDatabase();
      await initializeAuthSession();
      let profile = await getWorkspaceProfile();
      if (authStore.isAuthenticated) {
        try {
          await bootstrapSignedInWorkspace();
          if (await getPendingSyncCount() > 0) {
            await processSyncQueue();
          }
          profile = await getWorkspaceProfile();
        } catch (error) {
          console.warn('Unable to bootstrap signed-in workspace:', error);
        }
      }
      if (!isMounted) return;

      setIsReady(true);

      // New users or onboarding not finished → onboarding flow
      if (!profile || !profile.onboardingCompleted) {
        if (pathname !== '/onboarding' && pathname !== '/account') {
          router.replace('/onboarding');
        }
        return;
      }

      // Not authenticated and didn't explicitly choose local/offline mode → sign in
      if (!authStore.isAuthenticated && profile.accountMode !== 'local' && pathname !== '/account') {
        router.replace({ pathname: '/account', params: { mode: 'signin' } });
      }
    })();

    return () => {
      isMounted = false;
    };
  }, []);

  if (!isReady) {
    return (
      <View style={{ flex: 1, backgroundColor: '#1e1b4b', alignItems: 'center', justifyContent: 'center', gap: spacing.md }}>
        <Ionicons name="compass" size={56} color="#f59e0b" />
        <Text variant="bodyBold" color="#fbbf24">ScoutHaul</Text>
        <ActivityIndicator size="small" color="#f59e0b" />
      </View>
    );
  }

  return <RootStack />;
}

export default function RootLayout() {
  return (
    <ThemeProvider>
      <StartupGate />
    </ThemeProvider>
  );
}
