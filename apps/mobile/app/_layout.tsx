import { Ionicons } from '@expo/vector-icons';
import { LinearGradient } from 'expo-linear-gradient';
import { Stack, usePathname, useRouter } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Animated, Easing, StyleSheet, View } from 'react-native';
import { getPendingSyncCount, getWorkspaceProfile, initializeLocalDatabase, processSyncQueue } from '../src/db/localStore';
import { ErrorBoundary } from '../src/components/ErrorBoundary';
import { Text } from '../src/components/Text';
import { spacing } from '../src/components/theme';
import { ThemeProvider, useColors } from '../src/context/ThemeContext';
import { initializeAuthSession, authStore } from '../src/services/auth';
import { bootstrapSignedInWorkspace } from '../src/services/sessionBootstrap';
import { useAppFonts } from '../src/hooks/useFonts';

function BrandedSplash() {
  const c = useColors();
  const logoScale = useRef(new Animated.Value(0.8)).current;
  const logoOpacity = useRef(new Animated.Value(0)).current;
  const textOpacity = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    Animated.sequence([
      Animated.parallel([
        Animated.timing(logoScale, {
          toValue: 1,
          duration: 500,
          easing: Easing.out(Easing.back(1.5)),
          useNativeDriver: true,
        }),
        Animated.timing(logoOpacity, { toValue: 1, duration: 400, useNativeDriver: true }),
      ]),
      Animated.timing(textOpacity, { toValue: 1, duration: 300, useNativeDriver: true }),
    ]).start();
  }, []);

  return (
    <LinearGradient
      colors={[c.primary, c.primaryPressed, c.primaryDeep]}
      start={{ x: 0, y: 0 }}
      end={{ x: 1, y: 1 }}
      style={splashStyles.container}
    >
      <View style={splashStyles.decorCircle1} />
      <View style={splashStyles.decorCircle2} />
      <Animated.View style={[splashStyles.logoWrap, { opacity: logoOpacity, transform: [{ scale: logoScale }] }]}>
        <View style={splashStyles.logoMark}>
          <Ionicons name="compass" size={40} color={c.primary} />
        </View>
      </Animated.View>
      <Animated.View style={[splashStyles.textWrap, { opacity: textOpacity }]}>
        <Text variant="title" color="#FFFFFF">
          HaulScout
        </Text>
        <Text variant="caption" color="rgba(255,255,255,0.6)">
          Resale command center
        </Text>
      </Animated.View>
      <Animated.View style={[splashStyles.loader, { opacity: textOpacity }]}>
        <ActivityIndicator size="small" color="rgba(255,255,255,0.5)" />
      </Animated.View>
    </LinearGradient>
  );
}

const splashStyles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.lg,
  },
  decorCircle1: {
    position: 'absolute',
    width: 260,
    height: 260,
    borderRadius: 130,
    backgroundColor: 'rgba(255,255,255,0.04)',
    top: -60,
    right: -80,
  },
  decorCircle2: {
    position: 'absolute',
    width: 160,
    height: 160,
    borderRadius: 80,
    backgroundColor: 'rgba(255,255,255,0.03)',
    bottom: -30,
    left: -40,
  },
  logoWrap: {
    alignItems: 'center',
  },
  logoMark: {
    width: 80,
    height: 80,
    borderRadius: 24,
    backgroundColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 8 },
    shadowOpacity: 0.2,
    shadowRadius: 16,
    elevation: 8,
  },
  textWrap: {
    alignItems: 'center',
    gap: spacing.xs,
  },
  loader: {
    position: 'absolute',
    bottom: 80,
  },
});

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
      <Stack.Screen name="scanner" options={{ headerShown: false, presentation: 'fullScreenModal' }} />
      <Stack.Screen name="manual-search" options={{ title: 'Search' }} />
      <Stack.Screen name="marketplace-links" options={{ title: 'Marketplace Search' }} />
      <Stack.Screen name="comp-capture" options={{ title: 'Add Comp' }} />
      <Stack.Screen name="deal-analyzer" options={{ title: 'Deal Analyzer' }} />
      <Stack.Screen name="inventory/[id]" options={{ title: 'Item Detail' }} />
      <Stack.Screen name="inventory/edit" options={{ title: 'Add Item' }} />
      <Stack.Screen name="trips/[id]" options={{ title: 'Trip Detail' }} />
      <Stack.Screen name="budget" options={{ title: 'Budget' }} />
      <Stack.Screen name="reports" options={{ title: 'Reports' }} />
      <Stack.Screen name="customers/index" options={{ title: 'Customers' }} />
      <Stack.Screen name="customers/[id]" options={{ title: 'Customer Detail' }} />
      <Stack.Screen name="channels" options={{ title: 'Channels' }} />
      <Stack.Screen name="purchases/index" options={{ title: 'Purchases' }} />
      <Stack.Screen name="purchases/[id]" options={{ title: 'Purchase Detail' }} />
      <Stack.Screen name="listing-queue" options={{ title: 'Listing Queue' }} />
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
          if ((await getPendingSyncCount()) > 0) {
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
    return <BrandedSplash />;
  }

  return <RootStack />;
}

export default function RootLayout() {
  const { fontsLoaded, fontError } = useAppFonts();

  if (!fontsLoaded && !fontError) {
    return null;
  }

  return (
    <ErrorBoundary>
      <ThemeProvider>
        <StartupGate />
      </ThemeProvider>
    </ErrorBoundary>
  );
}
