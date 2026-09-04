import { MigrationError, migrations, readSchemaVersion, runMigrations, type Migration } from './migrations';

/** A stand-in that records what it was asked to run and honours `pragma user_version`. */
function fakeDb(startingVersion = 0, failOn?: string) {
  const executed: string[] = [];
  let version = startingVersion;

  const self = {
    executed,
    get version() {
      return version;
    },
    /** Mirrors expo-sqlite: the body runs, and a throw aborts without committing. */
    withTransactionAsync: async (task: () => Promise<void>): Promise<void> => {
      const before = version;
      try {
        await task();
      } catch (error) {
        version = before;
        throw error;
      }
    },
    getFirstAsync: async <T>(sql: string): Promise<T | null> => {
      if (sql.includes('user_version')) return { user_version: version } as T;
      return null;
    },
    execAsync: async (sql: string): Promise<void> => {
      executed.push(sql);
      if (failOn && sql.includes(failOn)) throw new Error('constraint failed');
      const bump = /pragma user_version = (\d+)/.exec(sql);
      if (bump) version = Number(bump[1]);
    },
  };
  return self;
}

const fake = (name: string): Migration => ({ name, up: `create table ${name} (id text);` });

describe('runMigrations', () => {
  it('brings a brand-new database all the way up', async () => {
    const db = fakeDb(0);
    const ended = await runMigrations(db, [fake('a'), fake('b'), fake('c')]);

    expect(ended).toBe(3);
    expect(db.version).toBe(3);
  });

  it('runs only what a partly-migrated database is missing', async () => {
    const db = fakeDb(2);
    await runMigrations(db, [fake('a'), fake('b'), fake('c')]);

    // The first two already ran on this install and must never run twice.
    expect(db.executed.some((sql) => sql.includes('create table a'))).toBe(false);
    expect(db.executed.some((sql) => sql.includes('create table b'))).toBe(false);
    expect(db.executed.some((sql) => sql.includes('create table c'))).toBe(true);
    expect(db.version).toBe(3);
  });

  it('does nothing when the database is already current', async () => {
    const db = fakeDb(3);
    await runMigrations(db, [fake('a'), fake('b'), fake('c')]);

    expect(db.executed).toHaveLength(0);
  });

  it('applies migrations in order', async () => {
    const db = fakeDb(0);
    await runMigrations(db, [fake('a'), fake('b'), fake('c')]);

    const order = db.executed.map((sql) => /create table (\w+)/.exec(sql)?.[1]).filter(Boolean);
    expect(order).toEqual(['a', 'b', 'c']);
  });

  it('bumps the version only after the migration in the same transaction', async () => {
    const db = fakeDb(0);
    await runMigrations(db, [fake('a')]);

    // The migration body runs before the version is recorded, and both sit inside one transaction.
    expect(db.executed[0]).toContain('create table a');
    expect(db.executed[1]).toContain('user_version = 1');
  });

  it('stops at the last version that fully applied when one fails', async () => {
    const db = fakeDb(0, 'create table b');

    await expect(runMigrations(db, [fake('a'), fake('b'), fake('c')])).rejects.toBeInstanceOf(
      MigrationError,
    );
    // 'a' committed, 'b' did not, and 'c' must not have been attempted on a half-migrated database.
    expect(db.version).toBe(1);
    expect(db.executed.some((sql) => sql.includes('create table c'))).toBe(false);
  });

  it('names the migration that failed', async () => {
    const db = fakeDb(0, 'create table b');
    const error = await runMigrations(db, [fake('a'), fake('b')]).catch((e) => e);

    expect(error).toBeInstanceOf(MigrationError);
    expect((error as MigrationError).migration).toBe('b');
    expect((error as MigrationError).message).toContain('constraint failed');
  });

  it('leaves a database written by a newer build alone', async () => {
    // Downgrading cannot be done correctly, and guessing would destroy data.
    const db = fakeDb(9);
    const ended = await runMigrations(db, [fake('a')]);

    expect(ended).toBe(9);
    expect(db.executed).toHaveLength(0);
  });
});

describe('readSchemaVersion', () => {
  it('treats a database that has never been migrated as version zero', async () => {
    const db = {
      getFirstAsync: async () => null,
      execAsync: async () => {},
      withTransactionAsync: async (task: () => Promise<void>) => task(),
    };
    await expect(readSchemaVersion(db)).resolves.toBe(0);
  });
});

describe('the shipped migration list', () => {
  it('has a baseline that is safe to run against a database that predates the runner', () => {
    // An existing install sits at user_version 0 with every table already there, so the baseline
    // must be a no-op for it — which is only true while every statement is `if not exists`.
    const baseline = migrations[0];
    const creates = baseline.up.match(/create table[^(]*/gi) ?? [];

    expect(creates).not.toHaveLength(0);
    for (const statement of creates) {
      expect(statement.toLowerCase()).toContain('if not exists');
    }
  });

  it('gives every migration a distinct name', () => {
    const names = migrations.map((migration) => migration.name);
    expect(new Set(names).size).toBe(names.length);
  });
});
