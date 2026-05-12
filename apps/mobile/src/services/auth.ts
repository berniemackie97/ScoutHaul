/**
 * Auth service abstraction.
 *
 * In development, StubAuth bypasses this entirely — the API client sends
 * hardcoded org/user IDs via headers instead of JWT tokens.
 *
 * For production, integrate your auth provider here:
 *   - Auth0: expo-auth-session + Auth0 SDK
 *   - Supabase: @supabase/supabase-js
 *   - Firebase: @react-native-firebase/auth
 *   - Custom: expo-secure-store for token persistence
 *
 * The authStore below is a minimal token holder. Replace with your
 * provider's session management when ready.
 */

type AuthListener = (isAuthenticated: boolean) => void;

class AuthStore {
  private token: string | null = null;
  private listeners: Set<AuthListener> = new Set();

  getToken(): string | null {
    return this.token;
  }

  setToken(token: string | null): void {
    this.token = token;
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
    this.notify();
  }
}

export const authStore = new AuthStore();
