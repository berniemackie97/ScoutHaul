import type { SourcingRouteDetail } from '../domain/types';
import { getDatabase } from './database';
import { serializeByKey } from './serializeByKey';

export async function saveRouteSnapshot(route: SourcingRouteDetail): Promise<void> {
  const db = await getDatabase();
  await db.runAsync(
    `insert into route_snapshots (tripId, bodyJson, updatedAt) values (?, ?, ?)
    on conflict(tripId) do update set
      bodyJson = excluded.bodyJson,
      updatedAt = excluded.updatedAt`,
    [route.trip.id, JSON.stringify(route), new Date().toISOString()],
  );
}

export async function getRouteSnapshot(tripId: string): Promise<SourcingRouteDetail | null> {
  const db = await getDatabase();
  const row = await db.getFirstAsync<{ bodyJson: string }>(
    `select bodyJson from route_snapshots where tripId = ?`,
    [tripId],
  );
  return row ? JSON.parse(row.bodyJson) as SourcingRouteDetail : null;
}

/**
 * Applies a change to the stored route.
 *
 * Serialized per trip: this reads, changes and writes across three awaits, so two overlapping calls
 * would both read the same snapshot and the second write would discard the first. Offline, that is
 * a stop marked visited or a find logged that simply vanishes.
 */
export function updateRouteSnapshot(
  tripId: string,
  updater: (route: SourcingRouteDetail) => SourcingRouteDetail,
): Promise<SourcingRouteDetail | null> {
  return serializeByKey(`route:${tripId}`, async () => {
    const current = await getRouteSnapshot(tripId);
    if (!current) return null;
    const next = updater(current);
    await saveRouteSnapshot(next);
    return next;
  });
}
