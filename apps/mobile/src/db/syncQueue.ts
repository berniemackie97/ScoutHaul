import { ApiError, apiRequest } from '../api';
import { createLocalId } from '../utils/id';
import { getDatabase } from './database';

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

export async function enqueueOperation(
  operationType: string,
  endpoint: string,
  method: SyncQueueRow['method'],
  body: unknown,
): Promise<void> {
  const db = await getDatabase();
  const id = createLocalId('sync');
  await db.runAsync(
    `insert into sync_queue (id, operationType, endpoint, method, bodyJson, status, createdAt, lastError)
      values (?, ?, ?, ?, ?, 'pending', ?, null)`,
    [id, operationType, endpoint, method, JSON.stringify(body), new Date().toISOString()],
  );
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
        idempotencyKey: row.id,
      });
      await db.runAsync(`update sync_queue set status = 'synced', lastError = null where id = ?`, [row.id]);
      synced += 1;
    } catch (error) {
      // Being offline is not a failed write — the queue exists precisely for this. Marking every
      // row failed here made a normal offline session look like a pile of broken syncs, so stop
      // and leave the rest pending for the next attempt.
      if (error instanceof ApiError && error.reason === 'offline') {
        break;
      }

      const message = error instanceof Error ? error.message : 'Unknown sync error';
      await db.runAsync(`update sync_queue set status = 'failed', lastError = ? where id = ?`, [message, row.id]);
      failed += 1;
    }
  }

  return { synced, failed };
}
