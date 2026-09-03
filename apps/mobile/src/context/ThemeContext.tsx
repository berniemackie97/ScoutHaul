import { createContext, PropsWithChildren, useContext, useMemo, useState } from 'react';
import { useColorScheme, ViewStyle } from 'react-native';
import {
  buildToneColors,
  type ColorPalette,
  darkColors,
  darkShadows,
  lightColors,
  shadows,
  type ToneKey,
} from '../components/theme';

type ThemeMode = 'system' | 'light' | 'dark';

type ThemeContextValue = {
  colors: ColorPalette;
  tones: Record<ToneKey, { bg: string; text: string; border: string }>;
  shadows: { sm: ViewStyle; md: ViewStyle; lg: ViewStyle };
  isDark: boolean;
  mode: ThemeMode;
  setMode: (mode: ThemeMode) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

export function ThemeProvider({ children }: PropsWithChildren) {
  const systemScheme = useColorScheme();
  const [mode, setMode] = useState<ThemeMode>('system');

  const isDark = mode === 'dark' || (mode === 'system' && systemScheme === 'dark');
  const activeColors = isDark ? darkColors : lightColors;
  const activeShadows = isDark ? darkShadows : shadows;
  const activeTones = useMemo(() => buildToneColors(activeColors), [isDark]);

  const value = useMemo<ThemeContextValue>(
    () => ({
      colors: activeColors,
      tones: activeTones,
      shadows: activeShadows,
      isDark,
      mode,
      setMode,
    }),
    [isDark, mode],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useAppTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    return {
      colors: lightColors,
      tones: buildToneColors(lightColors),
      shadows,
      isDark: false,
      mode: 'system',
      setMode: () => {},
    };
  }
  return ctx;
}

export function useColors(): ColorPalette {
  return useAppTheme().colors;
}

export function useTones() {
  return useAppTheme().tones;
}
