import type { Provider, Session, User } from '@supabase/supabase-js';
import * as WebBrowser from 'expo-web-browser';
import { makeRedirectUri } from 'expo-auth-session';
import { getSupabaseClient } from './supabaseClient';

type AuthListener = (isAuthenticated: boolean) => void;

export type AuthSession = {
  accessToken: string;
  refreshToken?: string | null;
  expiresAt?: number | null;
};

export type AuthAccount = {
  email: string | null;
  displayName: string | null;
};

class AuthStore {
  private token: string | null = null;
  private session: AuthSession | null = null;
  private account: AuthAccount | null = null;
  private listeners: Set<AuthListener> = new Set();

  getToken(): string | null {
    return this.token;
  }

  getSession(): AuthSession | null {
    return this.session;
  }

  getAccount(): AuthAccount | null {
    return this.account;
  }

  setSession(session: AuthSession | null, account: AuthAccount | null = this.account): void {
    this.session = session;
    this.token = session?.accessToken ?? null;
    this.account = account;
    this.notify();
  }

  get isAuthenticated(): boolean {
    return this.token !== null;
  }

  subscribe(listener: AuthListener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  private notify(): void {
    for (const listener of this.listeners) {
      listener(this.isAuthenticated);
    }
  }

  clear(): void {
    this.token = null;
    this.session = null;
    this.account = null;
    this.notify();
  }
}

export const authStore = new AuthStore();

let didRegisterAuthListener = false;

export async function initializeAuthSession(): Promise<void> {
  const supabase = getSupabaseClient();
  if (!supabase) {
    authStore.clear();
    return;
  }

  const { data } = await supabase.auth.getSession();
  syncSession(data.session);

  if (!didRegisterAuthListener) {
    didRegisterAuthListener = true;
    supabase.auth.onAuthStateChange((_event, session) => {
      syncSession(session);
    });
  }
}

export async function signInWithPassword(email: string, password: string): Promise<Session | null> {
  const supabase = requireSupabaseClient();
  const { data, error } = await supabase.auth.signInWithPassword({ email, password });
  if (error) throw new Error(error.message);
  syncSession(data.session);
  return data.session;
}

export async function signUpWithPassword(email: string, password: string): Promise<Session | null> {
  const supabase = requireSupabaseClient();
  const { data, error } = await supabase.auth.signUp({ email, password });
  if (error) throw new Error(error.message);
  syncSession(data.session);
  return data.session;
}

export async function signInWithOAuthProvider(provider: 'google' | 'apple'): Promise<Session | null> {
  const supabase = requireSupabaseClient();
  const redirectTo = makeRedirectUri();

  const { data, error } = await supabase.auth.signInWithOAuth({
    provider: provider as Provider,
    options: {
      redirectTo,
      skipBrowserRedirect: true,
    },
  });

  if (error) throw new Error(error.message);
  if (!data.url) throw new Error('No OAuth URL returned.');

  const result = await WebBrowser.openAuthSessionAsync(data.url, redirectTo);

  if (result.type !== 'success' || !result.url) {
    return null;
  }

  // Extract tokens from the redirect URL fragment
  const url = new URL(result.url);
  // Supabase puts tokens in the hash fragment
  const hashParams = new URLSearchParams(url.hash.substring(1));
  const accessToken = hashParams.get('access_token');
  const refreshToken = hashParams.get('refresh_token');

  if (!accessToken) {
    // Try query params as fallback (some flows use code exchange)
    const code = url.searchParams.get('code');
    if (code) {
      const { data: sessionData, error: exchangeError } =
        await supabase.auth.exchangeCodeForSession(code);
      if (exchangeError) throw new Error(exchangeError.message);
      syncSession(sessionData.session);
      return sessionData.session;
    }
    throw new Error('No authentication tokens received.');
  }

  const { data: sessionData, error: setError } = await supabase.auth.setSession({
    access_token: accessToken,
    refresh_token: refreshToken ?? '',
  });

  if (setError) throw new Error(setError.message);
  syncSession(sessionData.session);
  return sessionData.session;
}

export async function signOut(): Promise<void> {
  const supabase = getSupabaseClient();
  if (supabase) {
    await supabase.auth.signOut();
  }

  authStore.clear();
}

function requireSupabaseClient() {
  const supabase = getSupabaseClient();
  if (!supabase) {
    throw new Error('Supabase is not configured.');
  }

  return supabase;
}

function syncSession(session: Session | null): void {
  if (!session) {
    authStore.clear();
    return;
  }

  authStore.setSession(
    {
      accessToken: session.access_token,
      refreshToken: session.refresh_token,
      expiresAt: session.expires_at ? session.expires_at * 1000 : null,
    },
    buildAccount(session.user),
  );
}

function buildAccount(user: User | null): AuthAccount | null {
  if (!user) {
    return null;
  }

  const displayName = typeof user.user_metadata?.name === 'string'
    ? user.user_metadata.name
    : typeof user.user_metadata?.full_name === 'string'
      ? user.user_metadata.full_name
      : null;

  return {
    email: user.email ?? null,
    displayName,
  };
}
