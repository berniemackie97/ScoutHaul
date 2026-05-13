import { Ionicons } from '@expo/vector-icons';
import { PropsWithChildren, useRef } from 'react';
import { Animated, PanResponder, Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';

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
  const translateX = useRef(new Animated.Value(0)).current;
  const lastOffset = useRef(0);
  const gestureStartOffset = useRef(0);

  const panResponder = useRef(
    PanResponder.create({
      onMoveShouldSetPanResponder: (_, gesture) => Math.abs(gesture.dx) > 10 && Math.abs(gesture.dy) < 10,
      onPanResponderGrant: () => {
        gestureStartOffset.current = lastOffset.current;
      },
      onPanResponderMove: (_, gesture) => {
        let dx = gestureStartOffset.current + gesture.dx;
        if (!leftAction && dx > 0) dx = 0;
        if (!rightAction && dx < 0) dx = 0;
        dx = Math.max(-THRESHOLD, Math.min(THRESHOLD, dx));
        translateX.setValue(dx);
      },
      onPanResponderRelease: (_, gesture) => {
        const nextOffset = gestureStartOffset.current + gesture.dx;
        if (nextOffset > THRESHOLD / 2 && leftAction) {
          Animated.spring(translateX, { toValue: THRESHOLD, useNativeDriver: true }).start();
          lastOffset.current = THRESHOLD;
        } else if (nextOffset < -THRESHOLD / 2 && rightAction) {
          Animated.spring(translateX, { toValue: -THRESHOLD, useNativeDriver: true }).start();
          lastOffset.current = -THRESHOLD;
        } else {
          Animated.spring(translateX, { toValue: 0, useNativeDriver: true }).start();
          lastOffset.current = 0;
        }
      },
      onPanResponderTerminate: () => {
        Animated.spring(translateX, { toValue: 0, useNativeDriver: true }).start();
        lastOffset.current = 0;
      },
    }),
  ).current;

  function close() {
    Animated.spring(translateX, { toValue: 0, useNativeDriver: true }).start();
    lastOffset.current = 0;
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
          <Ionicons name={leftAction.icon} size={22} color="#fff" />
          <Text variant="caption" color="#fff">{leftAction.label}</Text>
        </Pressable>
      ) : null}
      {rightAction ? (
        <Pressable
          onPress={() => { close(); rightAction.onPress(); }}
          style={[styles.action, styles.rightAction, { backgroundColor: rightAction.color }]}
          accessibilityLabel={rightAction.label}
          accessibilityRole="button"
        >
          <Ionicons name={rightAction.icon} size={22} color="#fff" />
          <Text variant="caption" color="#fff">{rightAction.label}</Text>
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
