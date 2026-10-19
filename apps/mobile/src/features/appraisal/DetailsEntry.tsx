import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Button } from '../../components/Button';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';

type DetailsEntryProps = {
  initial: string;
  /** Set when the details are a hint to go with a photo that was not identified on its own. */
  photoUri: string | null;
  onSubmit: (details: string) => void;
  onCamera: () => void;
  onClose: () => void;
};

export function DetailsEntry({ initial, photoUri, onSubmit, onCamera, onClose }: DetailsEntryProps) {
  const { colors: c } = useAppTheme();
  const insets = useSafeAreaInsets();
  const [details, setDetails] = useState(initial);
  const ready = details.trim().length >= 3;

  return (
    <KeyboardAvoidingView style={[styles.fill, { backgroundColor: c.background }]} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <View style={[styles.header, { paddingTop: insets.top + spacing.sm, backgroundColor: c.surface, borderBottomColor: c.borderLight }]}>
        <Pressable onPress={onClose} hitSlop={8} accessibilityRole="button" accessibilityLabel="Close" style={styles.headerButton}>
          <Ionicons name="close" size={24} color={c.text} />
        </Pressable>
        <Text variant="bodyBold">{photoUri ? 'Add a hint' : 'Type it'}</Text>
        <Pressable onPress={onCamera} hitSlop={8} accessibilityRole="button" style={styles.headerButton}>
          <Text variant="bodyBold" color={c.primary}>Camera</Text>
        </Pressable>
      </View>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        {photoUri ? <Image source={{ uri: photoUri }} style={styles.photo} contentFit="cover" /> : null}
        <TextField
          label="What is it?"
          value={details}
          onChangeText={setDetails}
          placeholder="Sony WH-1000XM5 headphones, black, used"
          hint="Brand, model, anything printed on it, size and condition all help."
          multiline
          autoFocus
          style={styles.input}
        />
        <Button label="Identify and price" icon="search" size="lg" fullWidth disabled={!ready} onPress={() => onSubmit(details.trim())} />
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  fill: {
    flex: 1,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.md,
    paddingBottom: spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  headerButton: {
    minWidth: 44,
    minHeight: 44,
    justifyContent: 'center',
    alignItems: 'center',
  },
  content: {
    padding: spacing.lg,
    gap: spacing.lg,
  },
  photo: {
    width: '100%',
    aspectRatio: 4 / 3,
    borderRadius: radius.lg,
  },
  input: {
    minHeight: 96,
    textAlignVertical: 'top',
  },
});
