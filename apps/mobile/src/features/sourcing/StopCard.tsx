import { Ionicons } from '@expo/vector-icons';
import { Pressable, StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Divider } from '../../components/Divider';
import { ProgressBar } from '../../components/ProgressBar';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';
import { formatMinutes, sourceTypeLabel } from '../../domain/routes';
import type { SourcingTripStop } from '../../domain/types';

type StopCardProps = {
  stop: SourcingTripStop;
  now: Date;
  onNavigate: (provider: 'google' | 'apple') => void;
  onArrive: () => void;
  onComplete: (force?: boolean) => void;
  onSkip: () => void;
};

export function CurrentStopCard({ stop, now, onNavigate, onArrive, onComplete, onSkip }: StopCardProps) {
  const c = useColors();

  return (
    <Card variant="elevated" style={styles.card}>
      <View style={styles.header}>
        <View style={[styles.stopNumber, { backgroundColor: c.info }]}>
          <Text variant="bodyBold" color={c.textInverse}>{stop.plannedOrder}</Text>
        </View>
        <View style={styles.title}>
          <Text variant="label" color={c.textMuted}>CURRENT STOP</Text>
          <Text variant="subtitle" numberOfLines={2}>{stop.name}</Text>
          <Text variant="caption" color={c.textMuted} numberOfLines={2}>{stop.address}</Text>
        </View>
        <StatusPill label={stop.status} tone={stop.status === 'InProgress' ? 'success' : 'primary'} size="sm" />
      </View>

      <View style={styles.meta}>
        <MetaItem icon="pricetag-outline" label={sourceTypeLabel(stop.sourceType)} />
        <MetaItem icon="timer-outline" label={`${stop.estimatedDurationMinutes} min`} />
        <MetaItem icon="flag-outline" label={stop.priority} />
        {stop.isTimeSensitive ? <MetaItem icon="alarm-outline" label="Time-sensitive" tone={c.warning} /> : null}
      </View>

      {stop.status === 'InProgress' ? <TimerBlock stop={stop} now={now} /> : null}

      <View style={styles.actions}>
        {stop.status === 'InProgress' ? (
          <Button label="Complete Stop" icon="checkmark-circle-outline" size="lg" fullWidth onPress={() => onComplete()} />
        ) : stop.status === 'Navigating' ? (
          <Button label="I'm Here" icon="location-outline" size="lg" fullWidth onPress={onArrive} />
        ) : (
          <>
            <Button label="Navigate" icon="navigate-outline" size="lg" fullWidth onPress={() => onNavigate('google')} />
            <View style={styles.mapRow}>
              <Button label="Google Maps" icon="map-outline" variant="secondary" onPress={() => onNavigate('google')} />
              <Button label="Apple Maps" icon="map-outline" variant="secondary" onPress={() => onNavigate('apple')} />
            </View>
          </>
        )}
        {stop.canBeSkipped && stop.status !== 'InProgress' ? (
          <Button label="Skip Optional Stop" icon="play-skip-forward-outline" variant="ghost" onPress={onSkip} />
        ) : null}
      </View>
    </Card>
  );
}

export function ChecklistCard({ stop, onToggle }: { stop: SourcingTripStop; onToggle: (id: string, value: boolean) => void }) {
  const c = useColors();
  const completed = stop.checklistItems.filter((item) => item.isCompleted).length;
  const progress = stop.checklistItems.length > 0 ? completed / stop.checklistItems.length : 1;

  return (
    <Card>
      <View style={styles.checklistHeader}>
        <View>
          <Text variant="label">Checklist</Text>
          <Text variant="caption" color={c.textMuted}>{completed} of {stop.checklistItems.length} complete</Text>
        </View>
        <StatusPill label={`${Math.round(progress * 100)}%`} tone={progress === 1 ? 'success' : 'neutral'} size="sm" />
      </View>
      <ProgressBar progress={progress} color={progress === 1 ? c.success : c.primary} height={6} />
      {stop.checklistItems.map((item, index) => (
        <View key={item.id}>
          {index > 0 ? <Divider /> : null}
          <Pressable
            onPress={() => onToggle(item.id, !item.isCompleted)}
            accessibilityRole="checkbox"
            accessibilityState={{ checked: item.isCompleted }}
            style={styles.checklistRow}
          >
            <Ionicons name={item.isCompleted ? 'checkmark-circle' : 'ellipse-outline'} size={22} color={item.isCompleted ? c.success : c.textMuted} />
            <Text variant="body" style={styles.checkText}>{item.text}</Text>
            {item.isRequired ? <Text variant="label" color={c.warning}>Required</Text> : null}
          </Pressable>
        </View>
      ))}
    </Card>
  );
}

function TimerBlock({ stop, now }: { stop: SourcingTripStop; now: Date }) {
  const c = useColors();
  const arrived = stop.actualArrivalAt ? new Date(stop.actualArrivalAt) : now;
  const elapsedMinutes = Math.max(0, (now.getTime() - arrived.getTime()) / 60000);
  const remaining = stop.estimatedDurationMinutes - elapsedMinutes;
  const isOver = remaining < 0;

  return (
    <View style={[styles.timerBlock, { backgroundColor: c.surfaceAlt }]}>
      <View>
        <Text variant="label">Elapsed</Text>
        <Text variant="metricSmall">{formatMinutes(elapsedMinutes)}</Text>
      </View>
      <View style={styles.timerRight}>
        <Text variant="label">{isOver ? 'Over Time' : 'Remaining'}</Text>
        <Text variant="metricSmall" color={isOver ? c.danger : c.success}>{formatMinutes(remaining)}</Text>
      </View>
    </View>
  );
}

function MetaItem({ icon, label, tone }: { icon: keyof typeof Ionicons.glyphMap; label: string; tone?: string }) {
  const c = useColors();
  return (
    <View style={styles.metaItem}>
      <Ionicons name={icon} size={14} color={tone ?? c.textMuted} />
      <Text variant="caption" color={tone ?? c.textMuted}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  card: { gap: spacing.md },
  header: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.md },
  stopNumber: { width: 40, height: 40, borderRadius: 20, alignItems: 'center', justifyContent: 'center' },
  title: { flex: 1, gap: spacing.xxs },
  meta: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  metaItem: { flexDirection: 'row', alignItems: 'center', gap: spacing.xxs },
  timerBlock: { flexDirection: 'row', justifyContent: 'space-between', borderRadius: 8, padding: spacing.md },
  timerRight: { alignItems: 'flex-end' },
  actions: { gap: spacing.sm },
  mapRow: { flexDirection: 'row', gap: spacing.sm },
  checklistHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  checklistRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingVertical: spacing.sm },
  checkText: { flex: 1 },
});
