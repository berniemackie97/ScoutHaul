import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions, type BarcodeScanningResult } from 'expo-camera';
import { useRef, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Button } from '../../components/Button';
import { Text } from '../../components/Text';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { haptics } from '../../utils/haptics';

export type CaptureMode = 'barcode' | 'photo';

type ScoutCameraProps = {
  mode: CaptureMode;
  onModeChange: (mode: CaptureMode) => void;
  onBarcode: (barcode: string) => void;
  onPhoto: (photoUri: string) => void;
  onType: () => void;
  onClose: () => void;
};

const BARCODE_TYPES = ['upc_a', 'upc_e', 'ean13', 'ean8', 'code128'] as const;
const SCRIM = 'rgba(0,0,0,0.55)';
const ON_CAMERA = '#FFFFFF';

export function ScoutCamera({ mode, onModeChange, onBarcode, onPhoto, onType, onClose }: ScoutCameraProps) {
  const { colors: c } = useAppTheme();
  const insets = useSafeAreaInsets();
  const [permission, requestPermission] = useCameraPermissions();
  const [torch, setTorch] = useState(false);
  const [capturing, setCapturing] = useState(false);
  const camera = useRef<CameraView>(null);
  // The scanner reports the same code many times a second; only the first one counts.
  const scanned = useRef(false);

  if (!permission) {
    return <View style={[styles.fill, { backgroundColor: c.background }]} />;
  }

  if (!permission.granted) {
    return (
      <View style={[styles.fill, styles.permission, { backgroundColor: c.background, paddingTop: insets.top }]}>
        <Ionicons name="camera-outline" size={40} color={c.textMuted} />
        <Text variant="subtitle">Camera access needed</Text>
        <Text variant="caption" style={styles.centered}>
          Scanning reads barcodes and photographs items to identify them.
        </Text>
        <Button label="Allow camera" onPress={() => void requestPermission()} />
        <Button label="Type it instead" variant="ghost" onPress={onType} />
      </View>
    );
  }

  function handleBarcode(result: BarcodeScanningResult) {
    if (scanned.current || !result.data) return;
    scanned.current = true;
    haptics.light();
    onBarcode(result.data);
  }

  async function takePhoto() {
    if (!camera.current || capturing) return;
    setCapturing(true);
    haptics.medium();
    try {
      const photo = await camera.current.takePictureAsync({ quality: 0.8 });
      if (photo?.uri) onPhoto(photo.uri);
    } finally {
      setCapturing(false);
    }
  }

  return (
    <View style={styles.fill}>
      <CameraView
        ref={camera}
        style={StyleSheet.absoluteFill}
        facing="back"
        enableTorch={torch}
        barcodeScannerSettings={mode === 'barcode' ? { barcodeTypes: [...BARCODE_TYPES] } : undefined}
        onBarcodeScanned={mode === 'barcode' ? handleBarcode : undefined}
      />

      <View style={[styles.topBar, { paddingTop: insets.top + spacing.sm }]}>
        <RoundButton icon="close" label="Close" onPress={onClose} />
        <RoundButton icon={torch ? 'flash' : 'flash-outline'} label={torch ? 'Torch off' : 'Torch on'} onPress={() => setTorch((on) => !on)} />
      </View>

      <View style={styles.frameArea} pointerEvents="none">
        <View style={[styles.frame, mode === 'barcode' ? styles.barcodeFrame : styles.photoFrame]} />
        <Text variant="caption" color={ON_CAMERA} style={styles.hint}>
          {mode === 'barcode' ? 'Line the barcode up inside the box' : 'Fill the frame with the item and any label'}
        </Text>
      </View>

      <View style={[styles.bottomBar, { paddingBottom: insets.bottom + spacing.lg }]}>
        {mode === 'photo' ? (
          <Pressable
            onPress={() => void takePhoto()}
            disabled={capturing}
            accessibilityRole="button"
            accessibilityLabel="Take photo"
            style={({ pressed }) => [styles.shutter, pressed && styles.shutterPressed]}
          >
            {capturing ? <ActivityIndicator color={ON_CAMERA} /> : <View style={styles.shutterCore} />}
          </Pressable>
        ) : (
          <View style={styles.shutterSpacer} />
        )}

        <View style={styles.modes}>
          <ModeTab label="Barcode" selected={mode === 'barcode'} onPress={() => onModeChange('barcode')} />
          <ModeTab label="Photo" selected={mode === 'photo'} onPress={() => onModeChange('photo')} />
          <ModeTab label="Type" selected={false} onPress={onType} />
        </View>
      </View>
    </View>
  );
}

function RoundButton({ icon, label, onPress }: { icon: keyof typeof Ionicons.glyphMap; label: string; onPress: () => void }) {
  return (
    <Pressable onPress={onPress} hitSlop={8} accessibilityRole="button" accessibilityLabel={label} style={styles.roundButton}>
      <Ionicons name={icon} size={22} color={ON_CAMERA} />
    </Pressable>
  );
}

function ModeTab({ label, selected, onPress }: { label: string; selected: boolean; onPress: () => void }) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="tab"
      accessibilityState={{ selected }}
      style={[styles.modeTab, selected && styles.modeTabSelected]}
    >
      <Text variant="bodyBold" color={selected ? '#000000' : ON_CAMERA}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  fill: {
    flex: 1,
    backgroundColor: '#000000',
  },
  permission: {
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.md,
    padding: spacing.xl,
  },
  centered: {
    textAlign: 'center',
  },
  topBar: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.lg,
  },
  roundButton: {
    width: 44,
    height: 44,
    borderRadius: radius.full,
    backgroundColor: SCRIM,
    alignItems: 'center',
    justifyContent: 'center',
  },
  frameArea: {
    ...StyleSheet.absoluteFillObject,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.md,
  },
  frame: {
    borderWidth: 2,
    borderColor: ON_CAMERA,
    borderRadius: radius.md,
  },
  barcodeFrame: {
    width: '78%',
    aspectRatio: 2.2,
  },
  photoFrame: {
    width: '86%',
    aspectRatio: 0.85,
  },
  hint: {
    backgroundColor: SCRIM,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
    borderRadius: radius.full,
    overflow: 'hidden',
  },
  bottomBar: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 0,
    alignItems: 'center',
    gap: spacing.lg,
    paddingTop: spacing.lg,
    backgroundColor: SCRIM,
  },
  shutter: {
    width: 76,
    height: 76,
    borderRadius: radius.full,
    borderWidth: 4,
    borderColor: ON_CAMERA,
    alignItems: 'center',
    justifyContent: 'center',
  },
  shutterPressed: {
    transform: [{ scale: 0.95 }],
  },
  shutterCore: {
    width: 60,
    height: 60,
    borderRadius: radius.full,
    backgroundColor: ON_CAMERA,
  },
  shutterSpacer: {
    height: 76,
  },
  modes: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  modeTab: {
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
    borderRadius: radius.full,
    minHeight: 44,
    justifyContent: 'center',
  },
  modeTabSelected: {
    backgroundColor: ON_CAMERA,
  },
});
