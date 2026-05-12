import { StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { spacing, type ColorPalette } from './theme';
import { useColors } from '../context/ThemeContext';

type ConfidenceRingProps = {
  score: number;
  size?: number;
};

function scoreColor(score: number, c: ColorPalette): string {
  if (score >= 70) return c.success;
  if (score >= 40) return c.warning;
  return c.danger;
}

function scoreLabel(score: number): string {
  if (score >= 70) return 'High';
  if (score >= 40) return 'Medium';
  return 'Low';
}

export function ConfidenceRing({ score, size = 56 }: ConfidenceRingProps) {
  const c = useColors();
  const ringColor = scoreColor(score, c);
  const borderWidth = 4;

  return (
    <View style={styles.container}>
      <View
        style={[
          styles.ring,
          {
            width: size,
            height: size,
            borderRadius: size / 2,
            borderWidth,
            borderColor: ringColor,
          },
        ]}
      >
        <Text variant="bodyBold" color={ringColor}>
          {score}
        </Text>
      </View>
      <Text variant="label" color={ringColor}>
        {scoreLabel(score)}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    alignItems: 'center',
    gap: spacing.xs,
  },
  ring: {
    alignItems: 'center',
    justifyContent: 'center',
  },
});
