# XD01-405 — Grant-sync prune query plan at production volume

XD01-400 added `IMunicipioGrantSyncStore.PruneAsync` (`EFMunicipioGrantSyncOutbox`), which `MunicipioGrantSyncPruner` runs
every `GrantSync:PruneInterval` (1 h) in batches of `GrantSync:PruneBatchSize` (500). Each batch selects up to 500 ids of
finished requests that a later request of the same município superseded, `ORDER BY "Id" LIMIT 500`, and deletes them by id.
It shipped without a new index and without an `EXPLAIN`. This report measures the plan PostgreSQL actually picks.

**Result: no sequential scan in any case, so no index or migration ships.** Every case walks the primary key in `Id` order
and checks "a later request exists" with an index-only probe of the existing `(MunicipioId, Id)` index. The worst case is a
run with nothing (or little) to prune, at about 2–2.5 ms.

## Method

The test is `tests/GeoAssets.IpBilling.EFCore.Tests/EFMunicipioGrantSyncOutboxPrunePlanTests.cs`. It uses Testcontainers
(`postgres:16`, 16.15, the shared `IpBillingPostgres` container, one new database per test) and the real
`IpBillingDbContext` migration chain. It never touches the local compose stack. It asserts on the plan shape only, never on
time:

- no `Seq Scan`;
- an `Index Scan using "PK_MunicipioGrantSyncRequests"` for the outer walk;
- a probe of `"IX_MunicipioGrantSyncRequests_MunicipioId_Id"` for the semi-join.

Two things keep the measurement faithful to production:

- **The SQL is what EF sends.** The test runs the real `PruneAsync` once on a context whose connection has `auto_explain`
  loaded (`log_min_duration = 0`, `log_analyze`, `log_buffers`, `log_timing`, `log_level = notice`). Postgres plans and
  executes each statement exactly as EF sent it, with its bound parameters, and returns the `EXPLAIN (ANALYZE, BUFFERS)`
  output as a notice. The batch runs inside a transaction that is rolled back, so the data stays as seeded. Nothing is
  re-typed, inlined or approximated.
- **Statistics:** `VACUUM ANALYZE "MunicipioGrantSyncRequests"` runs after seeding, and again after draining. The planner
  therefore works from statistics for the full table, not from an empty or brand-new one.

### Dataset (70,210 rows, deterministic)

"Now" is 2026-11-20 12:00 UTC. `CompletedRetention` is the 30-day default (cutoff 2026-10-21 12:00). For the
`FailedRetention` cases it is 90 days (cutoff 2026-08-22 12:00). Rows are inserted in time order, so ids grow with
`RequestedAt`, as they do in production.

| Rows | What |
|---:|---|
| 55,700 | Backlog: 10 completed requests per município, 370 to 100 days ago, for all 5,570 municípios. |
| 2,785 | Odd municípios: one completed request 60 days ago. This is their latest request, which must be kept. |
| 11,140 | Even municípios: four completed requests in the last 30 days (the retention window). |
| 222 | Every 25th município: a request that failed 120 days ago and was superseded later. |
| 223 | The 12th of every 25: a request that failed 45 days ago. |
| 140 | The 7th of every 40: a pending request from the last hour. |

**Steady state** is the dataset after the real `PruneAsync` has drained it batch by batch, with completed retention only,
then vacuumed. 14,287 rows remain: the retention window, each município's kept latest request, and every failed and pending
request.

## Results

The timings are the `auto_explain` execution durations, median of 5 runs. They were measured on a shared development
laptop under load (load average about 10, Docker Desktop), with all pages in shared buffers.

| Case | Rows matched | Plan | Rows walked on the PK | Buffers (shared hit) | Median (ms) |
|---|---:|---|---:|---:|---:|
| Full backlog | 500 | Limit → Nested Loop Semi Join (PK Index Scan, Index Only Scan `(MunicipioId, Id)`) | 500 | 2,014 | 0.46 |
| Full backlog, `FailedRetention` set | 500 | same | 500 | 2,014 | 0.46 |
| Steady state, nothing due | 0 | same | 2,562 kept (+11,725 removed by filter) | 7,997 | 2.10 |
| Steady state, `FailedRetention` set (222 failed rows due) | 222 | same | 2,784 (+11,503 removed by filter) | 8,664 | 2.44 |

The delete that follows a batch (`"Id" = ANY(@ids)` plus the finished re-check) is an `Index Scan using
"PK_MunicipioGrantSyncRequests"`. It takes about 0.23 ms for 500 ids.

How to read these numbers:

- **Backlog.** The oldest rows have the lowest ids, so the PK walk finds 500 matches in the first 500 rows. Each batch costs
  the same no matter how large the backlog is.
- **Steady state.** This is the weak case XD01-400 predicted. The walk reads the whole table: the retention window plus the
  kept latest row of each município. It is still one index walk and about 2.5k index-only probes, in 2–2.5 ms, once an hour.
  The cost grows linearly with the retention window. At ten times this volume, it would still be tens of milliseconds per
  hourly run.
- **`FailedRetention` set.** The `OR` branch for `FailedAt` does not change the plan. It only adds the due failed rows to the
  matches.

### Counterfactual: without the `(MunicipioId, Id)` index

This was a one-off check: the same test with that index dropped after seeding. It was not shipped. The planner then falls
back to a `Hash Semi Join` of two `Seq Scan`s of the table, plus a `Sort`. The plan assertion fails, so the test is not
vacuous. The steady-state run took 7.9 ms. The run with `FailedRetention` set took 2.5 ms.

The existing index is what keeps the prune off sequential scans. A dedicated index on `CompletedAt` or `FailedAt` is not
needed. It could not serve the `Id` ordering anyway, and it would add a write to every completion.

## Re-measuring

```bash
dotnet test tests/GeoAssets.IpBilling.EFCore.Tests --filter "FullyQualifiedName~PrunePlanTests" \
  --logger "console;verbosity=detailed"
```

Each test writes its scenario, the statements `PruneAsync` ran, their parameters and their plans to the test output.
