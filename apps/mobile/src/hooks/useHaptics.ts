import * as Haptics from 'expo-haptics';

export function useHaptics() {
  return {
    light: () => void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light),
    medium: () => void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium),
    heavy: () => void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy),
    success: () => void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success),
    warning: () => void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning),
    error: () => void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error),
    selection: () => void Haptics.selectionAsync(),
  };
}
