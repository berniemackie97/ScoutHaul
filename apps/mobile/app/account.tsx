import { Ionicons } from '@expo/vector-icons';
import { LinearGradient } from 'expo-linear-gradient';
import * as WebBrowser from 'expo-web-browser';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  TextInput,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Text } from '../src/components/Text';
import { api } from '../src/api';
import { InvitePanel } from '../src/features/account/InvitePanel';
import { radius, spacing } from '../src/components/theme';
import { appConfig } from '../src/config';
import { useAppTheme } from '../src/context/ThemeContext';
import { getWorkspaceProfile, saveWorkspaceProfile } from '../src/db/localStore';
import {
  authStore,
  signInWithOAuthProvider,
  signInWithPassword,
  signOut,
  signUpWithPassword,
} from '../src/services/auth';
import { bootstrapSignedInWorkspace } from '../src/services/sessionBootstrap';
import { setActiveOrganizationId } from '../src/services/workspaceSession';

WebBrowser.maybeCompleteAuthSession();

export default function AccountScreen() {
  const params = useLocalSearchParams<{ mode?: 'signin' | 'signup' }>();
  const { colors: c } = useAppTheme();
  const insets = useSafeAreaInsets();
  const [isAuthenticated, setIsAuthenticated] = useState(authStore.isAuthenticated);
  const [accountInfo, setAccountInfo] = useState(authStore.getAccount());
  const [mode, setMode] = useState<'signin' | 'signup'>(
    params.mode === 'signin' ? 'signin' : 'signup',
  );
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [inviteCode, setInviteCode] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<'error' | 'info'>('error');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isJoiningTeam, setIsJoiningTeam] = useState(false);
  const [oauthLoading, setOauthLoading] = useState<string | null>(null);
  const [focusedField, setFocusedField] = useState<'email' | 'password' | null>(null);
  const [isSigningOut, setIsSigningOut] = useState(false);

  const passwordRef = useRef<TextInput>(null);

  // Subscribe to auth state changes
  useEffect(() => {
    return authStore.subscribe((authed) => {
      setIsAuthenticated(authed);
      setAccountInfo(authStore.getAccount());
    });
  }, []);

  useEffect(() => {
    let isMounted = true;
    void (async () => {
      const existing = await getWorkspaceProfile();
      if (isMounted && existing?.pendingInviteCode) {
        setInviteCode(existing.pendingInviteCode);
      }
    })();

    return () => {
      isMounted = false;
    };
  }, []);

  async function redeemInviteCodeForSession(code: string) {
    const result = await api.redeemOrganizationInviteCode(code);
    await setActiveOrganizationId(result.organization.id);
    await saveWorkspaceProfile({
      usageMode: 'join',
      accountMode: 'signedIn',
      joinedOrganizationId: result.organization.id,
      joinedTeamId: result.teamId ?? null,
      pendingInviteCode: null,
    });
    setInviteCode('');
    return result;
  }

  async function completeAuthenticatedSession(accountEmail: string, displayName: string) {
    const existing = await getWorkspaceProfile();
    await saveWorkspaceProfile({
      accountMode: 'signedIn',
      accountEmail,
      displayName,
    });

    const pendingInviteCode = existing?.pendingInviteCode?.trim();
    if (pendingInviteCode) {
      await redeemInviteCodeForSession(pendingInviteCode);
      router.replace('/(tabs)');
      return;
    }

    await bootstrapSignedInWorkspace();
    router.replace('/(tabs)');
  }

  async function joinTeamFromInviteCode() {
    const normalizedInviteCode = inviteCode.trim();
    if (!normalizedInviteCode) {
      setMessageType('error');
      setMessage('Enter a team invite code.');
      return;
    }

    if (!authStore.isAuthenticated) {
      await saveWorkspaceProfile({
        usageMode: 'join',
        planIntent: 'team',
        pendingInviteCode: normalizedInviteCode,
        accountMode: 'signedOut',
      });
      setMode('signup');
      setMessageType('info');
      setMessage('Invite code saved. Create an account or sign in to join that team.');
      return;
    }

    setIsJoiningTeam(true);
    setMessage(null);
    try {
      const result = await redeemInviteCodeForSession(normalizedInviteCode);
      setMessageType('info');
      setMessage(`Joined ${result.organization.name}.`);
    } catch (e) {
      setMessageType('error');
      setMessage(e instanceof Error ? e.message : 'Unable to join team.');
    } finally {
      setIsJoiningTeam(false);
    }
  }

  async function handleOAuth(provider: 'google' | 'apple') {
    if (!appConfig.isSupabaseConfigured) {
      setMessageType('error');
      setMessage('Auth not configured.');
      return;
    }
    setOauthLoading(provider);
    setMessage(null);
    try {
      const session = await signInWithOAuthProvider(provider);
      if (!session) {
        setOauthLoading(null);
        return;
      }
      const accountEmail = session.user.email ?? '';
      const displayName =
        typeof session.user.user_metadata?.name === 'string'
          ? session.user.user_metadata.name
          : typeof session.user.user_metadata?.full_name === 'string'
            ? session.user.user_metadata.full_name
            : accountEmail.split('@')[0];

      await completeAuthenticatedSession(accountEmail, displayName);
    } catch (e) {
      setMessageType('error');
      setMessage(e instanceof Error ? e.message : 'OAuth sign-in failed.');
    } finally {
      setOauthLoading(null);
    }
  }

  async function submitAccount() {
    if (!appConfig.isSupabaseConfigured) {
      setMessageType('error');
      setMessage('Auth not configured.');
      return;
    }

    const trimmedEmail = email.trim();
    if (!trimmedEmail || !trimmedEmail.includes('@')) {
      setMessageType('error');
      setMessage('Enter a valid email address.');
      return;
    }
    if (password.length < 8) {
      setMessageType('error');
      setMessage('Password must be at least 8 characters.');
      return;
    }

    setIsSubmitting(true);
    setMessage(null);

    try {
      const session =
        mode === 'signup'
          ? await signUpWithPassword(trimmedEmail, password)
          : await signInWithPassword(trimmedEmail, password);

      if (!session) {
        setMessageType('info');
        setMessage('Check your email to confirm your account, then sign in.');
        setMode('signin');
        return;
      }

      const accountEmail = session.user.email ?? trimmedEmail.toLowerCase();
      const displayName =
        typeof session.user.user_metadata?.name === 'string'
          ? session.user.user_metadata.name
          : accountEmail.split('@')[0];

      await completeAuthenticatedSession(accountEmail, displayName);
    } catch (e) {
      setMessageType('error');
      setMessage(e instanceof Error ? e.message : 'Something went wrong. Try again.');
    } finally {
      setIsSubmitting(false);
    }
  }

  async function continueLocal() {
    const existing = await getWorkspaceProfile();
    await saveWorkspaceProfile({
      accountMode: 'local',
      pendingInviteCode: null,
    });
    router.replace(existing?.onboardingCompleted ? '/(tabs)' : '/onboarding');
  }

  async function handleSignOut() {
    setIsSigningOut(true);
    try {
      await signOut();
      await saveWorkspaceProfile({ accountMode: 'signedOut' });
      setMessage(null);
      setMode('signin');
    } catch (e) {
      setMessageType('error');
      setMessage(e instanceof Error ? e.message : 'Sign out failed.');
    } finally {
      setIsSigningOut(false);
    }
  }

  function renderInvitePanel() {
    return (
      <InvitePanel
        inviteCode={inviteCode}
        onChangeCode={setInviteCode}
        isJoining={isJoiningTeam}
        isAuthenticated={authStore.isAuthenticated}
        onJoin={() => void joinTeamFromInviteCode()}
      />
    );
  }

  const isSignUp = mode === 'signup';

  // ── Signed-in view ──
  if (isAuthenticated) {
    return (
      <View style={[styles.container, { backgroundColor: c.background }]}>
        <LinearGradient
          colors={[c.primary, c.primaryPressed]}
          start={{ x: 0, y: 0 }}
          end={{ x: 1, y: 1 }}
          style={[styles.headerGradient, { paddingTop: insets.top + spacing.lg }]}
        >
          <View style={[styles.decorCircle, styles.decorCircle1]} />
          <View style={[styles.decorCircle, styles.decorCircle2]} />
          <View style={styles.headerContent}>
            <View style={styles.logoRow}>
              <View style={styles.logoMark}>
                <Ionicons name="compass" size={20} color={c.primary} />
              </View>
              <Text variant="bodyBold" color="#FFFFFF" style={styles.logoText}>ScoutHaul</Text>
            </View>
            <Text variant="title" color="#FFFFFF" style={styles.heroTitle}>Your Account</Text>
          </View>
        </LinearGradient>

        <ScrollView
          style={styles.scrollView}
          contentContainerStyle={[styles.scrollContent, { paddingBottom: insets.bottom + spacing.xxl }]}
          showsVerticalScrollIndicator={false}
        >
          {/* Account info card */}
          <View style={[styles.accountCard, { backgroundColor: c.surface, borderColor: c.border }]}>
            <View style={[styles.avatarCircle, { backgroundColor: c.primaryLight }]}>
              <Ionicons name="person" size={28} color={c.primary} />
            </View>
            <View style={styles.accountInfo}>
              {accountInfo?.displayName ? (
                <Text variant="bodyBold">{accountInfo.displayName}</Text>
              ) : null}
              {accountInfo?.email ? (
                <Text variant="caption" color={c.textSecondary}>{accountInfo.email}</Text>
              ) : null}
              <View style={styles.authBadge}>
                <Ionicons name="checkmark-circle" size={14} color={c.success} />
                <Text variant="label" color={c.success}>Signed in</Text>
              </View>
            </View>
          </View>

          {renderInvitePanel()}

          {message ? (
            <View style={[styles.messageBanner, { backgroundColor: c.dangerLight, borderColor: c.danger }]}>
              <Ionicons name="alert-circle" size={18} color={c.danger} />
              <Text variant="caption" color={c.danger} style={styles.messageText}>{message}</Text>
            </View>
          ) : null}

          {/* Back to app */}
          <Pressable
            onPress={() => router.replace('/(tabs)')}
            style={({ pressed }) => [
              styles.submitButton,
              { backgroundColor: c.primary },
              pressed && { backgroundColor: c.primaryPressed },
            ]}
          >
            <View style={styles.submitInner}>
              <Ionicons name="arrow-back" size={18} color="#FFFFFF" />
              <Text variant="bodyBold" color="#FFFFFF">Back to App</Text>
            </View>
          </Pressable>

          {/* Sign out */}
          <Pressable
            onPress={() => void handleSignOut()}
            disabled={isSigningOut}
            style={({ pressed }) => [
              styles.localButton,
              { borderColor: c.danger, backgroundColor: pressed ? c.dangerLight : c.surface },
              isSigningOut && styles.submitDisabled,
            ]}
          >
            {isSigningOut ? (
              <ActivityIndicator size="small" color={c.danger} />
            ) : (
              <View style={styles.localButtonInner}>
                <Ionicons name="log-out-outline" size={20} color={c.danger} />
                <View style={styles.localTextBlock}>
                  <Text variant="bodyBold" color={c.danger}>Sign Out</Text>
                  <Text variant="caption" color={c.textMuted}>Data stays on device</Text>
                </View>
              </View>
            )}
          </Pressable>
        </ScrollView>
      </View>
    );
  }

  // ── Sign-in / Sign-up view ──
  return (
    <View style={[styles.container, { backgroundColor: c.background }]}>
      {/* Gradient header */}
      <LinearGradient
        colors={[c.primary, c.primaryPressed]}
        start={{ x: 0, y: 0 }}
        end={{ x: 1, y: 1 }}
        style={[styles.headerGradient, { paddingTop: insets.top + spacing.lg }]}
      >
        <View style={[styles.decorCircle, styles.decorCircle1]} />
        <View style={[styles.decorCircle, styles.decorCircle2]} />

        <View style={styles.headerContent}>
          <View style={styles.logoRow}>
            <View style={styles.logoMark}>
              <Ionicons name="compass" size={20} color={c.primary} />
            </View>
            <Text variant="bodyBold" color="#FFFFFF" style={styles.logoText}>
              ScoutHaul
            </Text>
          </View>
          <Text variant="title" color="#FFFFFF" style={styles.heroTitle}>
            {isSignUp ? 'Create your account' : 'Welcome back'}
          </Text>
        </View>
      </LinearGradient>

      <ScrollView
        style={styles.scrollView}
        contentContainerStyle={[styles.scrollContent, { paddingBottom: insets.bottom + spacing.xxl }]}
        keyboardShouldPersistTaps="handled"
        keyboardDismissMode="none"
        showsVerticalScrollIndicator={false}
      >
        {/* Mode toggle */}
        <View style={[styles.modeToggle, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}>
          <Pressable
            accessibilityRole="tab"
            accessibilityState={{ selected: isSignUp }}
            onPress={() => { setMode('signup'); setMessage(null); }}
            style={[
              styles.modeTab,
              isSignUp && [styles.modeTabActive, { backgroundColor: c.surface }],
            ]}
          >
            <Text variant="bodyBold" color={isSignUp ? c.primary : c.textMuted}>
              Create Account
            </Text>
          </Pressable>
          <Pressable
            accessibilityRole="tab"
            accessibilityState={{ selected: !isSignUp }}
            onPress={() => { setMode('signin'); setMessage(null); }}
            style={[
              styles.modeTab,
              !isSignUp && [styles.modeTabActive, { backgroundColor: c.surface }],
            ]}
          >
            <Text variant="bodyBold" color={!isSignUp ? c.primary : c.textMuted}>
              Sign In
            </Text>
          </Pressable>
        </View>

        {renderInvitePanel()}

        {/* OAuth buttons */}
        <View style={styles.oauthSection}>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Continue with Google"
            disabled={oauthLoading !== null}
            onPress={() => void handleOAuth('google')}
            style={({ pressed }) => [
              styles.oauthButton,
              { backgroundColor: c.surface, borderColor: c.border },
              pressed && { backgroundColor: c.surfaceAlt },
            ]}
          >
            {oauthLoading === 'google' ? (
              <ActivityIndicator size="small" color={c.textSecondary} />
            ) : (
              <>
                <Ionicons name="logo-google" size={20} color="#4285F4" />
                <Text variant="bodyBold" color={c.text}>Continue with Google</Text>
              </>
            )}
          </Pressable>

          {Platform.OS === 'ios' ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Continue with Apple"
              disabled={oauthLoading !== null}
              onPress={() => void handleOAuth('apple')}
              style={({ pressed }) => [
                styles.oauthButton,
                { backgroundColor: c.text, borderColor: c.text },
                pressed && { opacity: 0.85 },
              ]}
            >
              {oauthLoading === 'apple' ? (
                <ActivityIndicator size="small" color={c.textInverse} />
              ) : (
                <>
                  <Ionicons name="logo-apple" size={20} color={c.textInverse} />
                  <Text variant="bodyBold" color={c.textInverse}>Continue with Apple</Text>
                </>
              )}
            </Pressable>
          ) : null}
        </View>

        {/* Divider */}
        <View style={styles.dividerRow}>
          <View style={[styles.dividerLine, { backgroundColor: c.border }]} />
          <Text variant="caption" color={c.textMuted}>or use email</Text>
          <View style={[styles.dividerLine, { backgroundColor: c.border }]} />
        </View>

        {/* Message banner */}
        {message ? (
          <View
            style={[
              styles.messageBanner,
              {
                backgroundColor: messageType === 'error' ? c.dangerLight : c.infoLight,
                borderColor: messageType === 'error' ? c.danger : c.info,
              },
            ]}
          >
            <Ionicons
              name={messageType === 'error' ? 'alert-circle' : 'information-circle'}
              size={18}
              color={messageType === 'error' ? c.danger : c.info}
            />
            <Text
              variant="caption"
              color={messageType === 'error' ? c.dangerDark : c.info}
              style={styles.messageText}
            >
              {message}
            </Text>
          </View>
        ) : null}

        {/* Email field */}
        <View style={styles.fieldBlock}>
          <Text variant="caption" color={c.textSecondary} style={styles.fieldLabel}>
            Email
          </Text>
          <View
            style={[
              styles.inputContainer,
              {
                borderColor: focusedField === 'email' ? c.primary : c.border,
                backgroundColor: c.surface,
              },
            ]}
          >
            <Ionicons
              name="mail-outline"
              size={18}
              color={focusedField === 'email' ? c.primary : c.textMuted}
              style={styles.inputIcon}
            />
            <TextInput
              value={email}
              onChangeText={setEmail}
              placeholder="you@example.com"
              placeholderTextColor={c.textMuted}
              autoCapitalize="none"
              autoCorrect={false}
              autoComplete="email"
              keyboardType="email-address"
              textContentType="emailAddress"
              returnKeyType="next"
              onSubmitEditing={() => passwordRef.current?.focus()}
              onFocus={() => setFocusedField('email')}
              onBlur={() => setFocusedField(null)}
              style={[styles.textInput, { color: c.text }]}
            />
          </View>
        </View>

        {/* Password field */}
        <View style={styles.fieldBlock}>
          <Text variant="caption" color={c.textSecondary} style={styles.fieldLabel}>
            Password
          </Text>
          <View
            style={[
              styles.inputContainer,
              {
                borderColor: focusedField === 'password' ? c.primary : c.border,
                backgroundColor: c.surface,
              },
            ]}
          >
            <Ionicons
              name="lock-closed-outline"
              size={18}
              color={focusedField === 'password' ? c.primary : c.textMuted}
              style={styles.inputIcon}
            />
            <TextInput
              ref={passwordRef}
              value={password}
              onChangeText={setPassword}
              placeholder={isSignUp ? 'Min. 8 characters' : 'Your password'}
              placeholderTextColor={c.textMuted}
              secureTextEntry={!showPassword}
              autoCapitalize="none"
              autoComplete={isSignUp ? 'new-password' : 'current-password'}
              textContentType={isSignUp ? 'newPassword' : 'password'}
              returnKeyType="done"
              onSubmitEditing={() => void submitAccount()}
              onFocus={() => setFocusedField('password')}
              onBlur={() => setFocusedField(null)}
              style={[styles.textInput, { color: c.text }]}
            />
            <Pressable
              onPress={() => setShowPassword(!showPassword)}
              hitSlop={8}
              style={styles.eyeButton}
              accessibilityLabel={showPassword ? 'Hide password' : 'Show password'}
            >
              <Ionicons
                name={showPassword ? 'eye-off-outline' : 'eye-outline'}
                size={20}
                color={c.textMuted}
              />
            </Pressable>
          </View>
        </View>

        {/* Submit */}
        <Pressable
          accessibilityRole="button"
          disabled={!appConfig.isSupabaseConfigured || isSubmitting}
          onPress={() => void submitAccount()}
          style={({ pressed }) => [
            styles.submitButton,
            { backgroundColor: c.primary },
            pressed && { backgroundColor: c.primaryPressed },
            (isSubmitting || !appConfig.isSupabaseConfigured) && styles.submitDisabled,
          ]}
        >
          {isSubmitting ? (
            <ActivityIndicator size="small" color="#FFFFFF" />
          ) : (
            <View style={styles.submitInner}>
              <Text variant="bodyBold" color="#FFFFFF">
                {isSignUp ? 'Create Account' : 'Sign In'}
              </Text>
              <Ionicons name="arrow-forward" size={18} color="#FFFFFF" />
            </View>
          )}
        </Pressable>

        {/* Second divider */}
        <View style={styles.dividerRow}>
          <View style={[styles.dividerLine, { backgroundColor: c.border }]} />
        </View>

        {/* Continue local */}
        <Pressable
          accessibilityRole="button"
          onPress={() => void continueLocal()}
          style={({ pressed }) => [
            styles.localButton,
            { borderColor: c.border, backgroundColor: pressed ? c.surfaceAlt : c.surface },
          ]}
        >
          <View style={styles.localButtonInner}>
            <Ionicons name="phone-portrait-outline" size={20} color={c.textSecondary} />
            <View style={styles.localTextBlock}>
              <Text variant="bodyBold" color={c.text}>Continue without account</Text>
              <Text variant="caption" color={c.textMuted}>Full offline access. Sync later.</Text>
            </View>
            <Ionicons name="chevron-forward" size={16} color={c.textMuted} />
          </View>
        </Pressable>

        {/* Footer */}
        <Text variant="caption" color={c.textMuted} style={styles.footerText}>
          By continuing you agree to our Terms of Service and Privacy Policy.
        </Text>
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  headerGradient: {
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.xl,
    overflow: 'hidden',
  },
  decorCircle: {
    position: 'absolute',
    borderRadius: 999,
    backgroundColor: 'rgba(255,255,255,0.06)',
  },
  decorCircle1: {
    width: 180,
    height: 180,
    top: -50,
    right: -50,
  },
  decorCircle2: {
    width: 100,
    height: 100,
    bottom: -20,
    left: -20,
  },
  headerContent: {
    gap: spacing.sm,
  },
  logoRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  logoMark: {
    width: 32,
    height: 32,
    borderRadius: 8,
    backgroundColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
  },
  logoText: {
    fontSize: 16,
    letterSpacing: 0.5,
  },
  heroTitle: {
    fontSize: 24,
    lineHeight: 30,
    fontWeight: '800',
    letterSpacing: -0.3,
  },
  scrollView: {
    flex: 1,
  },
  scrollContent: {
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.lg,
    gap: spacing.md,
  },
  modeToggle: {
    flexDirection: 'row',
    borderRadius: radius.lg,
    borderWidth: 1,
    padding: 3,
  },
  modeTab: {
    flex: 1,
    alignItems: 'center',
    paddingVertical: spacing.md,
    borderRadius: radius.md,
  },
  modeTabActive: {
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.08,
    shadowRadius: 3,
    elevation: 2,
  },
  oauthSection: {
    gap: spacing.sm,
  },
  oauthButton: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    minHeight: 48,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  dividerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.xs,
  },
  dividerLine: {
    flex: 1,
    height: 1,
  },
  messageBanner: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  messageText: {
    flex: 1,
    lineHeight: 18,
  },
  fieldBlock: {
    gap: spacing.xs,
  },
  fieldLabel: {
    fontSize: 12,
    fontWeight: '600',
    letterSpacing: 0.3,
    marginLeft: spacing.xs,
  },
  inputContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: 50,
    borderWidth: 1.5,
    borderRadius: radius.md,
  },
  inputIcon: {
    marginLeft: spacing.md,
  },
  textInput: {
    flex: 1,
    minHeight: 50,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    fontSize: 15,
  },
  eyeButton: {
    padding: spacing.md,
  },
  submitButton: {
    minHeight: 52,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  joinButton: {
    minHeight: 48,
    borderRadius: radius.lg,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.lg,
  },
  submitInner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  submitDisabled: {
    opacity: 0.5,
  },
  localButton: {
    borderRadius: radius.lg,
    borderWidth: 1,
    padding: spacing.md,
  },
  localButtonInner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  localTextBlock: {
    flex: 1,
    gap: 2,
  },
  footerText: {
    textAlign: 'center',
    fontSize: 11,
    lineHeight: 16,
    paddingTop: spacing.sm,
  },
  accountCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.lg,
    borderRadius: radius.lg,
    borderWidth: 1,
  },
  joinCard: {
    borderWidth: 1,
    borderRadius: radius.xl,
    padding: spacing.lg,
    gap: spacing.md,
  },
  joinHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  joinIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.full,
    backgroundColor: 'rgba(11, 107, 91, 0.12)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  joinText: {
    flex: 1,
    gap: spacing.xs,
  },
  avatarCircle: {
    width: 56,
    height: 56,
    borderRadius: 28,
    alignItems: 'center',
    justifyContent: 'center',
  },
  accountInfo: {
    flex: 1,
    gap: spacing.xxs,
  },
  authBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xxs,
    marginTop: spacing.xxs,
  },
});
