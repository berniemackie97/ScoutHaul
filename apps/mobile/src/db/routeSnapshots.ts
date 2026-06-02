import type { SourcingRouteDetail } from '../domain/types';
import { getDatabase } from './database';

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

export async function updateRouteSnapshot(
  tripId: string,
  updater: (route: SourcingRouteDetail) => SourcingRouteDetail,
): Promise<SourcingRouteDetail | null> {
  const current = await getRouteSnapshot(tripId);
  if (!current) return null;
  const next = updater(current);
  await saveRouteSnapshot(next);
  return next;
}
