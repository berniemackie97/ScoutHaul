import { StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Text } from '../../components/Text';
import { spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';
import type { SourcingRouteDetail } from '../../domain/types';
import { formatMoney } from '../../utils/money';

type RouteSummaryCardProps = {
  route: SourcingRouteDetail;
  onComplete: () => void;
};

export function RouteSummaryCard({ route, onComplete }: RouteSummaryCardProps) {
  const c = useColors();

  return (
    <Card variant="elevated">
      <Text variant="subtitle">Route Summary</Text>
      <Row label="Completed stops" value={`${route.summary.completedStopCount} of ${route.summary.stopCount}`} />
      <Row label="Skipped stops" value={`${route.summary.skippedStopCount}`} />
      <Row label="Planned vs actual" value={`${route.summary.plannedMinutes} min / ${route.summary.actualMinutes} min`} />
      <Row label="Schedule variance" value={`${route.summary.scheduleVarianceMinutes >= 0 ? '+' : ''}${route.summary.scheduleVarianceMinutes} min`} />
      <Row label="Total spend" value={formatMoney(route.summary.totalPurchaseCost)} />
      <Row label="Remaining budget" value={formatMoney(route.summary.remainingBudget)} />
      <Row label="Purchases" value={`${route.summary.purchaseCount}`} />
      <Text variant="caption" color={c.textMuted}>
        Follow-up tasks and inventory work remain available from Tasks and Inventory.
      </Text>
      {route.trip.status !== 'Completed' ? (
        <Button label="Complete Route" icon="checkmark-done-outline" onPress={onComplete} />
      ) : null}
    </Card>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.row}>
      <Text variant="caption">{label}</Text>
      <Text variant="bodyBold">{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: spacing.xs,
  },
});
