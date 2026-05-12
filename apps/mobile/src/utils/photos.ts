import { Platform } from 'react-native';
import * as ImagePicker from 'expo-image-picker';
import { getDatabase } from '../db/localStore';
import { createLocalId } from './id';

export type ItemPhoto = {
  id: string;
  inventoryItemId: string;
  localUri: string;
  isHero: boolean;
  createdAt: string;
};

type ItemPhotoRow = Omit<ItemPhoto, 'isHero'> & { isHero: number };

let _photoDir: InstanceType<typeof import('expo-file-system').Directory> | null = null;

function getPhotoDir() {
  if (_photoDir) return _photoDir;
  const { Directory, Paths } = require('expo-file-system') as typeof import('expo-file-system');
  _photoDir = new Directory(Paths.document, 'photos/');
  return _photoDir;
}

function ensurePhotoDir() {
  const dir = getPhotoDir();
  if (!dir.exists) {
    dir.create();
  }
}

export async function pickPhoto(): Promise<string | null> {
  const result = await ImagePicker.launchImageLibraryAsync({
    mediaTypes: ['images'],
    quality: 0.8,
    allowsEditing: true,
    aspect: [4, 3],
  });
  if (result.canceled || !result.assets[0]) return null;
  return result.assets[0].uri;
}

export async function takePhoto(): Promise<string | null> {
  const perm = await ImagePicker.requestCameraPermissionsAsync();
  if (!perm.granted) return null;
  const result = await ImagePicker.launchCameraAsync({
    quality: 0.8,
    allowsEditing: true,
    aspect: [4, 3],
  });
  if (result.canceled || !result.assets[0]) return null;
  return result.assets[0].uri;
}

export async function savePhoto(inventoryItemId: string, sourceUri: string, isHero = false): Promise<ItemPhoto> {
  if (Platform.OS === 'web') throw new Error('Photo storage not available on web');
  const { File } = require('expo-file-system') as typeof import('expo-file-system');
  ensurePhotoDir();
  const id = createLocalId('photo');
  const ext = sourceUri.split('.').pop() ?? 'jpg';
  const dest = new File(getPhotoDir(), `${id}.${ext}`);
  const source = new File(sourceUri);
  source.copy(dest);

  const photo: ItemPhoto = {
    id,
    inventoryItemId,
    localUri: dest.uri,
    isHero,
    createdAt: new Date().toISOString(),
  };

  const db = await getDatabase();
  await db.runAsync(
    `INSERT INTO item_photos (id, inventoryItemId, localUri, isHero, createdAt)
     VALUES (?, ?, ?, ?, ?)`,
    [photo.id, photo.inventoryItemId, photo.localUri, isHero ? 1 : 0, photo.createdAt],
  );

  return photo;
}

export async function getPhotos(inventoryItemId: string): Promise<ItemPhoto[]> {
  const db = await getDatabase();
  const rows = await db.getAllAsync<ItemPhotoRow>(
    `SELECT * FROM item_photos WHERE inventoryItemId = ? ORDER BY isHero DESC, createdAt ASC`,
    [inventoryItemId],
  );
  return rows.map((r) => ({ ...r, isHero: Boolean(r.isHero) }));
}

export async function deletePhoto(id: string): Promise<void> {
  const db = await getDatabase();
  const row = await db.getFirstAsync<{ localUri: string }>(`SELECT localUri FROM item_photos WHERE id = ?`, [id]);
  if (row && Platform.OS !== 'web') {
    try {
      const { File } = require('expo-file-system') as typeof import('expo-file-system');
      const file = new File(row.localUri);
      if (file.exists) file.delete();
    } catch {
      // filesystem not available on web
    }
  }
  await db.runAsync(`DELETE FROM item_photos WHERE id = ?`, [id]);
}

export async function getHeroPhoto(inventoryItemId: string): Promise<string | null> {
  const db = await getDatabase();
  const row = await db.getFirstAsync<{ localUri: string }>(
    `SELECT localUri FROM item_photos WHERE inventoryItemId = ? ORDER BY isHero DESC, createdAt ASC LIMIT 1`,
    [inventoryItemId],
  );
  return row?.localUri ?? null;
}
