import { ExpoConfig, ConfigContext } from 'expo/config';

const IS_PROD = process.env.EAS_BUILD_PROFILE === 'production';
const IS_PREVIEW = process.env.EAS_BUILD_PROFILE === 'preview';

export default ({ config }: ConfigContext): ExpoConfig => {
  const apiBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL ?? 'http://localhost:5080';
  const apiPort = apiBaseUrl.match(/:(\d+)\/?$/)?.[1] ?? '5080';

  return {
    ...config,
    name: IS_PROD ? 'HaulScout' : IS_PREVIEW ? 'HaulScout (Preview)' : 'HaulScout (Dev)',
    slug: 'haulscout',
    version: '1.0.0',
    orientation: 'portrait',
    scheme: 'scouthaul',
    icon: './assets/icon.png',
    userInterfaceStyle: 'automatic',
    newArchEnabled: true,
    splash: {
      image: './assets/splash-icon.png',
      resizeMode: 'contain',
      backgroundColor: '#ffffff',
    },
    ios: {
      supportsTablet: true,
      bundleIdentifier: IS_PROD
        ? 'com.scouthaul.app'
        : IS_PREVIEW
          ? 'com.scouthaul.app.preview'
          : 'com.scouthaul.app.dev',
    },
    android: {
      adaptiveIcon: {
        foregroundImage: './assets/adaptive-icon.png',
        backgroundColor: '#ffffff',
      },
      edgeToEdgeEnabled: true,
      package: IS_PROD
        ? 'com.scouthaul.app'
        : IS_PREVIEW
          ? 'com.scouthaul.app.preview'
          : 'com.scouthaul.app.dev',
    },
    web: {
      favicon: './assets/favicon.png',
    },
    plugins: ['expo-router', 'expo-sqlite', 'expo-camera', 'expo-web-browser', 'expo-secure-store', 'expo-font'],
    extra: {
      apiBaseUrl,
      apiPort: Number(apiPort),
      organizationId: process.env.EXPO_PUBLIC_SCOUTHAUL_ORG_ID ?? '',
      userId: process.env.EXPO_PUBLIC_SCOUTHAUL_USER_ID ?? '',
      supabaseUrl: process.env.EXPO_PUBLIC_SUPABASE_URL ?? '',
      supabaseAnonKey: process.env.EXPO_PUBLIC_SUPABASE_ANON_KEY ?? '',
      eas: {
        projectId: '8ca8c9a6-4988-4a8c-9656-476be071871e',
      },
    },
    updates: {
      url: 'https://u.expo.dev/8ca8c9a6-4988-4a8c-9656-476be071871e',
    },
    runtimeVersion: {
      policy: 'appVersion',
    },
  };
};
