import { Ionicons } from '@expo/vector-icons';
import { LinearGradient } from 'expo-linear-gradient';
import { router, useLocalSearchParams } from 'expo-router';
import { useRef, useState } from 'react';
import {
  Animated,
  Dimensions,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  StyleSheet,
  TextInput,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Button } from '../src/components/Button';
import { Text } from '../src/components/Text';
import { radius, spacing } from '../src/components/theme';
import { appConfig } from '../src/config';
import { useAppTheme } from '../src/context/ThemeContext';
import { saveWorkspaceProfile } from '../src/db/localStore';
import { signInWithPassword, signUpWithPassword } from '../src/services/auth';

const { width: SCREEN_WIDTH } = Dimensions.get('window');

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
  const [focusedField, setFocusedField] = useState<'email' | 'password' | null>(null);

  const passwordRef = useRef<TextInput>(null);
  const fadeAnim = useRef(new Animated.Value(1)).current;

  function switchMode(newMode: 'signin' | 'signup') {
    Animated.timing(fadeAnim, {
      toValue: 0,
      duration: 120,
      useNativeDriver: true,
    }).start(() => {
      setMode(newMode);
      setMessage(null);
      Animated.timing(fadeAnim, {
        toValue: 1,
        duration: 200,
        useNativeDriver: true,
      }).start();
    });
  }

  async function submitAccount() {
    if (!appConfig.isSupabaseConfigured) {
      setMessageType('error');
      setMessage(
        'Auth not configured. Set EXPO_PUBLIC_SUPABASE_URL and EXPO_PUBLIC_SUPABASE_ANON_KEY, then restart.',
      );
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
        style={[styles.headerGradient, { paddingTop: insets.top + spacing.xl }]}
      >
        {/* Decorative circles */}
        <View style={[styles.decorCircle, styles.decorCircle1]} />
        <View style={[styles.decorCircle, styles.decorCircle2]} />
        <View style={[styles.decorCircle, styles.decorCircle3]} />

        <View style={styles.headerContent}>
          <View style={styles.logoRow}>
            <View style={styles.logoMark}>
              <Ionicons name="trail-sign" size={22} color={c.primary} />
            </View>
            <Text variant="bodyBold" color="#FFFFFF" style={styles.logoText}>
              HaulScout
            </Text>
          </View>
          <Text variant="title" color="#FFFFFF" style={styles.heroTitle}>
            {isSignUp ? 'Start your resale\nbusiness command center' : 'Welcome back'}
          </Text>
          <Text variant="body" color="rgba(255,255,255,0.75)" style={styles.heroSubtitle}>
            {isSignUp
              ? 'Source smarter. Track everything. Scale with confidence.'
              : 'Sign in to sync your inventory, routes, and analytics.'}
          </Text>
        </View>
      </LinearGradient>

      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        style={styles.formWrapper}
      >
        {/* Mode toggle */}
        <View style={[styles.modeToggle, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}>
          <Pressable
            accessibilityRole="tab"
            accessibilityState={{ selected: isSignUp }}
            onPress={() => switchMode('signup')}
            style={[
              styles.modeTab,
              isSignUp && [styles.modeTabActive, { backgroundColor: c.surface }],
            ]}
          >
            <Text
              variant="bodyBold"
              color={isSignUp ? c.primary : c.textMuted}
              style={styles.modeTabText}
            >
              Create Account
            </Text>
          </Pressable>
          <Pressable
            accessibilityRole="tab"
            accessibilityState={{ selected: !isSignUp }}
            onPress={() => switchMode('signin')}
            style={[
              styles.modeTab,
              !isSignUp && [styles.modeTabActive, { backgroundColor: c.surface }],
            ]}
          >
            <Text
              variant="bodyBold"
              color={!isSignUp ? c.primary : c.textMuted}
              style={styles.modeTabText}
            >
              Sign In
            </Text>
          </Pressable>
        </View>

        <Animated.View style={[styles.formCard, { backgroundColor: c.surface, borderColor: c.borderLight, opacity: fadeAnim }]}>
          {/* Message banner */}
          {message ? (
            <View
              style={[
                styles.messageBanner,
                {
                  backgroundColor:
                    messageType === 'error' ? c.dangerLight : c.infoLight,
                  borderColor:
                    messageType === 'error' ? c.danger : c.info,
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
                  backgroundColor: c.background,
                },
                focusedField === 'email' && styles.inputFocused,
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
                  backgroundColor: c.background,
                },
                focusedField === 'password' && styles.inputFocused,
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
                placeholder={isSignUp ? 'Create a strong password' : 'Enter your password'}
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
            {isSignUp ? (
              <Text variant="caption" color={c.textMuted} style={styles.fieldHint}>
                Minimum 8 characters
              </Text>
            ) : null}
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
              <Text variant="bodyBold" color="#FFFFFF">
                {isSignUp ? 'Creating account...' : 'Signing in...'}
              </Text>
            ) : (
              <View style={styles.submitInner}>
                <Text variant="bodyBold" color="#FFFFFF">
                  {isSignUp ? 'Create Account' : 'Sign In'}
                </Text>
                <Ionicons name="arrow-forward" size={18} color="#FFFFFF" />
              </View>
            )}
          </Pressable>
        </Animated.View>

        {/* Divider */}
        <View style={styles.dividerRow}>
          <View style={[styles.dividerLine, { backgroundColor: c.border }]} />
          <Text variant="caption" color={c.textMuted} style={styles.dividerText}>
            or
          </Text>
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
            <View style={[styles.localIcon, { backgroundColor: c.surfaceAlt }]}>
              <Ionicons name="phone-portrait-outline" size={18} color={c.textSecondary} />
            </View>
            <View style={styles.localTextBlock}>
              <Text variant="bodyBold" color={c.text}>
                Continue without account
              </Text>
              <Text variant="caption" color={c.textMuted}>
                Full offline access. Sync anytime later.
              </Text>
            </View>
            <Ionicons name="chevron-forward" size={18} color={c.textMuted} />
          </View>
        </Pressable>

        {/* Footer */}
        <View style={[styles.footer, { paddingBottom: insets.bottom + spacing.lg }]}>
          <Text variant="caption" color={c.textMuted} style={styles.footerText}>
            By continuing, you agree to our Terms of Service and Privacy Policy.
          </Text>
        </View>
      </KeyboardAvoidingView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  headerGradient: {
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.xxxl,
    overflow: 'hidden',
  },
  decorCircle: {
    position: 'absolute',
    borderRadius: 999,
    backgroundColor: 'rgba(255,255,255,0.06)',
  },
  decorCircle1: {
    width: 200,
    height: 200,
    top: -40,
    right: -60,
  },
  decorCircle2: {
    width: 120,
    height: 120,
    top: 60,
    left: -30,
  },
  decorCircle3: {
    width: 80,
    height: 80,
    bottom: 10,
    right: 40,
  },
  headerContent: {
    gap: spacing.md,
  },
  logoRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  logoMark: {
    width: 36,
    height: 36,
    borderRadius: 10,
    backgroundColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
  },
  logoText: {
    fontSize: 17,
    letterSpacing: 0.5,
  },
  heroTitle: {
    fontSize: 26,
    lineHeight: 32,
    fontWeight: '800',
    letterSpacing: -0.3,
  },
  heroSubtitle: {
    fontSize: 14,
    lineHeight: 20,
  },
  formWrapper: {
    flex: 1,
    marginTop: -spacing.xl,
    paddingHorizontal: spacing.lg,
    gap: spacing.lg,
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
  modeTabText: {
    fontSize: 14,
  },
  formCard: {
    borderRadius: radius.xl,
    borderWidth: 1,
    padding: spacing.xl,
    gap: spacing.lg,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.04,
    shadowRadius: 8,
    elevation: 2,
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
  inputFocused: {
    shadowColor: '#0B6B5B',
    shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.12,
    shadowRadius: 4,
    elevation: 2,
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
  fieldHint: {
    marginLeft: spacing.xs,
    fontSize: 11,
  },
  submitButton: {
    minHeight: 52,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: spacing.xs,
  },
  submitInner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  submitDisabled: {
    opacity: 0.5,
  },
  dividerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  dividerLine: {
    flex: 1,
    height: 1,
  },
  dividerText: {
    fontSize: 12,
  },
  localButton: {
    borderRadius: radius.lg,
    borderWidth: 1,
    padding: spacing.lg,
  },
  localButtonInner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  localIcon: {
    width: 40,
    height: 40,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  localTextBlock: {
    flex: 1,
    gap: 2,
  },
  footer: {
    marginTop: 'auto',
    alignItems: 'center',
    paddingTop: spacing.lg,
  },
  footerText: {
    textAlign: 'center',
    fontSize: 11,
    lineHeight: 16,
  },
});
