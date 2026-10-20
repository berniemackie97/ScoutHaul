import 'react-native-url-polyfill/auto';
import { createClient, processLock, type SupabaseClient } from '@supabase/supabase-js';
import { AppState, Platform } from 'react-native';
import { appConfig } from '../config';
import { secureSessionStorage } from './secureSessionStorage';

let client: SupabaseClient | null = null;
let didRegisterAppState = false;

export function getSupabaseClient(): SupabaseClient | null {
  if (!appConfig.isSupabaseConfigured) {
    return null;
  }

  if (client) {
    return client;
  }

  client = createClient(appConfig.supabaseUrl, appConfig.supabaseAnonKey, {
    auth: {
      ...(Platform.OS !== 'web' ? { storage: secureSessionStorage } : {}),
      // PKCE: the redirect carries a one-time code, not the tokens themselves.
      flowType: 'pkce',
      autoRefreshToken: true,
      persistSession: true,
      detectSessionInUrl: Platform.OS === 'web',
      lock: processLock,
    },
  });

  if (Platform.OS !== 'web' && !didRegisterAppState) {
    didRegisterAppState = true;
    AppState.addEventListener('change', (state) => {
      if (!client) return;
      if (state === 'active') {
        void client.auth.startAutoRefresh();
      } else {
        void client.auth.stopAutoRefresh();
      }
    });
  }

  return client;
}
