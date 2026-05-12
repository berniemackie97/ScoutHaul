import { StyleSheet, View } from 'react-native';
import { spacing } from './theme';
import { useColors } from '../context/ThemeContext';

type DividerProps = {
  spacing?: number;
};

export function Divider({ spacing: gap = spacing.md }: DividerProps) {
  const c = useColors();
  return <View style={[styles.line, { marginVertical: gap, backgroundColor: c.border }]} />;
}

const styles = StyleSheet.create({
  line: {
    height: 1,
  },
});
