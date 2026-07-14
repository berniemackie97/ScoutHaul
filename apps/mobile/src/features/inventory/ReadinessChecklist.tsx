import { Ionicons } from '@expo/vector-icons';
import { StyleSheet, View } from 'react-native';
import { Text } from '../../components/Text';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { readinessRequirements, type InventoryReadinessFacts } from '../../domain/lifecycleReadiness';
import type { InventoryStatus } from '../../domain/types';

export function ReadinessChecklist({ targetStatus, facts }: { targetStatus: InventoryStatus; facts: InventoryReadinessFacts }) {
  const { colors: c } = useAppTheme();
  const requirements = readinessRequirements(targetStatus, facts);
  const blockers = requirements.filter((requirement) => requirement.severity === 'blocker' && !requirement.met);

  if (requirements.length === 0) return null;

  return (
    <View style={[styles.card, { borderColor: c.borderLight, backgroundColor: c.surfaceAlt }]}>
      <View style={styles.header}>
        <Text variant="bodyBold">Readiness requirements</Text>
        <Text variant="caption" color={blockers.length > 0 ? c.danger : c.success}>
          {blockers.length > 0 ? `${blockers.length} blocker${blockers.length === 1 ? '' : 's'}` : 'No blockers'}
        </Text>
      </View>
      {requirements.map((requirement) => {
        const icon = requirement.met ? 'checkmark-circle' : requirement.severity === 'blocker' ? 'alert-circle' : 'ellipse-outline';
        const color = requirement.met ? c.success : requirement.severity === 'blocker' ? c.danger : c.warning;
        return (
          <View key={requirement.id} style={styles.row}>
            <Ionicons name={icon} size={16} color={color} />
            <Text variant="caption" color={requirement.met ? c.textMuted : c.text}>
              {requirement.label}
            </Text>
          </View>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.md,
    borderWidth: 1,
    padding: spacing.md,
    gap: spacing.sm,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
});
