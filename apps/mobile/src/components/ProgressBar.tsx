import { StyleSheet, View } from 'react-native';
import { radius } from './theme';
import { useColors } from '../context/ThemeContext';

type ProgressBarProps = {
  progress: number;
  color?: string;
  trackColor?: string;
  height?: number;
};

export function ProgressBar({
  progress,
  color,
  trackColor,
  height = 8,
}: ProgressBarProps) {
  const c = useColors();
  const fillColor = color ?? c.primary;
  const bgColor = trackColor ?? c.surfaceAlt;
  const clamped = Math.max(0, Math.min(1, progress));

  return (
    <View style={[styles.track, { backgroundColor: bgColor, height, borderRadius: height / 2 }]}>
      <View
        style={[
          styles.fill,
          {
            backgroundColor: fillColor,
            width: `${clamped * 100}%`,
            height,
            borderRadius: height / 2,
          },
        ]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  track: {
    width: '100%',
    overflow: 'hidden',
    borderRadius: radius.full,
  },
  fill: {
    borderRadius: radius.full,
  },
});
