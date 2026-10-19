import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Text } from '../../components/Text';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { photoOf, type AppraisalState } from './appraisalState';
import { CompList, SearchLinks, SourceReport } from './CompList';
import type { FailureRemedy, ScanFailure } from './failures';
import { IdentityCard } from './IdentityCard';
import { PriceSummary, PricingInProgress } from './PriceSummary';
import type { Identification } from './types';

export type ResultAction = {
  label: string;
  icon: keyof typeof Ionicons.glyphMap;
  onPress: () => void;
  disabled?: boolean;
};

type AppraisalViewProps = {
  state: Exclude<AppraisalState, { phase: 'ready' }>;
  primaryAction: ResultAction;
  secondaryActions: ResultAction[];
  /** Offered when nothing could be identified, so a find can still be recorded. */
  fallbackAction?: ResultAction;
  onCorrect: (item: Identification) => void;
  onRemedy: (remedy: FailureRemedy) => void;
  onNewScan: () => void;
  onClose: () => void;
};

export function AppraisalView({
  state,
  primaryAction,
  secondaryActions,
  fallbackAction,
  onCorrect,
  onRemedy,
  onNewScan,
  onClose,
}: AppraisalViewProps) {
  const { colors: c } = useAppTheme();
  const insets = useSafeAreaInsets();
  const photoUri = photoOf(state);
  const settled = state.phase === 'appraised' || state.phase === 'unpriced';

  return (
    <View style={[styles.fill, { backgroundColor: c.background }]}>
      <View style={[styles.header, { paddingTop: insets.top + spacing.sm, borderBottomColor: c.borderLight, backgroundColor: c.surface }]}>
        <Pressable onPress={onClose} hitSlop={8} accessibilityRole="button" accessibilityLabel="Close" style={styles.headerButton}>
          <Ionicons name="close" size={24} color={c.text} />
        </Pressable>
        <Text variant="bodyBold">{headline(state)}</Text>
        <Pressable onPress={onNewScan} hitSlop={8} accessibilityRole="button" style={styles.headerButton}>
          <Text variant="bodyBold" color={c.primary}>New scan</Text>
        </Pressable>
      </View>

      <ScrollView contentContainerStyle={[styles.content, { paddingBottom: insets.bottom + spacing.xxl }]}>
        {state.phase === 'identifying' || state.phase === 'unidentified' ? (
          <Card>
            <View style={styles.row}>
              {photoUri ? <Image source={{ uri: photoUri }} style={styles.thumb} contentFit="cover" /> : null}
              <View style={styles.flex}>
                <Text variant="bodyBold">{describeInput(state.input)}</Text>
                {state.phase === 'identifying' ? (
                  <View style={styles.row}>
                    <ActivityIndicator color={c.primary} size="small" />
                    <Text variant="caption">Identifying…</Text>
                  </View>
                ) : null}
              </View>
            </View>
          </Card>
        ) : (
          <IdentityCard item={state.item} photoUri={photoUri} onCorrect={settled ? onCorrect : undefined} />
        )}

        {state.phase === 'unidentified' ? <FailureCard failure={state.failure} onRemedy={onRemedy} /> : null}
        {state.phase === 'unidentified' && fallbackAction ? (
          <Button label={fallbackAction.label} icon={fallbackAction.icon} variant="ghost" fullWidth onPress={fallbackAction.onPress} />
        ) : null}
        {state.phase === 'appraising' ? <PricingInProgress /> : null}
        {state.phase === 'unpriced' ? <FailureCard failure={state.failure} onRemedy={onRemedy} /> : null}
        {state.phase === 'appraised' ? <PriceSummary appraisal={state.appraisal} /> : null}

        {settled ? (
          <View style={styles.actions}>
            <Button label={primaryAction.label} icon={primaryAction.icon} size="lg" fullWidth disabled={primaryAction.disabled} onPress={primaryAction.onPress} />
            {secondaryActions.map((action) => (
              <Button key={action.label} label={action.label} icon={action.icon} variant="secondary" fullWidth disabled={action.disabled} onPress={action.onPress} />
            ))}
          </View>
        ) : null}

        {state.phase === 'appraised' ? (
          <>
            <CompList comps={state.appraisal.comps} />
            <SourceReport appraisal={state.appraisal} />
            <SearchLinks appraisal={state.appraisal} />
          </>
        ) : null}
      </ScrollView>
    </View>
  );
}

const remedyButtons: Record<FailureRemedy, { label: string; icon: ResultAction['icon'] }> = {
  retry: { label: 'Try again', icon: 'refresh' },
  wait: { label: 'Try again', icon: 'refresh' },
  photo: { label: 'Take a photo', icon: 'camera-outline' },
  details: { label: 'Type what it is', icon: 'create-outline' },
  signIn: { label: 'Sign in', icon: 'log-in-outline' },
};

function FailureCard({ failure, onRemedy }: { failure: ScanFailure; onRemedy: (remedy: FailureRemedy) => void }) {
  const { colors: c } = useAppTheme();
  const remedies = [...new Set(failure.remedies.map((remedy) => (remedy === 'wait' ? 'retry' : remedy)))];

  return (
    <Card style={{ borderColor: c.warning }}>
      <Text variant="bodyBold">{failure.title}</Text>
      <Text variant="caption">{failure.message}</Text>
      {remedies.length > 0 ? (
        <View style={styles.remedies}>
          {remedies.map((remedy) => (
            <Button
              key={remedy}
              label={remedyButtons[remedy].label}
              icon={remedyButtons[remedy].icon}
              variant="secondary"
              size="sm"
              onPress={() => onRemedy(remedy)}
            />
          ))}
        </View>
      ) : null}
    </Card>
  );
}

function headline(state: AppraisalViewProps['state']): string {
  switch (state.phase) {
    case 'identifying':
      return 'Identifying';
    case 'unidentified':
      return 'Not identified';
    case 'appraising':
      return 'Pricing';
    case 'appraised':
      return 'Appraisal';
    case 'unpriced':
      return 'Not priced';
  }
}

function describeInput(input: AppraisalViewProps['state']['input']): string {
  switch (input.kind) {
    case 'barcode':
      return `Barcode ${input.barcode}`;
    case 'photo':
      return input.details ? 'Photo and your notes' : 'Photo';
    case 'details':
      return `“${input.details.length > 80 ? `${input.details.slice(0, 80)}…` : input.details}”`;
  }
}

const styles = StyleSheet.create({
  fill: {
    flex: 1,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.md,
    paddingBottom: spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  headerButton: {
    minWidth: 44,
    minHeight: 44,
    justifyContent: 'center',
    alignItems: 'center',
  },
  content: {
    padding: spacing.lg,
    gap: spacing.md,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
    gap: spacing.xs,
  },
  thumb: {
    width: 84,
    height: 84,
    borderRadius: radius.md,
  },
  actions: {
    gap: spacing.sm,
  },
  remedies: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginTop: spacing.xs,
  },
});
