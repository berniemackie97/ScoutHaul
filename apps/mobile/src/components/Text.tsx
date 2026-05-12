import { PropsWithChildren } from 'react';
import { Text as NativeText, TextProps as NativeTextProps, TextStyle } from 'react-native';
import { typography } from './theme';
import { useColors } from '../context/ThemeContext';

type Variant = 'hero' | 'title' | 'subtitle' | 'body' | 'bodyBold' | 'caption' | 'label' | 'metric' | 'metricSmall';

type TextProps = PropsWithChildren<
  NativeTextProps & {
    variant?: Variant;
    color?: string;
  }
>;

const coloredVariants: Partial<Record<Variant, 'textSecondary' | 'textMuted'>> = {
  caption: 'textSecondary',
  label: 'textMuted',
};

export function Text({ variant = 'body', color, style, children, ...props }: TextProps) {
  const c = useColors();
  const baseColor = color ?? (coloredVariants[variant] ? c[coloredVariants[variant]!] : c.text);

  return (
    <NativeText style={[typography[variant], { color: baseColor }, style]} {...props}>
      {children}
    </NativeText>
  );
}
