import { Ionicons } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Divider } from '../src/components/Divider';
import { ProgressBar } from '../src/components/ProgressBar';
import { Screen } from '../src/components/Screen';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing, type ToneKey } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import { saveWorkspaceProfile, type WorkspacePlanIntent, type WorkspaceUsageMode } from '../src/db/localStore';
import { parseOptionalNumber } from '../src/utils/money';

type Role = 'owner' | 'operator' | 'employee';
type PrimaryGoal = 'source' | 'inventory' | 'listing' | 'profit';

const steps = ['Welcome', 'Workspace', 'Business', 'Privacy'];

const usageOptions: { value: WorkspaceUsageMode; label: string; icon: keyof typeof Ionicons.glyphMap }[] = [
  { value: 'solo', label: 'Solo', icon: 'person-outline' },
  { value: 'team', label: 'Team', icon: 'people-outline' },
];

const roleOptions: { value: Role; label: string }[] = [
  { value: 'owner', label: 'Owner' },
  { value: 'operator', label: 'Operator' },
  { value: 'employee', label: 'Employee' },
];

const goalOptions: { value: PrimaryGoal; label: string; icon: keyof typeof Ionicons.glyphMap; tone: ToneKey }[] = [
  { value: 'source', label: 'Source better', icon: 'navigate-outline', tone: 'primary' },
  { value: 'inventory', label: 'Control inventory', icon: 'cube-outline', tone: 'info' },
  { value: 'listing', label: 'List faster', icon: 'pricetag-outline', tone: 'warning' },
  { value: 'profit', label: 'Track profit', icon: 'trending-up-outline', tone: 'success' },
];

const planOptions: { value: WorkspacePlanIntent; label: string; subtitle: string }[] = [
  { value: 'starter', label: 'Starter', subtitle: 'Local workflow' },
  { value: 'pro', label: 'Pro', subtitle: 'Advanced solo' },
  { value: 'team', label: 'Team', subtitle: 'Shared workspace' },
];

export default function OnboardingScreen() {
  const { colors: c } = useAppTheme();
  const [step, setStep] = useState(0);
  const [workspaceName, setWorkspaceName] = useState('My resale workspace');
  const [usageMode, setUsageMode] = useState<WorkspaceUsageMode>('solo');
  const [role, setRole] = useState<Role>('owner');
  const [monthlyBudget, setMonthlyBudget] = useState('');
  const [primaryGoal, setPrimaryGoal] = useState<PrimaryGoal>('source');
  const [planIntent, setPlanIntent] = useState<WorkspacePlanIntent>('starter');
  const [analyticsConsent, setAnalyticsConsent] = useState(false);
  const [marketingConsent, setMarketingConsent] = useState(false);
  const [isSaving, setIsSaving] = useState(false);

  async function finish(skipped = false) {
    setIsSaving(true);
    await saveWorkspaceProfile({
      onboardingCompleted: true,
      onboardingSkipped: skipped,
      workspaceName: skipped ? 'My resale workspace' : workspaceName,
      usageMode: skipped ? 'solo' : usageMode,
      role: skipped ? 'owner' : role,
      monthlySourcingBudget: skipped ? null : parseOptionalNumber(monthlyBudget),
      primaryGoal: skipped ? 'source' : primaryGoal,
      planIntent: skipped ? 'starter' : planIntent,
      analyticsConsent: skipped ? false : analyticsConsent,
      marketingConsent: skipped ? false : marketingConsent,
    });
    setIsSaving(false);
    router.replace('/(tabs)');
  }

  return (
    <Screen edges={['top', 'left', 'right', 'bottom']}>
      <View style={styles.header}>
        <View style={styles.brandMark}>
          <Ionicons name="trail-sign-outline" size={26} color={c.textInverse} />
        </View>
        <View style={styles.headerText}>
          <Text variant="title">Set up ScoutHaul</Text>
          <Text variant="caption" color={c.textMuted}>Workspace-first, account-optional.</Text>
        </View>
        <Pressable accessibilityRole="button" accessibilityLabel="Continue local" onPress={() => void finish(true)} hitSlop={8}>
          <Text variant="caption" color={c.primary}>Local</Text>
        </Pressable>
      </View>

      <View style={styles.progressBlock}>
        <ProgressBar progress={(step + 1) / steps.length} height={8} />
        <Text variant="caption" color={c.textMuted}>{steps[step]} · {step + 1} of {steps.length}</Text>
      </View>

      {step === 0 ? (
        <WelcomeStep onLocal={() => void finish(true)} />
      ) : step === 1 ? (
        <WorkspaceStep
          workspaceName={workspaceName}
          onWorkspaceName={setWorkspaceName}
          usageMode={usageMode}
          onUsageMode={setUsageMode}
          role={role}
          onRole={setRole}
        />
      ) : step === 2 ? (
        <BusinessStep
          monthlyBudget={monthlyBudget}
          onMonthlyBudget={setMonthlyBudget}
          primaryGoal={primaryGoal}
          onPrimaryGoal={setPrimaryGoal}
          planIntent={planIntent}
          onPlanIntent={setPlanIntent}
        />
      ) : (
        <PrivacyStep
          analyticsConsent={analyticsConsent}
          onAnalyticsConsent={setAnalyticsConsent}
          marketingConsent={marketingConsent}
          onMarketingConsent={setMarketingConsent}
        />
      )}

      <View style={styles.footer}>
        {step > 0 ? (
          <Button label="Back" icon="chevron-back-outline" variant="secondary" onPress={() => setStep((value) => Math.max(0, value - 1))} />
        ) : null}
        <Button
          label={step === steps.length - 1 ? isSaving ? 'Saving...' : 'Finish Setup' : 'Continue'}
          icon={step === steps.length - 1 ? 'checkmark-circle-outline' : 'chevron-forward-outline'}
          fullWidth={step === 0}
          disabled={isSaving}
          onPress={step === steps.length - 1 ? () => void finish(false) : () => setStep((value) => Math.min(steps.length - 1, value + 1))}
        />
      </View>
    </Screen>
  );
}

