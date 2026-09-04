import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useAppTheme } from '../../context/ThemeContext';

/** One dot in the scan progress row: upcoming, in progress, or done. */
export function StepDot({ active, done, color }: { active: boolean; done: boolean; color: string }) {
  const { colors: c } = useAppTheme();
  return (
    <View
      style={[
        stepStyles.dot,
        done && { backgroundColor: color, borderColor: color },
        active && { borderColor: color, borderWidth: 2.5 },
        !done && !active && { borderColor: c.borderLight },
      ]}
    >
      {done ? <Ionicons name="checkmark" size={10} color="#fff" /> : null}
      {active ? <View style={[stepStyles.dotInner, { backgroundColor: color }]} /> : null}
    </View>
  );
}

const stepStyles = StyleSheet.create({
  dot: {
    width: 22,
    height: 22,
    borderRadius: 11,
    borderWidth: 2,
    alignItems: 'center',
    justifyContent: 'center',
  },
  dotInner: {
    width: 8,
    height: 8,
    borderRadius: 4,
  },
});
