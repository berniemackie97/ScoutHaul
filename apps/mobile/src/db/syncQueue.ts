import { api, apiRequest } from '../api';
import { deletePhoto, getPhotos } from '../utils/photos';
import { createLocalId } from '../utils/id';
import { getDatabase } from './database';
import { classifyFailure } from './syncQueueRules';

export type SyncQueueRow = {
  id: string;
  operationType: string;
  endpoint: string;
  method: 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  bodyJson: string;
  /** `stuck` is the end of the line: tried enough times, or rejected in a way that will not change. */
  status: 'pending' | 'synced' | 'failed' | 'stuck';
  createdAt: string;
  lastError: string | null;
  attempts: number;
  /** Not retried before this time. Null means "as soon as possible". */
  nextAttemptAt: string | null;
};

/** Rows the queue still intends to send. `stuck` ones are excluded — they need a person, not a retry. */
const RETRIABLE = `status in ('pending', 'failed')`;

/**
 * Queues a write for later, returning the row's id.
 *
 * The id is the handle for anything that has to travel with the write but will not fit in JSON —
 * photos, in practice. They are stored against it and attached once the write has been delivered and
 * there is a real record to attach them to.
 */
export async function enqueueOperation(
  operationType: string,
  endpoint: string,
  method: SyncQueueRow['method'],
  body: unknown,
): Promise<string> {
  const db = await getDatabase();
  const id = createLocalId('sync');
  await db.runAsync(
    `insert into sync_queue (id, operationType, endpoint, method, bodyJson, status, createdAt, lastError)
      values (?, ?, ?, ?, ?, 'pending', ?, null)`,
    [id, operationType, endpoint, method, JSON.stringify(body), new Date().toISOString()],
  );
  return id;
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

export async function getPendingSyncCount(): Promise<number> {
  const db = await getDatabase();
  const row = await db.getFirstAsync<{ count: number }>(
    `select count(*) as count from sync_queue where ${RETRIABLE}`,
  );
  return row?.count ?? 0;
}

/** Writes the queue has given up on. These are the only ones a person has to decide about. */
export async function listStuckOperations(): Promise<SyncQueueRow[]> {
  const db = await getDatabase();
  return db.getAllAsync<SyncQueueRow>(
    `select * from sync_queue where status = 'stuck' order by createdAt asc`,
  );
}

/** Throws away a write the queue could not deliver. The only way a stuck row leaves the queue. */
export async function discardOperation(id: string): Promise<void> {
  const db = await getDatabase();
  await db.runAsync(`delete from sync_queue where id = ? and status = 'stuck'`, [id]);
}

/** Puts a stuck write back in line, for when the reason it failed has been dealt with. */
export async function retryStuckOperation(id: string): Promise<void> {
  const db = await getDatabase();
  await db.runAsync(
    `update sync_queue set status = 'pending', attempts = 0, nextAttemptAt = null where id = ? and status = 'stuck'`,
    [id],
  );
}

export async function listSyncQueue(): Promise<SyncQueueRow[]> {
  const db = await getDatabase();
  return db.getAllAsync<SyncQueueRow>(`select * from sync_queue order by createdAt asc`);
}

export type SyncResult = { synced: number; failed: number; stuck: number };

export async function processSyncQueue(now: Date = new Date()): Promise<SyncResult> {
  const db = await getDatabase();
  // Rows still inside their backoff are skipped rather than hammered on every reconnect.
  const rows = await db.getAllAsync<SyncQueueRow>(
    `select * from sync_queue
      where ${RETRIABLE} and (nextAttemptAt is null or nextAttemptAt <= ?)
      order by createdAt asc`,
    [now.toISOString()],
  );

  let synced = 0;
  let failed = 0;
  let stuck = 0;

  for (const row of rows) {
    try {
      const created = await apiRequest<{ id?: string } | undefined>(row.endpoint, {
        method: row.method,
        body: JSON.parse(row.bodyJson),
        idempotencyKey: row.id,
      });

      // Photos could not travel in the queued JSON — they are files, and until the write lands there
      // is no record to attach them to. They were saved against this row's id when it was queued.
      if (created?.id) {
        await attachQueuedPhotos(row.id, created.id);
      }

      await db.runAsync(
        `update sync_queue set status = 'synced', lastError = null, nextAttemptAt = null where id = ?`,
        [row.id],
      );
      synced += 1;
    } catch (error) {
      const verdict = classifyFailure(error, row.attempts ?? 0, now);

      // Offline is not this row's fault, and the rest of the queue will fail the same way. Stop and
      // leave everything as it was, so a normal offline session does not look like broken syncs.
      if (verdict.action === 'pause') {
        break;
      }

      if (verdict.action === 'give-up') {
        await db.runAsync(
          `update sync_queue set status = 'stuck', attempts = ?, lastError = ?, nextAttemptAt = null where id = ?`,
          [verdict.attempts, verdict.reason, row.id],
        );
        stuck += 1;
        continue;
      }

      await db.runAsync(
        `update sync_queue set status = 'failed', attempts = ?, lastError = ?, nextAttemptAt = ? where id = ?`,
        [verdict.attempts, error instanceof Error ? error.message : 'Unknown sync error',
         verdict.nextAttemptAt.toISOString(), row.id],
      );
      failed += 1;
    }
  }

  return { synced, failed, stuck };
}

/**
 * Uploads the photos held for a queued write, now that the record it creates exists.
 *
 * Without this an item created in a shop with no signal arrived on the server with no photos at all,
 * silently and permanently — and photos are the one thing that cannot be recaptured, because by then
 * the item is no longer in your hands.
 *
 * A photo that fails to upload is left in place rather than deleted, so the next drain retries it.
 */
async function attachQueuedPhotos(queueId: string, inventoryItemId: string): Promise<void> {
  const held = await getPhotos(queueId);
  for (const photo of held) {
    await api.uploadInventoryPhoto(inventoryItemId, photo.localUri, photo.isHero);
    await deletePhoto(photo.id);
  }
}
