import * as SQLite from 'expo-sqlite';
import { runMigrations } from './migrations';

let databasePromise: Promise<SQLite.SQLiteDatabase> | null = null;

export async function getDatabase(): Promise<SQLite.SQLiteDatabase> {
  databasePromise ??= SQLite.openDatabaseAsync('scouthaul.db');
  return databasePromise;
}

export async function initializeLocalDatabase(): Promise<void> {
  const db = await getDatabase();
  await db.execAsync('pragma journal_mode = WAL;');
  await runMigrations(db);
}
