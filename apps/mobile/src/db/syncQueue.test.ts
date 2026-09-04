import { ApiError } from '../api/apiError';
import { apiRequest } from '../api';
import { getDatabase } from './database';
import { processSyncQueue } from './syncQueue';
import { MAX_ATTEMPTS } from './syncQueueRules';

jest.mock('../api', () => ({ apiRequest: jest.fn() }));
jest.mock('./database', () => ({ getDatabase: jest.fn() }));

const mockRequest = apiRequest as jest.Mock;
const mockGetDatabase = getDatabase as jest.Mock;

const NOW = new Date('2026-09-03T12:00:00.000Z');

type Row = {
  id: string;
  operationType: string;
  endpoint: string;
  method: string;
  bodyJson: string;
  status: string;
  createdAt: string;
  lastError: string | null;
  attempts: number;
  nextAttemptAt: string | null;
};

function row(overrides: Partial<Row> = {}): Row {
  return {
    id: 'sync-1',
    operationType: 'CreateInventory',
    endpoint: '/api/inventory',
    method: 'POST',
    bodyJson: '{"title":"Lamp"}',
    status: 'pending',
    createdAt: '2026-09-03T11:00:00.000Z',
    lastError: null,
    attempts: 0,
    nextAttemptAt: null,
    ...overrides,
  };
}

/** Applies the `update sync_queue set ... where id = ?` statements the queue issues. */
function fakeDb(rows: Row[]) {
  const selects: { sql: string; params: unknown[] }[] = [];

  return {
    rows,
    selects,
    getAllAsync: async (sql: string, params: unknown[] = []) => {
      selects.push({ sql, params });
      const cutoff = String(params[0] ?? '');
      return rows.filter(
        (r) =>
          ['pending', 'failed'].includes(r.status) &&
          (r.nextAttemptAt === null || r.nextAttemptAt <= cutoff),
      );
    },
    getFirstAsync: async () => null,
    runAsync: async (sql: string, params: unknown[]) => {
      const id = params[params.length - 1] as string;
      const target = rows.find((r) => r.id === id);
      if (!target) return;
      if (sql.includes("status = 'synced'")) target.status = 'synced';
      if (sql.includes("status = 'stuck'")) {
        target.status = 'stuck';
        target.attempts = params[0] as number;
        target.lastError = params[1] as string;
      }
      if (sql.includes("status = 'failed'")) {
        target.status = 'failed';
        target.attempts = params[0] as number;
        target.lastError = params[1] as string;
        target.nextAttemptAt = params[2] as string;
      }
    },
  };
}

beforeEach(() => jest.clearAllMocks());

describe('processSyncQueue', () => {
  it('marks a delivered write synced', async () => {
    const db = fakeDb([row()]);
    mockGetDatabase.mockResolvedValue(db);
    mockRequest.mockResolvedValue({});

    await expect(processSyncQueue(NOW)).resolves.toEqual({ synced: 1, failed: 0, stuck: 0 });
    expect(db.rows[0].status).toBe('synced');
  });

  it('sends the row id as the idempotency key, so a retry cannot double-write', async () => {
    mockGetDatabase.mockResolvedValue(fakeDb([row({ id: 'sync-42' })]));
    mockRequest.mockResolvedValue({});

    await processSyncQueue(NOW);
    expect(mockRequest.mock.calls[0][1].idempotencyKey).toBe('sync-42');
  });

  it('stops on the first offline failure and leaves the rest untouched', async () => {
    const db = fakeDb([row({ id: 'a' }), row({ id: 'b' })]);
    mockGetDatabase.mockResolvedValue(db);
    mockRequest.mockRejectedValue(new ApiError({ reason: 'offline', message: 'offline' }));

    await expect(processSyncQueue(NOW)).resolves.toEqual({ synced: 0, failed: 0, stuck: 0 });
    // A normal offline session must not look like a pile of broken syncs.
    expect(db.rows.every((r) => r.status === 'pending')).toBe(true);
    expect(mockRequest).toHaveBeenCalledTimes(1);
  });

  it('schedules a retryable failure for later instead of straight away', async () => {
    const db = fakeDb([row()]);
    mockGetDatabase.mockResolvedValue(db);
    mockRequest.mockRejectedValue(new ApiError({ reason: 'http', status: 503, message: 'down' }));

    await expect(processSyncQueue(NOW)).resolves.toEqual({ synced: 0, failed: 1, stuck: 0 });
    expect(db.rows[0].status).toBe('failed');
    expect(db.rows[0].attempts).toBe(1);
    expect(new Date(db.rows[0].nextAttemptAt!).getTime()).toBeGreaterThan(NOW.getTime());
  });

  it('skips a row that is still inside its backoff', async () => {
    const db = fakeDb([
      row({ status: 'failed', attempts: 1, nextAttemptAt: '2026-09-03T18:00:00.000Z' }),
    ]);
    mockGetDatabase.mockResolvedValue(db);

    await expect(processSyncQueue(NOW)).resolves.toEqual({ synced: 0, failed: 0, stuck: 0 });
    expect(mockRequest).not.toHaveBeenCalled();
  });

  it('gives up on a write the server rejected, and keeps the reason', async () => {
    const db = fakeDb([row()]);
    mockGetDatabase.mockResolvedValue(db);
    mockRequest.mockRejectedValue(
      new ApiError({ reason: 'http', status: 400, message: 'Price must be positive.' }),
    );

    await expect(processSyncQueue(NOW)).resolves.toEqual({ synced: 0, failed: 0, stuck: 1 });
    expect(db.rows[0].status).toBe('stuck');
    expect(db.rows[0].lastError).toContain('Price must be positive.');
  });

  it('gives up on a retryable write once it has been tried enough times', async () => {
    // This is the row that used to be retried on every launch for the life of the install.
    const db = fakeDb([row({ status: 'failed', attempts: MAX_ATTEMPTS - 1 })]);
    mockGetDatabase.mockResolvedValue(db);
    mockRequest.mockRejectedValue(new ApiError({ reason: 'network', message: 'no route' }));

    await expect(processSyncQueue(NOW)).resolves.toEqual({ synced: 0, failed: 0, stuck: 1 });
    expect(db.rows[0].status).toBe('stuck');
  });

  it('never picks a stuck row back up on its own', async () => {
    const db = fakeDb([row({ status: 'stuck', attempts: MAX_ATTEMPTS })]);
    mockGetDatabase.mockResolvedValue(db);

    await processSyncQueue(NOW);
    expect(mockRequest).not.toHaveBeenCalled();
  });

  it('keeps going past a row it gave up on', async () => {
    const db = fakeDb([row({ id: 'bad' }), row({ id: 'good' })]);
    mockGetDatabase.mockResolvedValue(db);
    mockRequest
      .mockRejectedValueOnce(new ApiError({ reason: 'http', status: 400, message: 'nope' }))
      .mockResolvedValueOnce({});

    await expect(processSyncQueue(NOW)).resolves.toEqual({ synced: 1, failed: 0, stuck: 1 });
    expect(db.rows.find((r) => r.id === 'good')!.status).toBe('synced');
  });
});