function WelcomeStep({ onLocal }: { onLocal: () => void }) {
  const { colors: c } = useAppTheme();
  return (
    <>
      <View style={[styles.heroPanel, { backgroundColor: c.surface }]}>
        <StatusPill label="Workspace operating system" tone="primary" />
        <Text variant="hero">Run sourcing, inventory, listings, and budgets from one workspace.</Text>
        <Text variant="body" color={c.textSecondary}>
          Set up a professional resale command center now. Sync, teams, billing, and account security are designed as first-class paths, while local mode stays available.
        </Text>
        <View style={styles.ctaStack}>
          <Button label="Create Free Account" icon="person-add-outline" size="lg" fullWidth onPress={() => router.push({ pathname: '/account', params: { mode: 'signup' } })} />
          <Button label="Sign In" icon="log-in-outline" variant="secondary" fullWidth onPress={() => router.push({ pathname: '/account', params: { mode: 'signin' } })} />
          <Button label="Continue Local" icon="phone-portrait-outline" variant="ghost" fullWidth onPress={onLocal} />
        </View>
      </View>
      <Card>
        <Text variant="bodyBold">Built for solo operators and teams.</Text>
        <View style={styles.valueRows}>
          <ValueRow icon="business-outline" title="Workspace state" text="Profile, team intent, plan fit, and budget defaults persist locally." />
          <ValueRow icon="shield-checkmark-outline" title="Consent-first data" text="Analytics and marketing data sharing are opt-in, not hidden in setup." />
          <ValueRow icon="card-outline" title="Monetization-ready" text="Plan intent, team mode, account CTAs, and budget usage are ready for billing hooks." />
        </View>
      </Card>
    </>
  );
}

function ValueRow({ icon, title, text }: {
  icon: keyof typeof Ionicons.glyphMap;
  title: string;
  text: string;
}) {
  const { colors: c } = useAppTheme();
  return (
    <View style={styles.valueRow}>
      <Ionicons name={icon} size={20} color={c.primary} />
      <View style={styles.valueText}>
        <Text variant="bodyBold">{title}</Text>
        <Text variant="caption">{text}</Text>
      </View>
    </View>
  );
}

