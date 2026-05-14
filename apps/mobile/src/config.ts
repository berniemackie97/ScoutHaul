import Constants from 'expo-constants';
import { Platform } from 'react-native';

export type Environment = 'development' | 'preview' | 'production';

export interface AppConfig {
  env: Environment;
  apiBaseUrl: string;
  supabaseUrl: string;
  supabaseAnonKey: string;
  isSupabaseConfigured: boolean;
}

function resolveEnvironment(): Environment {
  const profile = Constants.expoConfig?.extra?.eas?.buildProfile;
  if (profile === 'production') return 'production';
  if (profile === 'preview') return 'preview';
  return 'development';
}

function resolveApiBaseUrl(): string {
  const extra = Constants.expoConfig?.extra;
  const configured = (extra?.apiBaseUrl as string) ?? 'http://localhost:5080';
  const port = (extra?.apiPort as number) ?? 5080;

  if (__DEV__ && Platform.OS !== 'web') {
    const hostUri = Constants.expoConfig?.hostUri ?? '';
    const lanIp = hostUri.split(':')[0];
    if (lanIp && lanIp !== 'localhost' && lanIp !== '127.0.0.1') {
      return `http://${lanIp}:${port}`;
    }
  }

  return configured.replace(/\/+$/, '');
}

function buildConfig(): AppConfig {
  const extra = Constants.expoConfig?.extra;
  const env = resolveEnvironment();
  const supabaseUrl = ((extra?.supabaseUrl as string) ?? '').replace(/\/+$/, '');
  const supabaseAnonKey = (extra?.supabaseAnonKey as string) ?? '';

  return {
    env,
    apiBaseUrl: resolveApiBaseUrl(),
    supabaseUrl,
    supabaseAnonKey,
    isSupabaseConfigured: !!supabaseUrl && !!supabaseAnonKey,
  };
}

export const appConfig = buildConfig();
