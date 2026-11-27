# Runbook: the `geo_entity` table-rewrite migrations (XD01-467)

Two `GeoAssetsDbContext` migrations add a STORED generated geometry column to `geo_entity`. Adding one rewrites the
whole table under an `ACCESS EXCLUSIVE` lock, so nothing reads or writes `geo_entity` until it commits. On a populated
database, apply them by hand, in a maintenance window, **before** you roll the kernel image that carries them. Don't let
the kernel apply them at startup.

| Migration | Ticket | What it does to `geo_entity` | Lock |
|---|---|---|---|
| `20261126221155_AddGeoEntityGeom3857` | XD01-432 | Two rewrites in one transaction: `geom` becomes `geometry(Geometry,4326)` (`ALTER COLUMN … TYPE`), then the generated `geom_3857` is added. Then the GiST index `IX_geo_entity_geom_3857` is built. | `ACCESS EXCLUSIVE`, from the first statement to the commit |
| `20261126234247_AddGeoEntityTopologyIndex` | XD01-449 | Builds the GIN index `IX_geo_entity_topology`. No rewrite. | `SHARE` while the index builds: writes wait, reads don't |
| `20261127112047_AddGeoEntityGeom3857Centroid` | XD01-457 | One rewrite: the generated `geom_3857_centroid` is added. Then the GiST index `IX_geo_entity_geom_3857_centroid` is built. | `ACCESS EXCLUSIVE`, from the first statement to the commit |

Every rewrite also rebuilds **every** index on `geo_entity`: 12 before XD01-432, 15 after XD01-457, including three
trigram/jsonb GIN indexes. That rebuild is most of the cost, not the geometry expression. On 1 M rows, natively, a
bare `VACUUM FULL geo_entity` (a rewrite with no new column) takes 26.5 s, and each rewrite statement above takes
27–28 s.

A database that is still empty when it is migrated pays nothing. A new silo is like that: `provision-database.sh` runs
before the network import. The runbook is for a database that already holds features when one of these migrations
reaches it, such as the shared kernel's database or a silo that was imported before XD01-457.

## Why not at startup

The kernel migrates `GeoAssetsDbContext` before it listens. `Program.cs` awaits `InitializeCatalogPoolAsync()`, which
builds each `postgres` catalog through `PostgresProviderFactory.CreateAsync`, and that calls `MigrateAsync`. Until it
returns, `/healthz` doesn't answer.

- **The command timeout is the connection string's.** It is Npgsql's 30 s by default. One timeout covers a whole
  migration's SQL batch, not each statement in it. At 1 M rows, XD01-432's batch needs about 57 s and XD01-457's
  about 30 s, even on native hardware.
- **A timeout is retried.** The factory registers `EnableRetryOnFailure()`, and Npgsql reports a timeout as transient.
  So EF Core starts the migration again: 1 attempt plus 6 retries. Each attempt takes the `ACCESS EXCLUSIVE` lock for
  30 s, is cancelled, and rolls back.
- **What we measured.** On the 1 M-row bench, native arm64, with the default timeout, the kernel's own path
  (`PostgresProviderFactory.CreateAsync`) failed after **4 min 29 s** with `RetryLimitExceededException` (inner
  `TimeoutException`). It blocked `geo_entity` for about 30 s at a time, seven times, then exited. Nothing was applied:
  the history and the schema were unchanged, and the disk was freed.
- **Raising `Command Timeout` in the kernel's connection string is not the fix.** It would raise the timeout of every
  runtime query on that catalog as well. The startup would still hold the table for the whole migration, with no
  listener, under the container's health check (`start_period` 30 s, then 10 × 10 s).

Apply the migrations by hand first. The new image's `MigrateAsync` then finds nothing pending and returns at once.

## Measured cost

Throwaway PostGIS containers, never a running `geoasset-*` stack. The data is the render bench's deterministic dataset
(`tools/GeoAssets.Bench.Render seed`: 70 % points, 20 % lines, 10 % polygons; 16.8 M vertices per million rows). It was
seeded at the pre-XD01-432 schema, then `VACUUM (ANALYZE)` and `CHECKPOINT`. Each migration was applied with
`dotnet ef database update <migration>` and `Command Timeout=0`. PostgreSQL's settings were the image defaults
(`shared_buffers` 128 MB, `maintenance_work_mem` 64 MB, `max_wal_size` 1 GB), with `--shm-size=1g`. Docker Desktop ran
on an Apple M5 with 8 vCPUs and 7.6 GiB of RAM.

