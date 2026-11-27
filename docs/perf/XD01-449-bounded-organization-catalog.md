# Bounded organization catalog — before / after (XD01-449)

XD01-448 found that the kernel could store COELBA's METROPOLITANA region (783,827 features) but not serve it. An
organization's first read loaded its whole catalog into memory (`PostgresAssetProvider.WarmCacheAsync`, about 9.3 KiB per
feature) and, for 784 k features, threw `OutOfMemoryException` in a 6 GiB kernel
([report](XD01-448-metropolitana-scale-gate.md)). Since XD01-449 an organization larger than
`GeoAssets:OrganizationCatalogs:FullCacheMaxFeatures` (default 50,000) is served from PostGIS instead.

Measured on 2026-11-26 on the same `coelba` silo and database as XD01-448 (Docker VM 7.65 GiB, silo capped at 6 GiB,
`geoassets_coelba` with the 783,827 features), with a kernel built from this branch.

## Results

| | Before (XD01-448) | After (XD01-449) |
|---|---|---|
| Silo start to `/healthz` | 3.5 s | 2 s |
| Organization's first read, 784 k features | **500** after 9.5 s (`OutOfMemoryException`), retried on every request | **200** in 3.7 s (the row count, plus the first token's key fetch) |
| Organization's first read, 547 k features | 9.1 s, silo 228 MiB → **5.09 GiB** | — (the same path as 784 k) |
| Read by id (warm) | 3–30 ms | 1–3 ms |
| Silo memory after reads | 4.6 GiB (and dead) | **125–198 MiB** (the by-id cache holds at most 5,000 features) |
| `descendants` of a feature | 2.7 s (over the in-memory catalog) | 20–30 ms (recursive SQL) |
| Ancestors walk (topology reversed) | in-memory over the whole catalog | 2.6 ms recursive union on `IX_geo_entity_topology` (GIN, `jsonb_path_ops`) |
| `verify --sample 2000` | exit 3 (the 500) | **exit 0**, 2,000 of 2,000, 6.3 s |
| Re-import `complete` on the warm organization | full reload with the old cache alive → OOM-killed | a recount, no load; memory flat at 196 MiB |

## What changed

- **Mode per organization.** `PostgresOrganizationAssetCatalogs` counts an organization's own features when it warms or
  refreshes it, and also on a synchronous first read. At most `FullCacheMaxFeatures` keeps the whole-catalog cache, as
  before. Above that, the organization is served from PostgreSQL (`IsQueryBacked`, logged as
  `PostgresAssetProvider serves N features from PostgreSQL`). A refresh after growth switches to PostgreSQL, and one after
  shrinking switches back.
- **Reads of a PostgreSQL-served organization:**
  - **By id:** `GetById` goes to the primary key, through a by-id cache of `ByIdCacheCapacity` features (default 5,000).
    Writes through the catalog evict their ids, and a refresh clears it.
  - **Enumerating reads:** `GetAll`, `GetByAssetType`, `Search`, `GetWithin`, `GetIntersecting` and `GetNearby` run in SQL
    over the organization's own rows, in ordinal id order (`GetNearby`: by distance). A read that would return more than
    `EnumerationLimit` (default 10,000) features is refused with `CatalogEnumerationLimitException`, which the kernel
    answers as **422 `catalog.enumeration-limit`** (with `operation` and `limit`).
  - **Topology:** `GetNeighbors`, `GetDescendants` and `GetAncestors` through `OrganizationAssetCatalog` run as recursive
    SQL over the organization's and the shared rows, with `TopoGraph`'s semantics, and are refused beyond the same limit.
- **Mutation batches.** The per-batch provider of `PostgresAssetMutationBatchApplier` always reads by id through its own
  transaction, never loading the organization's catalog.
- **WFS GetFeature without BBOX** is one page in PostgreSQL (`GetPageAsync`): at most 10,000 features (`COUNT` absent or
  larger), and `numberMatched` is the exact count.
- **Small organizations keep the whole cache**, and so does the shared (organization-less) catalog.

## Configuration

`GeoAssets:OrganizationCatalogs` (`OrganizationCatalogOptions`):

| Key | Default | Meaning |
|---|---|---|
| `FullCacheMaxFeatures` | 50000 | The most own features an organization may have and still be cached whole. At 9.3 KiB per feature, 50,000 is about 450 MiB. 0 means never cached whole |
| `EnumerationLimit` | 10000 | The most features one enumerating read or traversal of a PostgreSQL-served organization returns |
| `ByIdCacheCapacity` | 5000 | Features a PostgreSQL-served organization keeps by id. 0 means none |

## Known limits

- `Clear()` and `LoadAll()` still read every row of the organization before deleting them. Nothing in the kernel calls
  them on an organization catalog today.
- WFS GetFeature **with** a BBOX reads every feature in the box (`GetInBoundsAsync`) and pages in memory. A box covering
  a whole region of a large distributor is still unbounded. The map's own reads (`/features/bounds`, WMS) are capped in
  SQL.
- `FindPath`, `FindShortestPath`, `GetConnectedComponents`, `HasCycles` and `TopologicalSort` still run over `GetAll()`.
  For a PostgreSQL-served organization they are therefore refused beyond the limit. The kernel has no caller for them.
- The 30 s Npgsql command timeout is too short for XD01-432's `geom_3857` migration on 784 k features (it rewrites
  `geo_entity`). `deploy/distributor-silo/provision-database.sh` now runs migrations with a 1,800 s timeout
  (`MIGRATION_COMMAND_TIMEOUT`); there it took about 1 min 40 s. A kernel that started against such a database with the
  migration still pending would fail its startup migration.

## Re-measure

As in [XD01-448](XD01-448-metropolitana-scale-gate.md#re-measure), against a silo built from this branch:

```bash
deploy/distributor-silo/provision-database.sh coelba      # the kernel migrations first (geom_3857, the topology index)
docker compose -f deploy/distributor-silo/docker-compose.yaml --env-file deploy/distributor-silo/coelba.env up --build -d
curl -s -o /dev/null -w 'first %{http_code} %{time_total}s\n' -H "Authorization: Bearer $TOKEN" \
  "http://127.0.0.1:8180/api/geoassets/organizations/$ORG_ID/features/$FEATURE_ID"
docker stats --no-stream geoasset-server-coelba
docker logs geoasset-server-coelba 2>&1 | grep -E 'serves .* from PostgreSQL|WarmCacheAsync|OutOfMemory'
```
