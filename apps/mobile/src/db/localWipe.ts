import { File } from 'expo-file-system';
import { Platform } from 'react-native';
import { getDatabase } from './database';

// Everything the phone holds on behalf of the signed-in person. Device settings stay.
const PERSONAL_TABLES = ['sync_queue', 'pending_comps', 'item_drafts', 'pending_trip_entries', 'route_snapshots'] as const;

/** Removes the previous person's queued work, drafts, photos and route state from this phone. */
export async function clearLocalWorkspace(): Promise<void> {
  const db = await getDatabase();
  const photos = await db.getAllAsync<{ localUri: string }>('select localUri from item_photos');
  if (Platform.OS !== 'web') {
    for (const photo of photos) {
      try {
        const file = new File(photo.localUri);
        if (file.exists) file.delete();
      } catch {
        // A file already gone is the outcome we want.
      }
    }
  }

  await db.withTransactionAsync(async () => {
    await db.execAsync('delete from item_photos;');
    for (const table of PERSONAL_TABLES) {
      await db.execAsync(`delete from ${table};`);
    }
  });
}
