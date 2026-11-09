import { Ionicons } from '@expo/vector-icons';
import { PhotoImage } from './PhotoImage';
import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useColors } from '../context/ThemeContext';

type PhotoThumbnailProps = {
  uri?: string | null;
  size?: number;
  onPress?: () => void;
  placeholder?: boolean;
};

export function PhotoThumbnail({ uri, size = 56, onPress, placeholder }: PhotoThumbnailProps) {
  const c = useColors();

  if (!uri || placeholder) {
    return (
      <Pressable
        onPress={onPress}
        style={[styles.placeholder, { width: size, height: size, backgroundColor: c.borderLight, borderColor: c.border }]}
        accessibilityLabel={onPress ? 'Add photo' : 'No photo'}
        accessibilityRole={onPress ? 'button' : 'image'}
      >
        <Ionicons name={onPress ? 'camera-outline' : 'image-outline'} size={size * 0.4} color={c.textMuted} />
      </Pressable>
    );
  }

  return (
    <Pressable onPress={onPress} accessibilityLabel="Item photo" accessibilityRole="image">
      <PhotoImage
        uri={uri}
        style={[styles.image, { width: size, height: size }]}
        contentFit="cover"
        cachePolicy="disk"
        transition={120}
      />
    </Pressable>
  );
}

type PhotoStripProps = {
  photos: string[];
  onAdd?: () => void;
  size?: number;
};

export function PhotoStrip({ photos, onAdd, size = 64 }: PhotoStripProps) {
  const c = useColors();
  return (
    <View style={styles.strip}>
      {photos.map((uri) => (
        <PhotoThumbnail key={uri} uri={uri} size={size} />
      ))}
      {onAdd ? (
        <PhotoThumbnail size={size} onPress={onAdd} placeholder />
      ) : null}
      {photos.length === 0 && !onAdd ? (
        <View style={styles.emptyStrip}>
          <Ionicons name="camera-outline" size={18} color={c.textMuted} />
          <Text variant="caption">No photos</Text>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  placeholder: {
    borderRadius: radius.md,
    borderWidth: 1,
    borderStyle: 'dashed',
    alignItems: 'center',
    justifyContent: 'center',
  },
  image: {
    borderRadius: radius.md,
  },
  strip: {
    flexDirection: 'row',
    gap: spacing.sm,
    flexWrap: 'wrap',
  },
  emptyStrip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
});
