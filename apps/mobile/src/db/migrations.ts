/**
 * Versioned schema migrations for the on-device database.
 *
 * The schema used to be a block of `create table if not exists`, which only ever builds a database
 * that has never existed. On a phone that already has the app, adding a column to an existing table
 * is a no-op — the table is there, so the statement does nothing — and every query using the new
 * column fails at runtime, on exactly the installs that hold the user's unsynced work.
 *
 * SQLite's own `user_version` records how far a database has been brought. Each migration runs once,
 * in order, and the version only moves after it succeeds.
 */

export type Migration = {
  /** Only for logs and errors; ordering comes from position, never from this. */
  readonly name: string;
  readonly up: string;
};

/**
 * Append-only. Never edit or reorder a migration that has shipped — an install that already ran it
 * will not run it again, so a change here only ever applies to fresh installs, and the two diverge.
 */
export const migrations: readonly Migration[] = [
  {
    // Baseline. Every statement is `if not exists`, so an install that predates this runner applies
    // it as a no-op and simply lands on version 1 with the schema it already had.
    name: '001-baseline',
    up: `
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
    `,
  },
];

/** The slice of expo-sqlite this runner needs — small enough for a test to stand in for. */
export type MigratableDatabase = {
  getFirstAsync<T>(sql: string): Promise<T | null>;
  execAsync(sql: string): Promise<void>;
  /** expo-sqlite's own transaction helper, so the begin/commit handling is not ours to get wrong. */
  withTransactionAsync(task: () => Promise<void>): Promise<void>;
};

export class MigrationError extends Error {
  constructor(
    readonly migration: string,
    readonly cause: unknown,
  ) {
    super(`Migration ${migration} failed: ${cause instanceof Error ? cause.message : String(cause)}`);
    this.name = 'MigrationError';
  }
}

/** How far this database has already been brought. */
export async function readSchemaVersion(db: MigratableDatabase): Promise<number> {
  const row = await db.getFirstAsync<{ user_version: number }>('pragma user_version;');
  return row?.user_version ?? 0;
}

/**
 * Brings the database up to the latest migration and returns the version it ended on.
 *
 * Each migration and the version bump go in one transaction, so a failure leaves the database on the
 * last version that fully applied rather than half-way through. A database from a newer build than
 * this one is left alone: downgrading is not something we can do correctly, and guessing would
 * destroy data.
 */
export async function runMigrations(
  db: MigratableDatabase,
  pending: readonly Migration[] = migrations,
): Promise<number> {
  const current = await readSchemaVersion(db);

  if (current > pending.length) {
    // An older build opening a database a newer one wrote. Refuse rather than "migrate" backwards.
    return current;
  }

  for (let version = current; version < pending.length; version += 1) {
    const migration = pending[version];
    try {
      await db.withTransactionAsync(async () => {
        await db.execAsync(migration.up);
        // Inside the same transaction: a migration that applied but did not record itself would run
        // again on the next launch.
        await db.execAsync(`pragma user_version = ${assertVersion(version + 1)};`);
      });
    } catch (error) {
      throw new MigrationError(migration.name, error);
    }
  }

  return pending.length;
}

/** `pragma user_version` cannot take a bound parameter, so the value is checked before interpolating. */
function assertVersion(version: number): number {
  if (!Number.isInteger(version) || version < 0) {
    throw new Error(`Refusing to write a non-integer schema version: ${version}`);
  }
  return version;
}
