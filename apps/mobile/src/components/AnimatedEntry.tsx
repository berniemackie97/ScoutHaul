import { PropsWithChildren, useEffect, useRef } from 'react';
import { Animated, ViewStyle } from 'react-native';

type AnimatedEntryProps = PropsWithChildren<{
  delay?: number;
  duration?: number;
  translateY?: number;
  style?: ViewStyle;
}>;

export function AnimatedEntry({
  delay = 0,
  duration = 350,
  translateY = 12,
  style,
  children,
}: AnimatedEntryProps) {
  const opacity = useRef(new Animated.Value(0)).current;
  const offset = useRef(new Animated.Value(translateY)).current;

  useEffect(() => {
    Animated.parallel([
      Animated.timing(opacity, {
        toValue: 1,
        duration,
        delay,
        useNativeDriver: true,
      }),
      Animated.timing(offset, {
        toValue: 0,
        duration,
        delay,
        useNativeDriver: true,
      }),
    ]).start();
  }, []);

  return (
    <Animated.View style={[style, { opacity, transform: [{ translateY: offset }] }]}>
      {children}
    </Animated.View>
  );
}
