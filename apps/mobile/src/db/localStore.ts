import * as SQLite from 'expo-sqlite';
import { apiRequest } from '../api/client';
import type { LocalItemDraft } from '../domain/types';
import { createLocalId } from '../utils/id';

type SyncQueueRow = {
  id: string;
  operationType: string;
  endpoint: string;
  method: 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  bodyJson: string;
  status: 'pending' | 'synced' | 'failed';
  createdAt: string;
  lastError: string | null;
};

type LocalItemDraftRow = Omit<LocalItemDraft, 'needsResearch'> & {
  needsResearch: number;
};

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
  `);
}

export async function saveItemDraft(input: Partial<LocalItemDraft> & { title?: string }): Promise<LocalItemDraft> {
  const db = await getDatabase();
  const now = new Date().toISOString();
  const draft: LocalItemDraft = {
    id: input.id ?? createLocalId('draft'),
    title: input.title?.trim() || 'Untitled draft',
    barcode: input.barcode ?? null,
    sellerAskingPrice: input.sellerAskingPrice ?? null,
    condition: input.condition ?? null,
    notes: input.notes ?? null,
    sourceName: input.sourceName ?? null,
    expectedSalePrice: input.expectedSalePrice ?? null,
    needsResearch: input.needsResearch ?? true,
    syncStatus: input.syncStatus ?? 'local',
    createdAt: input.createdAt ?? now,
    updatedAt: now,
  };

  await db.runAsync(
    `insert into item_drafts (
      id, title, barcode, sellerAskingPrice, condition, notes, sourceName, expectedSalePrice, needsResearch, syncStatus, createdAt, updatedAt
    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    on conflict(id) do update set
      title = excluded.title,
      barcode = excluded.barcode,
      sellerAskingPrice = excluded.sellerAskingPrice,
      condition = excluded.condition,
      notes = excluded.notes,
      sourceName = excluded.sourceName,
      expectedSalePrice = excluded.expectedSalePrice,
      needsResearch = excluded.needsResearch,
      syncStatus = excluded.syncStatus,
      updatedAt = excluded.updatedAt`,
    [
      draft.id,
      draft.title,
      draft.barcode ?? null,
      draft.sellerAskingPrice ?? null,
      draft.condition ?? null,
      draft.notes ?? null,
      draft.sourceName ?? null,
      draft.expectedSalePrice ?? null,
      draft.needsResearch ? 1 : 0,
      draft.syncStatus,
      draft.createdAt,
      draft.updatedAt,
    ],
  );

  return draft;
}

export async function listItemDrafts(): Promise<LocalItemDraft[]> {
  const db = await getDatabase();
  const rows = await db.getAllAsync<LocalItemDraftRow>(
    `select * from item_drafts order by updatedAt desc`,
  );

  return rows.map((row) => ({ ...row, needsResearch: Boolean(row.needsResearch) }));
}

export async function enqueueCreateInventoryFromDraft(draft: LocalItemDraft): Promise<void> {
  await enqueueOperation('CreateInventoryDraft', '/api/inventory', 'POST', {
    title: draft.title,
    sellerAskingPrice: draft.sellerAskingPrice,
    estimatedSalePrice: draft.expectedSalePrice,
    condition: draft.condition,
    barcode: draft.barcode,
    notes: [draft.notes, draft.sourceName ? `Source: ${draft.sourceName}` : null].filter(Boolean).join('\n'),
  });

  const db = await getDatabase();
  await db.runAsync(`update item_drafts set syncStatus = 'queued', updatedAt = ? where id = ?`, [
    new Date().toISOString(),
    draft.id,
  ]);
}

export async function enqueueComp(body: unknown, draftId?: string): Promise<void> {
  const db = await getDatabase();
  const id = createLocalId('comp');
  const createdAt = new Date().toISOString();
  await db.runAsync(
    `insert into pending_comps (id, draftId, bodyJson, syncStatus, createdAt) values (?, ?, ?, 'queued', ?)`,
    [id, draftId ?? null, JSON.stringify(body), createdAt],
  );
  await enqueueOperation('CreateComp', '/api/market-observations', 'POST', body);
}

export async function enqueueOperation(
  operationType: string,
  endpoint: string,
  method: SyncQueueRow['method'],
  body: unknown,
): Promise<void> {
  const db = await getDatabase();
  await db.runAsync(
    `insert into sync_queue (id, operationType, endpoint, method, bodyJson, status, createdAt, lastError)
      values (?, ?, ?, ?, ?, 'pending', ?, null)`,
    [createLocalId('sync'), operationType, endpoint, method, JSON.stringify(body), new Date().toISOString()],
  );
}

export async function getPendingSyncCount(): Promise<number> {
  const db = await getDatabase();
  const row = await db.getFirstAsync<{ count: number }>(
    `select count(*) as count from sync_queue where status in ('pending', 'failed')`,
  );
  return row?.count ?? 0;
}

export async function listSyncQueue(): Promise<SyncQueueRow[]> {
  const db = await getDatabase();
  return db.getAllAsync<SyncQueueRow>(`select * from sync_queue order by createdAt asc`);
}

export async function processSyncQueue(): Promise<{ synced: number; failed: number }> {
  const db = await getDatabase();
  const rows = await db.getAllAsync<SyncQueueRow>(
    `select * from sync_queue where status in ('pending', 'failed') order by createdAt asc`,
  );

  let synced = 0;
  let failed = 0;

  for (const row of rows) {
    try {
      await apiRequest(row.endpoint, {
        method: row.method,
        body: JSON.parse(row.bodyJson),
      });
      await db.runAsync(`update sync_queue set status = 'synced', lastError = null where id = ?`, [row.id]);
      synced += 1;
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unknown sync error';
      await db.runAsync(`update sync_queue set status = 'failed', lastError = ? where id = ?`, [message, row.id]);
      failed += 1;
    }
  }

  return { synced, failed };
}