How each figure was taken:

- **Lock**: how long `pg_locks` showed the `ACCESS EXCLUSIVE` lock on `geo_entity` granted, sampled every 0.25 s. A
  probe `SELECT` started during the migration waited for the same span.
- **Extra disk**: the peak of `pg_database_size` during the migration, minus its value before. It excludes `pg_wal`.
- **WAL**: `pg_wal_lsn_diff` across the migration.

| Image | Rows | `geo_entity` before → after (heap + indexes + TOAST) | Migration | Wall (`dotnet ef`) | `ACCESS EXCLUSIVE` held | Extra disk at peak | WAL written |
|---|---|---|---|---|---|---|---|
| `imresamu/postgis:16-3.4`, arm64 native | 200,000 | 225 → 289 MiB | XD01-432 | 13.0 s | 10.5 s | +481 MiB (2.1×) | 429 MiB |
| | | 289 → 289 MiB | topology index | 1.5 s | none (`SHARE` while it builds) | +0 | 0.2 MiB |
| | | 289 → 304 MiB | XD01-457 | 6.9 s | 5.2 s | +304 MiB (1.05×) | 258 MiB |
| `imresamu/postgis:16-3.4`, arm64 native | 1,000,000 | 1,070 → 1,415 MiB | XD01-432 | 57.4 s | 55.5 s | +2,370 MiB (2.2×) | 2,141 MiB |
| | | 1,415 → 1,416 MiB | topology index | 2.2 s | none (`SHARE` while it builds) | +1 MiB | 1 MiB |
| | | 1,416 → 1,489 MiB | XD01-457 | 31.0 s | 28.5 s | +1,482 MiB (1.05×) | 1,288 MiB |
| `postgis/postgis:16-3.5` (deploy/server's image), amd64 emulated | 1,000,000 | 1,071 → 1,409 MiB | XD01-432 | 63.7 s | 62.0 s | +2,363 MiB (2.2×) | 2,141 MiB |
| | | 1,409 → 1,410 MiB | topology index | 1.7 s | none | +1 MiB | 1.5 MiB |
| | | 1,410 → 1,483 MiB | XD01-457 | 42.8 s | 40.5 s | +1,472 MiB (1.04×) | 1,288 MiB |

The PostgreSQL log breaks the 1 M runs down by statement:

| Statement | Native | Emulated |
|---|---|---|
| XD01-432 `ALTER COLUMN geom TYPE` | 27.5 s | 27.8 s |
| XD01-432 `ADD COLUMN geom_3857` | 27.2 s | 33.1 s |
| XD01-432 `CREATE INDEX` | 1.0 s | 1.4 s |
| XD01-457 `ADD COLUMN geom_3857_centroid` | 27.9 s | 39.8 s |
| XD01-457 `CREATE INDEX` | 0.8 s | 1.2 s |

A field data point, from `deploy/distributor-silo/README.md`: XD01-432 took about 1 min 40 s on COELBA's 784 k
features, on the emulated image. That is about 2 min per million rows, twice the bench. Real features carry more
attributes and vertices than the bench's.

**Expected duration per million rows**, which is linear in rows (200 k → 1 M scaled by 5.3× and 5.5×):

| | Bench, native (arm64, or amd64 on amd64) | Bench, emulated amd64 on Apple Silicon (the local deploy/server stack) | COELBA, emulated (field) |
|---|---|---|---|
| XD01-432 | ~1 min, all of it locked | ~1 min 5 s, all of it locked | ~2 min 10 s |
| topology index | ~2 s, writes blocked | ~2 s, writes blocked | — |
| XD01-457 | ~30 s, all of it locked | ~45 s, all of it locked | — |

On unmeasured hardware or real data, plan the window at three times the bench figures (the field figure plus a
margin), plus the backup, plus `ANALYZE`. The bench's features carry empty topologies (`[]`), so a real network's
topology index takes longer than the 2 s above. That index doesn't lock reads.

**Disk.** The old files are kept until the migration's transaction commits.

- XD01-432 keeps three copies of the table (original, retyped, with `geom_3857`) until it commits. It needs **2.3 ×**
  the table's current total size free.
- XD01-457 needs **1.1 ×**.
- On top of that, PostgreSQL needs room for WAL. Without archiving or a replication slot, `pg_wal` stays at
  `max_wal_size` (1 GB by default). With either one, it grows by the WAL figure above (about 2.1 GB and 1.3 GB per
  million rows) until the archive or replica catches up.
- On Docker Desktop, the free space is the Linux VM's disk, not the Mac's. A full VM disk turns the VM read-only.

## Procedure

Run these steps once per database: the shared kernel's `geoassets`, then each silo's `geoassets_<company>`. The
example connection is the local `deploy/server` Postgres on `localhost:5433`. Use the table owner or a superuser
(`postgres`). `geo_entity` has `FORCE ROW LEVEL SECURITY`, so a non-superuser role counts zero rows.

```bash
export CONNECTION="Host=localhost;Port=5433;Database=geoassets_coelba;Username=postgres;Password=<POSTGRES_PASSWORD>"
psql_db() { docker exec -e PGPASSWORD PostgresL psql -v ON_ERROR_STOP=1 -U postgres -d geoassets_coelba "$@"; }
```

### 1. Pre-checks, before the window

Check which migrations are pending. They must be ones you built the new image from:

```bash
dotnet ef migrations list \
  --project providers/GeoAssets.Provider.PostgreSQL --startup-project providers/GeoAssets.Provider.PostgreSQL \
  --context GeoAssetsDbContext --connection "$CONNECTION"          # "(Pending)" marks the ones still to apply
```

Check the size and the disk the rewrites need:

```sql
SELECT (SELECT count(*) FROM geo_entity)                                        AS rows,
       pg_size_pretty(pg_relation_size('geo_entity'))                           AS heap,
       pg_size_pretty(pg_indexes_size('geo_entity'))                            AS indexes,
       pg_size_pretty(pg_total_relation_size('geo_entity'))                     AS table_total,
       pg_size_pretty(pg_database_size(current_database()))                     AS database,
       pg_size_pretty((2.3 * pg_total_relation_size('geo_entity'))::bigint)     AS free_needed_if_xd01_432_pending,
       pg_size_pretty((1.1 * pg_total_relation_size('geo_entity'))::bigint)     AS free_needed_if_only_xd01_457_pending,
       current_setting('max_wal_size')                                          AS max_wal_size,
       current_setting('archive_mode')                                          AS archive_mode,
       (SELECT count(*) FROM pg_replication_slots)                              AS replication_slots;
```

PostgreSQL has no SQL function for free disk, so measure it on the data volume:

```bash
docker exec PostgresL df -h /var/lib/postgresql/data      # local stack: the Docker VM's disk
# managed PostgreSQL: the provider's free-storage metric
```

Compare the result with `free_needed_*`. Add `max_wal_size`, or the WAL figure if `archive_mode` is on or a slot
exists. Don't start with less.

Estimate the window from the row count and the per-million figures above.

### 2. Back up

This is the restore point if anything goes wrong after a migration has committed. If the migration fails, its
transaction rolls back by itself.

```bash
docker exec -e PGPASSWORD PostgresL pg_dump -U postgres -Fc -d geoassets_coelba -f /tmp/geoassets_coelba-pre-xd01-467.dump
docker cp PostgresL:/tmp/geoassets_coelba-pre-xd01-467.dump . && docker exec PostgresL rm /tmp/geoassets_coelba-pre-xd01-467.dump
```

On a managed database, take a storage snapshot instead.

### 3. Open the window and freeze writes

The kernel (or the silo) is the only writer of `geo_entity`, and the network import CLI writes through it. So stopping
the container freezes the writes and drains the readers in one step.

```bash
# Stop any running import first: tools/GeoAssets.Tools.NetworkImport resumes from its checkpoint later.
docker compose -f deploy/server/docker-compose.yaml stop server                 # the shared kernel, or a silo:
docker compose -f deploy/distributor-silo/docker-compose.yaml --env-file deploy/distributor-silo/coelba.env stop
```

Then check that nothing else still holds the table. This query should return no rows:

```sql
SELECT pid, usename, application_name, state, now() - xact_start AS in_transaction, left(query, 80) AS query
FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid() AND state <> 'idle';
```

### 4. Apply the migrations by hand

Run them with no command timeout. Add a `lock_timeout`, so that if a session you missed holds the table, the migration
fails at once. Without one, it queues, and everything that arrives after it queues behind it. We checked this: with
another session holding `geo_entity`, the command below failed after 11.6 s with `55P03: canceling statement due to
lock timeout`, and applied nothing.

```bash
dotnet ef database update \
  --project providers/GeoAssets.Provider.PostgreSQL --startup-project providers/GeoAssets.Provider.PostgreSQL \
  --context GeoAssetsDbContext \
  --connection "$CONNECTION;Command Timeout=0;Options=-c lock_timeout=10s"
```

- **The timeout.** `Command Timeout=0` means no limit. Use a finite value, such as `Command Timeout=1800`, if you'd
  rather have it fail than overrun the window. A failure rolls back the migration that was running. The ones that
  committed before it stay applied: each migration commits on its own. So the disk peak is the largest single
  migration's, not their sum: the three applied in one run peaked at +2,370 MiB on 1 M rows, which is XD01-432's alone.
- **A silo.** `deploy/distributor-silo/provision-database.sh <company>` runs the same command, and then the other three
  kernel chains. Its `MIGRATION_COMMAND_TIMEOUT` environment variable sets `Command Timeout`: default 1800 s, `0` means
  no limit. Stop the silo before you run it. A running silo keeps its readers on the table, and they wait for the
  whole rewrite.
- **Progress.** You can watch it from another session:

  ```sql
  SELECT pid, now() - query_start AS running, wait_event_type, wait_event, left(query, 60) FROM pg_stat_activity
  WHERE datname = current_database() AND query ILIKE 'ALTER TABLE geo_entity%';
  ```

### 5. Verify

All three migrations should be applied:

```sql
SELECT "MigrationId" FROM "__EFMigrationsHistory"
WHERE "MigrationId" IN ('20261126221155_AddGeoEntityGeom3857', '20261126234247_AddGeoEntityTopologyIndex',
                        '20261127112047_AddGeoEntityGeom3857Centroid')
ORDER BY 1;                                                                     -- 3 rows
```

Check the columns. Expect `geom geometry(Geometry,4326)` (not generated), `geom_3857 geometry(Geometry,3857) s` and
`geom_3857_centroid geometry(Point,3857) s`, where `s` means stored generated:

```sql
SELECT attname, format_type(atttypid, atttypmod) AS type, attgenerated
FROM pg_attribute
WHERE attrelid = 'geo_entity'::regclass AND attname IN ('geom', 'geom_3857', 'geom_3857_centroid') AND NOT attisdropped
ORDER BY attnum;
```

Check the indexes. All four must be valid and ready:

```sql
SELECT c.relname, am.amname, i.indisvalid, i.indisready, pg_size_pretty(pg_relation_size(c.oid)) AS size
FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid JOIN pg_am am ON am.oid = c.relam
WHERE i.indrelid = 'geo_entity'::regclass
  AND c.relname IN ('ix_geo_entity_geom', 'IX_geo_entity_geom_3857', 'IX_geo_entity_topology', 'IX_geo_entity_geom_3857_centroid');
```

Check the generated values on a ~1 % sample. Every count except `sampled` must be 0:

```sql
SELECT count(*)                                                                               AS sampled,
       count(*) FILTER (WHERE ST_SRID(geom) <> 4326)                                          AS srid_not_4326,
       count(*) FILTER (WHERE geom IS NOT NULL AND (geom_3857 IS NULL OR geom_3857_centroid IS NULL)) AS not_backfilled,
       count(*) FILTER (WHERE ST_YMin(geom) >= -85.0511287798066 AND ST_YMax(geom) <= 85.0511287798066
                          AND NOT ST_Equals(geom_3857, ST_Transform(geom, 3857)))              AS geom_3857_differs,
       count(*) FILTER (WHERE NOT ST_Equals(geom_3857_centroid, ST_Centroid(geom_3857)))       AS centroid_differs
FROM geo_entity TABLESAMPLE SYSTEM (1);
```

Refresh the planner statistics. A rewrite doesn't collect any for the new columns, and the tile queries plan on them:

```sql
ANALYZE geo_entity;                                                             -- 0.5 s on 1 M rows
```

Finally, `dotnet ef migrations list` (step 1) should list nothing as `(Pending)`.

### 6. Roll the new image

Start the kernel (or the silo) on the new image. Its startup `MigrateAsync` finds nothing pending. Then check it:

```bash
docker compose -f deploy/server/docker-compose.yaml up --build -d server         # the shared kernel, or a silo:
docker compose -f deploy/distributor-silo/docker-compose.yaml --env-file deploy/distributor-silo/coelba.env up --build -d
curl -fsS http://127.0.0.1:8080/healthz          # the shared kernel; a silo answers on its SILO_PORT (8180 for COELBA)
```

Writes are open again. A tile read now uses `IX_geo_entity_geom_3857`, which you can confirm with
`EXPLAIN` on a `geom_3857 && ST_TileEnvelope(…)` filter.

## Rollback

- **A migration failed or was cancelled.** Its transaction rolled back. The schema and the history are as they were
  before it, and its extra disk is freed. We checked this after the 4 min 29 s startup failure above. Fix the cause,
  such as disk, a lock or the timeout, and run step 4 again. Migrations that committed before it stay applied.
- **The new image misbehaves, but the schema is fine.** Roll back the image alone. An image from before XD01-432 or
  XD01-457 should run on the migrated schema: EF Core doesn't map `geom_3857` or `geom_3857_centroid`, PostgreSQL fills
  them on every write, and an older image's `MigrateAsync` only applies migrations it knows and finds pending. You
  don't need `Down` for this. (This follows from the code; XD01-467 did not start an older image to prove it.)
- **The schema must go back.** Run `Down` with `dotnet ef database update <migration before>`, in the window, with
  writes frozen:
  - `dotnet ef database update AddGeoEntityTopologyIndex` reverts XD01-457. It drops the index and the column. A
    dropped column is only marked dropped, so this is fast and frees nothing until the next rewrite.
  - `dotnet ef database update AddElectricRolesToSeededAssetTypes` also reverts the topology index and XD01-432. That
    `Down` changes `geom` back to an untyped `geometry`. That is no rewrite: both reverts took 1.1 s each on 1 M rows.
  - `Down` cannot restore the SRID: rows that XD01-432 reprojected or stamped with 4326 stay 4326.
- **Restore.** `pg_restore --clean --if-exists -d geoassets_coelba geoassets_coelba-pre-xd01-467.dump`, or the
  snapshot, with the kernel stopped. This brings back the exact pre-window state. Any write made after step 3 is lost,
  and none should exist.

## The lower-lock alternative: rejected

The standard way to avoid the lock has four steps. Add a plain, nullable column, which only changes the catalog. Keep it
with a `BEFORE INSERT OR UPDATE` trigger. Backfill it in committed batches. Then `CREATE INDEX CONCURRENTLY`.

We ran it against the native 1 M-row copy, at the post-XD01-432 state, for `geom_3857_centroid`: 10,000-row
keyset batches, each committed.

| | XD01-457 as shipped (stored generated column) | Trigger + batched backfill + `CREATE INDEX CONCURRENTLY` |
|---|---|---|
| `ACCESS EXCLUSIVE` held | 28.5 s | ~10 ms (`ADD COLUMN`, `CREATE TRIGGER`) |
| Other blocking | none | each batch row-locks 10,000 rows while it updates them |
| Wall time | 31.0 s | 2 min 6 s backfill + 4.7 s index = **~2 min 11 s** |
| WAL written | 1,288 MiB | **7,252 MiB** (5.6×) |
| `geo_entity` heap after | 1,027 MiB | **1,756 MiB**. Every row was updated once, and `VACUUM` doesn't shrink it, so it stays bloated until a rewrite. |
| Total size after | 1,489 MiB | **2,711 MiB** |

We rejected it, for four reasons:

1. **It changes the schema and the behaviour.** A trigger-kept column is not a generated column. A writer could set
   it, `pg_dump` and EF Core see a different table, and `ALTER COLUMN geom TYPE` would need the trigger kept in step.
   XD01-432 is already applied wherever it ran (the local kernel, COELBA's silo per its README), and so is XD01-457
   on every database migrated since it merged. A second shape of the same column, on only some databases, is the drift
   that the tile code and future migrations must not have to handle.
2. **It costs far more.** It is 4.2× slower in total, writes 5.6× the WAL, and leaves a table 1.8× the size. That is
   the wrong trade for a table that is rewritten once.
3. **The cheaper half doesn't pay.** The only part of the shipped migrations that could move out of the lock without
   changing the schema is the GiST build. You could pre-create the index `CONCURRENTLY` and make the migration's
   `CREATE INDEX IF NOT EXISTS`. But that is 1.0 s and 0.8 s of 55.5 s and 28.5 s: 2–3 %. The rest is the rewrite and
   the rebuild of every other index (compare `VACUUM FULL` at 26.5 s). It would also add a trap: a failed
   `CREATE INDEX CONCURRENTLY` leaves an `INVALID` index under the same name, and `IF NOT EXISTS` would accept it
   silently.
4. **A window is short anyway.** It is about a minute per million rows on native hardware, and nothing at all for a
   database migrated before its import (every new silo).

So the migrations stay exactly as they are. A future migration that must add a column to `geo_entity` should be a
plain column or an index built `CONCURRENTLY` in a migration of its own (`migrationBuilder.Sql(…, suppressTransaction:
true)`), not another stored generated column. If it has to be one, it gets this runbook.

## Followed end to end (XD01-467)

We followed this runbook end to end on a throwaway, realistically sized copy: the 1 M-row bench database on native
arm64 PostGIS. Every step ran as written, against a copy at the pre-XD01-432 state:

| Step | Result |
|---|---|
| 1. Pre-checks | 3 pending; 1,000,000 rows, table 1,070 MB; `free_needed_if_xd01_432_pending` 2,461 MB against 13 GB free in the Docker VM; no archiving, no slot |
| 2. Back up | `pg_dump -Fc` 16.3 s, 288 MB |
| 3. Freeze | `pg_stat_activity` query: 0 rows |
| 4. Apply | one `dotnet ef database update`, all three migrations: 58.9 s wall, `ACCESS EXCLUSIVE` 57.2 s, peak +2,370 MiB, WAL 3.35 GiB. This is quicker than the separate runs above, because the backup had just read the table into the cache. A probe read waited 33.6 s, until XD01-432 committed. |
| 5. Verify | 3 history rows; `geom geometry(Geometry,4326)`, `geom_3857 … s`, `geom_3857_centroid … s`; the 4 indexes valid and ready; sample of 10,147 rows with every check at 0; `ANALYZE` 0.5 s; nothing pending |
| 6. Roll the image | the kernel's startup path (`PostgresProviderFactory.CreateAsync`, default 30 s timeout) migrated in 0.4 s; a `geom_3857 && ST_TileEnvelope(…)` count uses `IX_geo_entity_geom_3857` |
| Rollback by `Down` | to `AddGeoEntityTopologyIndex` 1.1 s, then to `AddElectricRolesToSeededAssetTypes` 1.1 s; `geom` untyped again, no `geom_3857*` column, every SRID still 4326 |
| Rollback by restore | `pg_restore` into a fresh database 18.5 s; 1,000,000 rows, history at `AddElectricRolesToSeededAssetTypes` |
| `lock_timeout` | with another session holding `geo_entity`, step 4's command failed after 11.6 s (`55P03`), nothing applied |

## Still owed

We have not yet followed this runbook against a non-local database of a real silo's size. That needs a managed or
remote PostgreSQL holding a copy of a silo's `geo_entity`, and this environment has neither. The silos' own data stayed
untouched by design. Do it before the first deploy outside a developer's machine, and record the numbers here.
