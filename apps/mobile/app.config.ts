import { ExpoConfig, ConfigContext } from 'expo/config';

const IS_PROD = process.env.EAS_BUILD_PROFILE === 'production';
const IS_PREVIEW = process.env.EAS_BUILD_PROFILE === 'preview';

export default ({ config }: ConfigContext): ExpoConfig => {
  const apiBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL ?? 'http://localhost:5080';
  const apiPort = apiBaseUrl.match(/:(\d+)\/?$/)?.[1] ?? '5080';

  return {
    ...config,
    name: IS_PROD ? 'ScoutHaul' : IS_PREVIEW ? 'ScoutHaul (Preview)' : 'ScoutHaul (Dev)',
    slug: 'scouthaul',
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
    plugins: ['expo-router', 'expo-sqlite', 'expo-camera'],
    extra: {
      apiBaseUrl,
      apiPort: Number(apiPort),
      organizationId: process.env.EXPO_PUBLIC_SCOUTHAUL_ORG_ID ?? '',
      userId: process.env.EXPO_PUBLIC_SCOUTHAUL_USER_ID ?? '',
      eas: {
        projectId: process.env.EAS_PROJECT_ID ?? '',
      },
    },
    updates: {
      url: process.env.EAS_PROJECT_ID
        ? `https://u.expo.dev/${process.env.EAS_PROJECT_ID}`
        : undefined,
    },
    runtimeVersion: {
      policy: 'appVersion',
    },
  };
};
