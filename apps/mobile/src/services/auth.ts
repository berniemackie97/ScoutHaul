import type { Provider, Session, User } from '@supabase/supabase-js';
import * as WebBrowser from 'expo-web-browser';
import { makeRedirectUri } from 'expo-auth-session';
import { clearLocalWorkspace } from '../db/localWipe';
import { getSupabaseClient } from './supabaseClient';
import { setActiveOrganizationId } from './workspaceSession';

type AuthListener = (isAuthenticated: boolean) => void;

export type AuthSession = {
  accessToken: string;
  refreshToken?: string | null;
  expiresAt?: number | null;
};

export type AuthAccount = {
  id: string;
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

  /**
   * Whether the token is close enough to expiry that it is not worth spending on a request.
   * A session with no recorded expiry is treated as fine — we have nothing to judge it on.
   */
  isExpiringWithin(ms: number): boolean {
    const expiresAt = this.session?.expiresAt;
    if (!expiresAt) return false;
    return expiresAt - Date.now() <= ms;
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

/**
 * Forces a token refresh and reports whether we ended up with a usable session.
 *
 * Supabase refreshes on its own schedule, which is enough while the app is in the foreground. It is
 * not enough after the phone has been asleep: the app wakes with an expired token and the next
 * request 401s for a reason the user cannot do anything about. The API client calls this before a
 * request whose token is about to expire, and again after a 401.
 */
export async function refreshAuthSession(): Promise<boolean> {
  const supabase = getSupabaseClient();
  if (!supabase) return false;

  try {
    const { data, error } = await supabase.auth.refreshSession();
    if (error || !data.session) return false;
    syncSession(data.session);
    return true;
  } catch {
    // A refresh that cannot reach the network is not a signed-out session; leave the token alone
    // so the caller reports the transport failure rather than bouncing the user to sign-in.
    return false;
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
  const redirectTo = makeRedirectUri({ scheme: 'scouthaul', path: 'auth/callback' });

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

  const code = new URL(result.url).searchParams.get('code');
  if (!code) throw new Error('Sign-in did not return a code. Try again.');

  const { data: sessionData, error: exchangeError } = await supabase.auth.exchangeCodeForSession(code);
  if (exchangeError) throw new Error(exchangeError.message);
  syncSession(sessionData.session);
  return sessionData.session;
}

/**
 * Signs out on this phone only (the default would end the person's admin sessions too) and clears
 * what the phone held for them, so the next person to sign in starts clean.
 */
export async function signOut(): Promise<void> {
  const supabase = getSupabaseClient();
  if (supabase) {
    await supabase.auth.signOut({ scope: 'local' });
  }

  authStore.clear();
  await clearLocalWorkspace();
  await setActiveOrganizationId(null);
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
    id: user.id,
    email: user.email ?? null,
    displayName,
  };
}
