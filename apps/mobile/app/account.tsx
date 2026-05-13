import { Ionicons } from '@expo/vector-icons';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Divider } from '../src/components/Divider';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { spacing } from '../src/components/theme';
import { appConfig } from '../src/config';
import { useColors } from '../src/context/ThemeContext';
import { saveWorkspaceProfile } from '../src/db/localStore';
import { signInWithPassword, signOut, signUpWithPassword } from '../src/services/auth';

export default function AccountScreen() {
  const params = useLocalSearchParams<{ mode?: 'signin' | 'signup' }>();
  const c = useColors();
  const [mode, setMode] = useState<'signin' | 'signup'>(params.mode === 'signin' ? 'signin' : 'signup');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function submitAccount() {
    if (!appConfig.isSupabaseConfigured) {
      setMessage('Supabase Auth is not configured yet. Add the project URL and anon key listed below, then restart Expo.');
      return;
    }

    if (!email.trim() || password.length < 8) {
      setMessage('Use a valid email and a password with at least 8 characters.');
      return;
    }

    setIsSubmitting(true);
    setMessage(null);

    try {
      const session = mode === 'signup'
        ? await signUpWithPassword(email.trim(), password)
        : await signInWithPassword(email.trim(), password);

      if (!session) {
        setMessage('Account created. Check your email to confirm before signing in.');
        return;
      }

      const accountEmail = session.user.email ?? email.trim().toLowerCase();
      const displayName = typeof session.user.user_metadata?.name === 'string'
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
      setMessage(e instanceof Error ? e.message : 'Unable to complete account action.');
    } finally {
      setIsSubmitting(false);
    }
  }

  async function continueLocal() {
    await signOut();
    await saveWorkspaceProfile({
      accountMode: 'local',
      onboardingCompleted: true,
      onboardingSkipped: true,
    });
    router.replace('/(tabs)');
  }

  return (
    <Screen>
      <View style={styles.hero}>
        <View style={[styles.accountMark, { backgroundColor: c.primary }]}>
          <Ionicons name="business-outline" size={28} color={c.textInverse} />
        </View>
        <Text variant="title">Account & Workspace</Text>
        <Text variant="body" color={c.textSecondary} style={styles.centerText}>
          Supabase handles authentication. ScoutHaul owns workspaces, teams, roles, permissions, and business data.
        </Text>
      </View>

      {message ? (
        <Card>
          <Text variant="bodyBold" color={c.warning}>{message}</Text>
        </Card>
      ) : null}

      <Card>
        <View style={styles.modeRow}>
          <Button label="Sign Up" variant={mode === 'signup' ? 'primary' : 'secondary'} onPress={() => setMode('signup')} />
          <Button label="Sign In" variant={mode === 'signin' ? 'primary' : 'secondary'} onPress={() => setMode('signin')} />
        </View>
        <TextField
          label="Email"
          value={email}
          onChangeText={setEmail}
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="email-address"
          placeholder="owner@example.com"
        />
        <TextField
          label="Password"
          value={password}
          onChangeText={setPassword}
          secureTextEntry
          autoCapitalize="none"
          placeholder="8+ characters"
        />
        <Button
          label={isSubmitting ? 'Connecting...' : mode === 'signup' ? 'Create Account' : 'Sign In'}
          icon={mode === 'signup' ? 'person-add-outline' : 'log-in-outline'}
          fullWidth
          disabled={!appConfig.isSupabaseConfigured || isSubmitting}
          onPress={() => void submitAccount()}
        />
      </Card>

      {!appConfig.isSupabaseConfigured ? (
        <Card>
          <StatusPill label="Configuration required" tone="warning" size="sm" />
          <Text variant="bodyBold">Set Supabase Auth values:</Text>
          <Text variant="caption" selectable>EXPO_PUBLIC_SUPABASE_URL=https://your-project.supabase.co</Text>
          <Text variant="caption" selectable>EXPO_PUBLIC_SUPABASE_ANON_KEY=your-publishable-or-anon-key</Text>
          <Divider />
          <Text variant="caption">
            On the API, set Authentication:Supabase:Url and validate the same Supabase project.
          </Text>
        </Card>
      ) : null}

      <Card>
        <View style={styles.localHeader}>
          <StatusPill label="Local mode" tone="success" size="sm" />
          <Text variant="bodyBold">Use without an account</Text>
        </View>
        <Text variant="caption">
          Keeps scouting, inventory, routes, budgets, and offline drafts usable with no signup.
        </Text>
        <Divider />
        <Button label="Continue Local" icon="phone-portrait-outline" variant="secondary" fullWidth onPress={() => void continueLocal()} />
      </Card>
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: {
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.lg,
  },
  accountMark: {
    width: 64,
    height: 64,
    borderRadius: 18,
    alignItems: 'center',
    justifyContent: 'center',
  },
  centerText: {
    textAlign: 'center',
  },
  modeRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  localHeader: {
    gap: spacing.sm,
  },
});
