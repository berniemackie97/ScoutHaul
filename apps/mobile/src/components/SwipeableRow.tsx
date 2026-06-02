import { Ionicons } from '@expo/vector-icons';
import { PropsWithChildren, useRef } from 'react';
import { Animated, PanResponder, Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';
import { useHaptics } from '../hooks/useHaptics';

type SwipeAction = {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  color: string;
  onPress: () => void;
};

type SwipeableRowProps = PropsWithChildren<{
  leftAction?: SwipeAction;
  rightAction?: SwipeAction;
}>;

const THRESHOLD = 80;

export function SwipeableRow({ children, leftAction, rightAction }: SwipeableRowProps) {
  const c = useColors();
  const haptics = useHaptics();
  const translateX = useRef(new Animated.Value(0)).current;

  const panResponder = useRef(
    PanResponder.create({
      onMoveShouldSetPanResponder: (_, gesture) => Math.abs(gesture.dx) > 10 && Math.abs(gesture.dy) < 10,
      onPanResponderMove: (_, gesture) => {
        let dx = gesture.dx;
        if (!leftAction && dx > 0) dx = 0;
        if (!rightAction && dx < 0) dx = 0;
        dx = Math.max(-THRESHOLD, Math.min(THRESHOLD, dx));
        translateX.setValue(dx);
      },
      onPanResponderRelease: (_, gesture) => {
        const action = gesture.dx > THRESHOLD / 2 ? leftAction : gesture.dx < -THRESHOLD / 2 ? rightAction : null;
        if (action) haptics.medium();
        Animated.spring(translateX, { toValue: 0, useNativeDriver: true }).start(() => action?.onPress());
      },
      onPanResponderTerminate: () => {
        Animated.spring(translateX, { toValue: 0, useNativeDriver: true }).start();
      },
    }),
  ).current;

  function close() {
    Animated.spring(translateX, { toValue: 0, useNativeDriver: true }).start();
  }

  return (
    <View style={styles.container}>
      {leftAction ? (
        <Pressable
          onPress={() => { close(); leftAction.onPress(); }}
          style={[styles.action, styles.leftAction, { backgroundColor: leftAction.color }]}
          accessibilityLabel={leftAction.label}
          accessibilityRole="button"
        >
          <Ionicons name={leftAction.icon} size={22} color={c.textInverse} />
          <Text variant="caption" color={c.textInverse}>{leftAction.label}</Text>
        </Pressable>
      ) : null}
      {rightAction ? (
        <Pressable
          onPress={() => { close(); rightAction.onPress(); }}
          style={[styles.action, styles.rightAction, { backgroundColor: rightAction.color }]}
          accessibilityLabel={rightAction.label}
          accessibilityRole="button"
        >
          <Ionicons name={rightAction.icon} size={22} color={c.textInverse} />
          <Text variant="caption" color={c.textInverse}>{rightAction.label}</Text>
        </Pressable>
      ) : null}
      <Animated.View
        style={[styles.content, { transform: [{ translateX }], backgroundColor: c.surface }]}
        accessibilityHint={[leftAction && `Swipe right to ${leftAction.label}`, rightAction && `Swipe left to ${rightAction.label}`].filter(Boolean).join('. ')}
        {...panResponder.panHandlers}
      >
        {children}
      </Animated.View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    overflow: 'hidden',
    borderRadius: radius.lg,
  },
  action: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    width: THRESHOLD,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xxs,
  },
  leftAction: {
    left: 0,
  },
  rightAction: {
    right: 0,
  },
  content: {
    zIndex: 1,
  },
});
