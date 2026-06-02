import { TextStyle, ViewStyle } from 'react-native';
import { fontFamily } from '../hooks/useFonts';

export type ColorPalette = typeof lightColors;

export const lightColors = {
  background: '#F5F6F8',
  surface: '#FFFFFF',
  surfaceElevated: '#FFFFFF',
  surfaceAlt: '#EEF2F6',
  border: '#E2E8F0',
  borderLight: '#F0F3F7',
  text: '#1A202C',
  textSecondary: '#64748B',
  textMuted: '#94A3B8',
  textInverse: '#FFFFFF',
  primary: '#0B6B5B',
  primaryLight: '#E6F5F0',
  primaryPressed: '#09584B',
  primaryDeep: '#062D25',
  accent: '#C6532D',
  accentLight: '#FEF0EB',
  success: '#059669',
  successLight: '#D1FAE5',
  successDark: '#065F46',
  warning: '#D97706',
  warningLight: '#FEF3C7',
  warningDark: '#92400E',
  danger: '#DC2626',
  dangerLight: '#FEE2E2',
  dangerDark: '#991B1B',
  info: '#2563EB',
  infoLight: '#DBEAFE',
  buyGreen: '#059669',
  buyGreenBg: '#D1FAE5',
  negotiateAmber: '#D97706',
  negotiateAmberBg: '#FEF3C7',
  passRed: '#DC2626',
  passRedBg: '#FEE2E2',
  researchBlue: '#2563EB',
  researchBlueBg: '#DBEAFE',
  unavailableGray: '#64748B',
  unavailableGrayBg: '#F1F5F9',
};

export const darkColors: ColorPalette = {
  background: '#0C0E14',
  surface: '#171923',
  surfaceElevated: '#1E2133',
  surfaceAlt: '#131620',
  border: '#2A2F42',
  borderLight: '#1F2336',
  text: '#ECEEF4',
  textSecondary: '#8B93A8',
  textMuted: '#5A6277',
  textInverse: '#171923',
  primary: '#34D399',
  primaryLight: '#052E1C',
  primaryPressed: '#10B981',
  primaryDeep: '#021A12',
  accent: '#FB923C',
  accentLight: '#3D1A05',
  success: '#34D399',
  successLight: '#052E1C',
  successDark: '#6EE7B7',
  warning: '#FBBF24',
  warningLight: '#3D2802',
  warningDark: '#FDE68A',
  danger: '#F87171',
  dangerLight: '#3D0808',
  dangerDark: '#FCA5A5',
  info: '#60A5FA',
  infoLight: '#0C1B3D',
  buyGreen: '#34D399',
  buyGreenBg: '#052E1C',
  negotiateAmber: '#FBBF24',
  negotiateAmberBg: '#3D2802',
  passRed: '#F87171',
  passRedBg: '#3D0808',
  researchBlue: '#60A5FA',
  researchBlueBg: '#0C1B3D',
  unavailableGray: '#8B93A8',
  unavailableGrayBg: '#1E2133',
};

export const colors = lightColors;

export const spacing = {
  xxs: 2,
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  xxl: 32,
  xxxl: 48,
};

export const radius = {
  sm: 6,
  md: 10,
  lg: 14,
  xl: 20,
  full: 999,
};

export const shadows = {
  sm: {
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.05,
    shadowRadius: 2,
    elevation: 1,
  } as ViewStyle,
  md: {
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.08,
    shadowRadius: 6,
    elevation: 3,
  } as ViewStyle,
  lg: {
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.12,
    shadowRadius: 12,
    elevation: 5,
  } as ViewStyle,
};

export const darkShadows = {
  sm: { ...shadows.sm, shadowOpacity: 0.2 } as ViewStyle,
  md: { ...shadows.md, shadowOpacity: 0.25 } as ViewStyle,
  lg: { ...shadows.lg, shadowOpacity: 0.35 } as ViewStyle,
};

export const typography = {
  hero: { fontSize: 32, lineHeight: 38, fontFamily: fontFamily.display } as TextStyle,
  title: { fontSize: 24, lineHeight: 30, fontFamily: fontFamily.display } as TextStyle,
  subtitle: { fontSize: 18, lineHeight: 24, fontFamily: fontFamily.bold } as TextStyle,
  body: { fontSize: 15, lineHeight: 22, fontFamily: fontFamily.regular } as TextStyle,
  bodyBold: { fontSize: 15, lineHeight: 22, fontFamily: fontFamily.semiBold } as TextStyle,
  caption: { fontSize: 13, lineHeight: 18, fontFamily: fontFamily.medium } as TextStyle,
  label: {
    fontSize: 11,
    lineHeight: 14,
    fontFamily: fontFamily.bold,
    letterSpacing: 0.8,
    textTransform: 'uppercase',
  } as TextStyle,
  metric: { fontSize: 28, lineHeight: 34, fontFamily: fontFamily.display } as TextStyle,
  metricSmall: { fontSize: 20, lineHeight: 26, fontFamily: fontFamily.displayBold } as TextStyle,
};

export type ToneKey = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'primary';

export function buildToneColors(c: ColorPalette) {
  return {
    neutral: { bg: c.surfaceAlt, text: c.textSecondary, border: c.border },
    success: { bg: c.successLight, text: c.successDark, border: c.success },
    warning: { bg: c.warningLight, text: c.warningDark, border: c.warning },
    danger: { bg: c.dangerLight, text: c.dangerDark, border: c.danger },
    info: { bg: c.infoLight, text: c.info, border: c.info },
    primary: { bg: c.primaryLight, text: c.primary, border: c.primary },
  } as Record<ToneKey, { bg: string; text: string; border: string }>;
}

export const toneColors = buildToneColors(lightColors);
