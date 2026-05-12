import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { Animated, Easing, Platform, StyleSheet, View } from 'react-native';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { Screen } from '../src/components/Screen';
import { Text } from '../src/components/Text';
import { radius, spacing } from '../src/components/theme';
import { useAppTheme } from '../src/context/ThemeContext';
import { saveItemDraft } from '../src/db/localStore';

export default function BarcodeScannerScreen() {
  const { colors: c } = useAppTheme();
  const [permission, requestPermission] = useCameraPermissions();
  const [barcode, setBarcode] = useState<string | null>(null);
  const [isLocked, setIsLocked] = useState(false);
  const scanLine = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    if (isLocked) return;
    const anim = Animated.loop(
      Animated.sequence([
        Animated.timing(scanLine, { toValue: 1, duration: 2000, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
        Animated.timing(scanLine, { toValue: 0, duration: 2000, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
      ]),
    );
    anim.start();
    return () => anim.stop();
  }, [isLocked, scanLine]);

  if (!permission) {
    return (
      <Screen>
        <Text variant="body">Checking camera permission...</Text>
      </Screen>
    );
  }

  if (!permission.granted) {
    return (
      <Screen>
        <View style={styles.center}>
          <Card>
            <View style={styles.permContent}>
              <Ionicons name="camera-outline" size={48} color={c.textMuted} />
              <Text variant="bodyBold">Camera Access Required</Text>
              <Text variant="caption">
                Camera permission is needed to scan barcodes. No images are stored.
              </Text>
              <Button
                label="Allow Camera"
                icon="camera-outline"
                size="lg"
                fullWidth
                onPress={() => void requestPermission()}
              />
            </View>
          </Card>
        </View>
      </Screen>
    );
  }

  async function continueWithBarcode() {
    if (!barcode) return;
    await saveItemDraft({
      title: `Barcode ${barcode}`,
      barcode,
      needsResearch: true,
      syncStatus: 'local',
    });
    router.replace({ pathname: '/(tabs)/scout', params: { barcode } });
  }

  const scanLineTranslate = scanLine.interpolate({
    inputRange: [0, 1],
    outputRange: [0, 120],
  });

  return (
    <Screen scroll={false} edges={['bottom']}>
      <View style={styles.container}>
        <CameraView
          style={styles.camera}
          facing="back"
          barcodeScannerSettings={{
            barcodeTypes: ['upc_a', 'upc_e', 'ean13', 'ean8', 'code128', 'code39', 'qr'],
          }}
          onBarcodeScanned={
            isLocked
              ? undefined
              : (result) => {
                  if (result.data) {
                    setIsLocked(true);
                    setBarcode(result.data);
                  }
                }
          }
        />

        <View style={styles.overlay} pointerEvents="none">
          <View style={styles.dimTop} />
          <View style={styles.middleRow}>
            <View style={styles.dimSide} />
            <View style={styles.viewfinder}>
              <View style={[styles.corner, styles.cornerTL, { borderColor: barcode ? c.success : c.primary }]} />
              <View style={[styles.corner, styles.cornerTR, { borderColor: barcode ? c.success : c.primary }]} />
              <View style={[styles.corner, styles.cornerBL, { borderColor: barcode ? c.success : c.primary }]} />
              <View style={[styles.corner, styles.cornerBR, { borderColor: barcode ? c.success : c.primary }]} />
              {!isLocked ? (
                <Animated.View
                  style={[
                    styles.scanLine,
                    { backgroundColor: c.primary, transform: [{ translateY: scanLineTranslate }] },
                  ]}
                />
              ) : null}
            </View>
            <View style={styles.dimSide} />
          </View>
          <View style={styles.dimBottom} />
        </View>

        <View style={[styles.panel, { backgroundColor: c.surface }]}>
          {barcode ? (
            <>
              <View style={styles.barcodeResult}>
                <Ionicons name="checkmark-circle" size={24} color={c.success} />
                <Text variant="bodyBold" color={c.success}>Barcode Captured</Text>
              </View>
              <Text variant="metric" style={styles.barcodeText}>{barcode}</Text>
              <View style={styles.actions}>
                <Button
                  label="Rescan"
                  icon="refresh-outline"
                  variant="secondary"
                  onPress={() => { setBarcode(null); setIsLocked(false); }}
                />
                <Button
                  label="Continue"
                  icon="arrow-forward-outline"
                  onPress={() => void continueWithBarcode()}
                />
              </View>
            </>
          ) : (
            <>
              <Text variant="bodyBold">Point at a barcode</Text>
              <Text variant="caption">UPC, EAN, ISBN, Code 128, or QR code</Text>
            </>
          )}
        </View>
      </View>
    </Screen>
  );
}

const VIEWFINDER_SIZE = 260;
const CORNER_SIZE = 24;
const CORNER_WIDTH = 3;

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  center: {
    flex: 1,
    justifyContent: 'center',
  },
  permContent: {
    alignItems: 'center',
    gap: spacing.md,
  },
  camera: {
    flex: 1,
    backgroundColor: '#000',
  },
  overlay: {
    ...StyleSheet.absoluteFillObject,
  },
  dimTop: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.55)',
  },
  dimBottom: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.55)',
  },
  middleRow: {
    flexDirection: 'row',
    height: VIEWFINDER_SIZE * 0.55,
  },
  dimSide: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.55)',
  },
  viewfinder: {
    width: VIEWFINDER_SIZE,
    height: '100%',
    overflow: 'hidden',
  },
  corner: {
    position: 'absolute',
    width: CORNER_SIZE,
    height: CORNER_SIZE,
  },
  cornerTL: {
    top: 0,
    left: 0,
    borderTopWidth: CORNER_WIDTH,
    borderLeftWidth: CORNER_WIDTH,
    borderTopLeftRadius: radius.sm,
  },
  cornerTR: {
    top: 0,
    right: 0,
    borderTopWidth: CORNER_WIDTH,
    borderRightWidth: CORNER_WIDTH,
    borderTopRightRadius: radius.sm,
  },
  cornerBL: {
    bottom: 0,
    left: 0,
    borderBottomWidth: CORNER_WIDTH,
    borderLeftWidth: CORNER_WIDTH,
    borderBottomLeftRadius: radius.sm,
  },
  cornerBR: {
    bottom: 0,
    right: 0,
    borderBottomWidth: CORNER_WIDTH,
    borderRightWidth: CORNER_WIDTH,
    borderBottomRightRadius: radius.sm,
  },
  scanLine: {
    position: 'absolute',
    left: spacing.md,
    right: spacing.md,
    height: 2,
    borderRadius: 1,
    top: 8,
  },
  panel: {
    padding: spacing.lg,
    gap: spacing.sm,
    alignItems: 'center',
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
  },
  barcodeResult: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  barcodeText: {
    textAlign: 'center',
  },
  actions: {
    flexDirection: 'row',
    gap: spacing.md,
    paddingTop: spacing.sm,
  },
});
