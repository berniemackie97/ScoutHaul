import { Ionicons } from '@expo/vector-icons';
import { LinearGradient } from 'expo-linear-gradient';
import * as WebBrowser from 'expo-web-browser';
import { router, useLocalSearchParams } from 'expo-router';
import { useRef, useState } from 'react';
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
import { radius, spacing } from '../src/components/theme';
import { appConfig } from '../src/config';
import { useAppTheme } from '../src/context/ThemeContext';
import { saveWorkspaceProfile } from '../src/db/localStore';
import {
  signInWithOAuthProvider,
  signInWithPassword,
  signUpWithPassword,
} from '../src/services/auth';

WebBrowser.maybeCompleteAuthSession();

export default function AccountScreen() {
  const params = useLocalSearchParams<{ mode?: 'signin' | 'signup' }>();
  const { colors: c } = useAppTheme();
  const insets = useSafeAreaInsets();
  const [mode, setMode] = useState<'signin' | 'signup'>(
    params.mode === 'signin' ? 'signin' : 'signup',
  );
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<'error' | 'info'>('error');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [oauthLoading, setOauthLoading] = useState<string | null>(null);
  const [focusedField, setFocusedField] = useState<'email' | 'password' | null>(null);

  const passwordRef = useRef<TextInput>(null);

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

      await saveWorkspaceProfile({
        accountMode: 'signedIn',
        accountEmail,
        displayName,
        onboardingCompleted: true,
        onboardingSkipped: false,
      });
      router.replace('/(tabs)');
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

      await saveWorkspaceProfile({
        accountMode: 'signedIn',
        accountEmail,
        displayName,
        onboardingCompleted: true,
        onboardingSkipped: false,
      });

      router.replace('/(tabs)');
    } catch (e) {
      setMessageType('error');
      setMessage(e instanceof Error ? e.message : 'Something went wrong. Try again.');
    } finally {
      setIsSubmitting(false);
    }
  }

  async function continueLocal() {
    await saveWorkspaceProfile({
      accountMode: 'local',
      onboardingCompleted: true,
      onboardingSkipped: true,
    });
    router.replace('/(tabs)');
  }

  const isSignUp = mode === 'signup';

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
              <Ionicons name="trail-sign" size={20} color={c.primary} />
            </View>
            <Text variant="bodyBold" color="#FFFFFF" style={styles.logoText}>
              HaulScout
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
});