function WorkspaceStep({
  workspaceName,
  onWorkspaceName,
  usageMode,
  onUsageMode,
  role,
  onRole,
}: {
  workspaceName: string;
  onWorkspaceName: (value: string) => void;
  usageMode: WorkspaceUsageMode;
  onUsageMode: (value: WorkspaceUsageMode) => void;
  role: Role;
  onRole: (value: Role) => void;
}) {
  return (
    <Card>
      <Text variant="subtitle">Workspace</Text>
      <TextField label="Workspace name" value={workspaceName} onChangeText={onWorkspaceName} placeholder="Acme Resale" />
      <View style={styles.optionGrid}>
        {usageOptions.map((option) => (
          <OptionCard
            key={option.value}
            icon={option.icon}
            label={option.label}
            selected={usageMode === option.value}
            onPress={() => onUsageMode(option.value)}
          />
        ))}
      </View>
      <SegmentedOptions options={roleOptions} value={role} onChange={onRole} />
    </Card>
  );
}

function BusinessStep({
  monthlyBudget,
  onMonthlyBudget,
  primaryGoal,
  onPrimaryGoal,
  planIntent,
  onPlanIntent,
}: {
  monthlyBudget: string;
  onMonthlyBudget: (value: string) => void;
  primaryGoal: PrimaryGoal;
  onPrimaryGoal: (value: PrimaryGoal) => void;
  planIntent: WorkspacePlanIntent;
  onPlanIntent: (value: WorkspacePlanIntent) => void;
}) {
  const { colors: c } = useAppTheme();
  return (
    <>
      <Card>
        <Text variant="subtitle">Business Defaults</Text>
        <TextField
          label="Monthly sourcing budget"
          value={monthlyBudget}
          onChangeText={onMonthlyBudget}
          keyboardType="decimal-pad"
          prefix="$"
          placeholder="0.00"
        />
        <Text variant="label">Primary goal</Text>
        <View style={styles.goalGrid}>
          {goalOptions.map((option) => (
            <GoalCard
              key={option.value}
              icon={option.icon}
              label={option.label}
              tone={option.tone}
              selected={primaryGoal === option.value}
              onPress={() => onPrimaryGoal(option.value)}
            />
          ))}
        </View>
      </Card>
      <Card>
        <Text variant="subtitle">Plan Fit</Text>
        <View style={styles.planGrid}>
          {planOptions.map((option) => {
            const selected = planIntent === option.value;
            return (
              <Pressable
                key={option.value}
                accessibilityRole="button"
                accessibilityState={{ selected }}
                accessibilityLabel={`Plan fit ${option.label}`}
                onPress={() => onPlanIntent(option.value)}
                style={[
                  styles.planCard,
                  {
                    backgroundColor: selected ? c.primaryLight : c.surfaceAlt,
                    borderColor: selected ? c.primary : c.border,
                  },
                ]}
              >
                <Text variant="bodyBold" color={selected ? c.primary : c.text}>{option.label}</Text>
                <Text variant="caption">{option.subtitle}</Text>
              </Pressable>
            );
          })}
        </View>
      </Card>
    </>
  );
}

function PrivacyStep({
  analyticsConsent,
  onAnalyticsConsent,
  marketingConsent,
  onMarketingConsent,
}: {
  analyticsConsent: boolean;
  onAnalyticsConsent: (value: boolean) => void;
  marketingConsent: boolean;
  onMarketingConsent: (value: boolean) => void;
}) {
  return (
    <Card>
      <Text variant="subtitle">Data Controls</Text>
      <Text variant="body">
        Private mode is the default. Future analytics and marketing data sharing must be explicit, revocable, and separated from core app use.
      </Text>
      <Divider />
      <ConsentRow
        title="Share anonymized business insights"
        subtitle="Helps benchmark sourcing categories, sell-through trends, and route performance."
        value={analyticsConsent}
        onChange={onAnalyticsConsent}
      />
      <Divider />
      <ConsentRow
        title="Product updates and offers"
        subtitle="Allows launch updates, pricing research, and optional partner offers."
        value={marketingConsent}
        onChange={onMarketingConsent}
      />
    </Card>
  );
}

