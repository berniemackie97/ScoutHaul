import { useFonts as useExpoFonts } from 'expo-font';
import {
  DMSans_400Regular,
  DMSans_500Medium,
  DMSans_600SemiBold,
  DMSans_700Bold,
  DMSans_800ExtraBold,
} from '@expo-google-fonts/dm-sans';
import {
  SourceSerif4_700Bold,
  SourceSerif4_800ExtraBold,
} from '@expo-google-fonts/source-serif-4';

export function useAppFonts() {
  const [loaded, error] = useExpoFonts({
    'DMSans-Regular': DMSans_400Regular,
    'DMSans-Medium': DMSans_500Medium,
    'DMSans-SemiBold': DMSans_600SemiBold,
    'DMSans-Bold': DMSans_700Bold,
    'DMSans-ExtraBold': DMSans_800ExtraBold,
    'SourceSerif-Bold': SourceSerif4_700Bold,
    'SourceSerif-ExtraBold': SourceSerif4_800ExtraBold,
  });

  return { fontsLoaded: loaded, fontError: error };
}

export const fontFamily = {
  regular: 'DMSans-Regular',
  medium: 'DMSans-Medium',
  semiBold: 'DMSans-SemiBold',
  bold: 'DMSans-Bold',
  extraBold: 'DMSans-ExtraBold',
  display: 'SourceSerif-ExtraBold',
  displayBold: 'SourceSerif-Bold',
} as const;
