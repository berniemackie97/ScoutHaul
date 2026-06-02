import * as SQLite from 'expo-sqlite';

let databasePromise: Promise<SQLite.SQLiteDatabase> | null = null;

export async function getDatabase(): Promise<SQLite.SQLiteDatabase> {
  databasePromise ??= SQLite.openDatabaseAsync('scouthaul.db');
  return databasePromise;
}

export async function initializeLocalDatabase(): Promise<void> {
  const db = await getDatabase();
  await db.execAsync(`
    pragma journal_mode = WAL;

    create table if not exists item_drafts (
      id text primary key not null,
      title text not null,
      barcode text,
      sellerAskingPrice real,
      condition text,
      notes text,
      sourceName text,
      expectedSalePrice real,
      needsResearch integer not null default 1,
      syncStatus text not null default 'local',
      createdAt text not null,
      updatedAt text not null
    );

    create table if not exists sync_queue (
      id text primary key not null,
      operationType text not null,
      endpoint text not null,
      method text not null,
      bodyJson text not null,
      status text not null default 'pending',
      createdAt text not null,
      lastError text
    );

    create table if not exists pending_comps (
      id text primary key not null,
      draftId text,
      bodyJson text not null,
      syncStatus text not null default 'local',
      createdAt text not null
    );

    create table if not exists item_photos (
      id text primary key not null,
      inventoryItemId text not null,
      localUri text not null,
      isHero integer not null default 0,
      createdAt text not null
    );

    create table if not exists pending_trip_entries (
      id text primary key not null,
      draftId text,
      tripName text,
      bodyJson text not null,
      syncStatus text not null default 'local',
      createdAt text not null
    );

    create table if not exists route_snapshots (
      tripId text primary key not null,
      bodyJson text not null,
      updatedAt text not null
    );

    create table if not exists app_preferences (
      key text primary key not null,
      valueJson text not null,
      updatedAt text not null
    );
  `);
}
