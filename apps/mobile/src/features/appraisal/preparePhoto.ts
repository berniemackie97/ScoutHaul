import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';

// A phone photo is 3–12 MB; at this size it uploads in a second on a weak signal and still shows
// labels and model numbers clearly.
const TARGET_WIDTH = 1536;
const JPEG_QUALITY = 0.6;

export async function preparePhoto(uri: string): Promise<string> {
  const rendered = await ImageManipulator.manipulate(uri).resize({ width: TARGET_WIDTH }).renderAsync();
  const saved = await rendered.saveAsync({ compress: JPEG_QUALITY, format: SaveFormat.JPEG });
  return saved.uri;
}
