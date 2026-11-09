import { Ionicons } from '@expo/vector-icons';
import { PhotoImage } from '../../components/PhotoImage';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text } from '../../components/Text';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';

/** A photo on the form: either already on the server, or just taken and not yet uploaded. */
export type LocalPhoto = {
  uri: string;
  thumbnailUri?: string;
  remoteId?: string;
  isHero: boolean;
  isRemote?: boolean;
};

/**
 * The photo strip at the top of the inventory form — take, pick, identify, reorder, remove.
 *
 * It was declared below the 1400-line screen it belongs to, sharing that file's stylesheet, so the
 * only way to see what it needed was to read the whole thing.
 */
export function PhotoManager({
  photos,
  onTake,
  onPick,
  onIdentify,
  onRemove,
  onSetHero,
}: {
  photos: LocalPhoto[];
  onTake: () => void;
  onPick: () => void;
  onIdentify: () => void;
  onRemove: (index: number) => void;
  onSetHero: (index: number) => void;
}) {
  const { colors: c, shadows: s } = useAppTheme();
  const hasPhotos = photos.length > 0;

  return (
    <View style={[styles.photoSection, { backgroundColor: c.surface, borderColor: c.borderLight, ...s.sm }]}>
      <View style={styles.photoHeader}>
        <View style={styles.photoHeaderLeft}>
          <Ionicons name="camera-outline" size={18} color={c.primary} />
          <Text variant="bodyBold">Photos</Text>
        </View>
        {hasPhotos ? (
          <View style={[styles.photoCountBadge, { backgroundColor: c.primaryLight }]}>
            <Text variant="label" color={c.primary}>{photos.length}</Text>
          </View>
        ) : (
          <Text variant="caption" color={c.textMuted}>Tap to add</Text>
        )}
      </View>

      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.photoScroll}
      >
        <Pressable
          onPress={onIdentify}
          style={[styles.addPhotoBtn, styles.addPhotoPrimary, { backgroundColor: c.accentLight, borderColor: c.accent }]}
          accessibilityLabel="Photo identify item"
        >
          <Ionicons name="sparkles-outline" size={24} color={c.accent} />
          <Text variant="label" color={c.accent}>Identify</Text>
        </Pressable>
        <Pressable
          onPress={onTake}
          style={[styles.addPhotoBtn, styles.addPhotoPrimary, { backgroundColor: c.primaryLight, borderColor: c.primary }]}
          accessibilityLabel="Take photo"
        >
          <Ionicons name="camera" size={24} color={c.primary} />
          <Text variant="label" color={c.primary}>Camera</Text>
        </Pressable>
        <Pressable
          onPress={onPick}
          style={[styles.addPhotoBtn, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}
          accessibilityLabel="Pick from library"
        >
          <Ionicons name="images-outline" size={22} color={c.textSecondary} />
          <Text variant="label" color={c.textSecondary}>Library</Text>
        </Pressable>
        {photos.map((photo, i) => (
          <View key={photo.remoteId ?? photo.uri} style={styles.photoWrapper}>
            <Pressable onPress={() => onSetHero(i)} style={styles.photoTouch}>
              <PhotoImage
                uri={photo.thumbnailUri ?? photo.uri}
                style={styles.photoThumb}
                contentFit="cover"
                cachePolicy="disk"
              />
              {photo.isHero ? (
                <View style={[styles.heroBadge, { backgroundColor: c.primary }]}>
                  <Ionicons name="star" size={10} color={c.textInverse} />
                </View>
              ) : (
                <View style={[styles.orderBadge, { backgroundColor: 'rgba(0,0,0,0.5)' }]}>
                  <Text variant="label" color={c.textInverse} style={styles.orderText}>{i + 1}</Text>
                </View>
              )}
            </Pressable>
            <Pressable
              onPress={() => onRemove(i)}
              style={[styles.removeBtn, { backgroundColor: c.danger }]}
              // The visible dot stays small so it does not cover the thumbnail; the tappable area is
              // padded out to the 44pt minimum, which it was 10pt short of.
              hitSlop={11}
              accessibilityRole="button"
              accessibilityLabel={`Remove photo ${i + 1}`}
            >
              <Ionicons name="close" size={12} color={c.textInverse} />
            </Pressable>
          </View>
        ))}
      </ScrollView>

      {!hasPhotos ? (
        <View style={[styles.photoHint, { backgroundColor: c.surfaceAlt }]}>
          <Ionicons name="information-circle-outline" size={14} color={c.textMuted} />
          <Text variant="caption" color={c.textMuted}>
            First photo becomes the hero image. Tap any photo to set it as hero.
          </Text>
        </View>
      ) : null}
    </View>
  );
}

// ── Helpers ──

const styles = StyleSheet.create({
  addPhotoBtn: {
    width: 76,
    height: 76,
    borderRadius: radius.md,
    borderWidth: 1.5,
    borderStyle: 'dashed',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xxs,
  },
  addPhotoPrimary: {
    borderStyle: 'solid',
  },
  heroBadge: {
    position: 'absolute',
    bottom: 4,
    left: 4,
    width: 20,
    height: 20,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  orderBadge: {
    position: 'absolute',
    bottom: 4,
    left: 4,
    minWidth: 18,
    height: 18,
    borderRadius: 9,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 4,
  },
  orderText: {
    fontSize: 9,
    lineHeight: 12,
    letterSpacing: 0,
  },
  photoCountBadge: {
    paddingHorizontal: spacing.sm,
    paddingVertical: spacing.xxs,
    borderRadius: radius.full,
  },
  photoHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  photoHeaderLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  photoHint: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.sm,
    borderRadius: radius.sm,
  },
  photoScroll: {
    gap: spacing.sm,
  },
  photoSection: {
    borderRadius: radius.lg,
    borderWidth: 1,
    padding: spacing.lg,
    gap: spacing.md,
  },
  photoThumb: {
    width: 76,
    height: 76,
    borderRadius: radius.md,
  },
  photoTouch: {
    borderRadius: radius.md,
    overflow: 'hidden',
  },
  photoWrapper: {
    position: 'relative',
  },
  removeBtn: {
    position: 'absolute',
    top: -5,
    right: -5,
    width: 22,
    height: 22,
    borderRadius: 11,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