function ConsentRow({
  title,
  subtitle,
  value,
  onChange,
}: {
  title: string;
  subtitle: string;
  value: boolean;
  onChange: (value: boolean) => void;
}) {
  const { colors: c } = useAppTheme();
  return (
    <Pressable
      accessibilityRole="switch"
      accessibilityState={{ checked: value }}
      accessibilityLabel={title}
      onPress={() => onChange(!value)}
      style={styles.consentRow}
    >
      <View style={styles.consentText}>
        <Text variant="bodyBold">{title}</Text>
        <Text variant="caption">{subtitle}</Text>
      </View>
      <View style={[styles.switchTrack, { backgroundColor: value ? c.primary : c.border }]}>
        <View style={[styles.switchThumb, value && styles.switchThumbOn]} />
      </View>
    </Pressable>
  );
}

function OptionCard({ icon, label, selected, onPress }: {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  selected: boolean;
  onPress: () => void;
}) {
  const { colors: c } = useAppTheme();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected }}
      accessibilityLabel={label}
      onPress={onPress}
      style={[
        styles.optionCard,
        {
          backgroundColor: selected ? c.primaryLight : c.surfaceAlt,
          borderColor: selected ? c.primary : c.border,
        },
      ]}
    >
      <Ionicons name={icon} size={22} color={selected ? c.primary : c.textMuted} />
      <Text variant="bodyBold" color={selected ? c.primary : c.text}>{label}</Text>
    </Pressable>
  );
}

function GoalCard({ icon, label, tone, selected, onPress }: {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  tone: ToneKey;
  selected: boolean;
  onPress: () => void;
}) {
  const { colors: c, tones } = useAppTheme();
  const t = tones[tone];
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected }}
      accessibilityLabel={label}
      onPress={onPress}
      style={[
        styles.goalCard,
        {
          backgroundColor: selected ? t.bg : c.surfaceAlt,
          borderColor: selected ? t.border : c.border,
        },
      ]}
    >
      <Ionicons name={icon} size={20} color={selected ? t.text : c.textMuted} />
      <Text variant="caption" color={selected ? t.text : c.textSecondary}>{label}</Text>
    </Pressable>
  );
}

function SegmentedOptions<T extends string>({
  options,
  value,
  onChange,
}: {
  options: { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
}) {
  const { colors: c } = useAppTheme();
  return (
    <View style={styles.chips}>
      {options.map((option) => {
        const selected = value === option.value;
        return (
          <Pressable
            key={option.value}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            accessibilityLabel={option.label}
            onPress={() => onChange(option.value)}
            style={[
              styles.chip,
              {
                backgroundColor: selected ? c.primary : c.surfaceAlt,
                borderColor: selected ? c.primary : c.border,
              },
            ]}
          >
            <Text variant="caption" color={selected ? c.textInverse : c.textSecondary}>{option.label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  brandMark: {
    width: 48,
    height: 48,
    borderRadius: 16,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: '#0B6B5B',
  },
  headerText: {
    flex: 1,
    gap: spacing.xs,
  },
  progressBlock: {
    gap: spacing.xs,
  },
  footer: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: 'auto',
  },
  heroPanel: {
    borderRadius: radius.xl,
    padding: spacing.xl,
    gap: spacing.lg,
    borderWidth: 1,
    borderColor: 'rgba(148, 163, 184, 0.25)',
  },
  ctaStack: {
    gap: spacing.sm,
  },
  valueRows: {
    gap: spacing.md,
  },
  valueRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  valueText: {
    flex: 1,
    gap: spacing.xs,
  },
  optionGrid: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  optionCard: {
    flex: 1,
    alignItems: 'center',
    gap: spacing.sm,
    borderWidth: 1,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  chip: {
    borderWidth: 1,
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  goalGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  goalCard: {
    width: '48%',
    minHeight: 72,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xs,
    borderWidth: 1,
    borderRadius: radius.lg,
    padding: spacing.md,
  },
  planGrid: {
    gap: spacing.sm,
  },
  planCard: {
    borderWidth: 1,
    borderRadius: radius.lg,
    padding: spacing.md,
    gap: spacing.xs,
  },
  consentRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  consentText: {
    flex: 1,
    gap: spacing.xs,
  },
  switchTrack: {
    width: 48,
    height: 28,
    borderRadius: 14,
    justifyContent: 'center',
    paddingHorizontal: 3,
  },
  switchThumb: {
    width: 22,
    height: 22,
    borderRadius: 11,
    backgroundColor: '#fff',
  },
  switchThumbOn: {
    alignSelf: 'flex-end',
  },
});
