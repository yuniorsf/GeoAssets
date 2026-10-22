# XD01-171 — Real search-path baseline (GetPageAsync)

This measures the **real** SQL `PostgresAssetProvider.GetPageAsync` sends — captured via an EF `DbCommandInterceptor`, not the hand-written "mirror" SQL the existing EXPLAIN tests in `PostgresAssetProviderGetPageAsyncTests.cs` run against. Those tests went green on 2026-10-01 (commit `5f1a65c`) after adding trigram/GIN/B-tree indexes, but they never exercised the method's actual query shape — in particular the always-run `jsonb_each_text` + `ILIKE` subquery used to match `SearchText` against custom-attribute keys/values, and the resulting `"Id" = ANY(@array)` parameter the subquery's matches get folded into.

## Methodology

The ticket asked to run this at commit `5f1a65c^` (before the indexes) and `5f1a65c` (after). `git diff 5f1a65c^ 5f1a65c -- providers/GeoAssets.Provider.PostgreSQL/Repositories/PostgresAssetProvider.cs` shows the **only** change to `GetPageAsync` in that commit is a comment — no behavior difference — and no later migration touches `geo_entity`. So instead of checking out and rebuilding two historical commits, this harness builds **one** binary at HEAD (on branch `xd01-171-real-search-path`) and toggles the *database schema* between "no indexes" (migrated down to `SeedDomainAssetTypesAndLayers`, the migration immediately before `AddGetPageAsyncSearchSortIndexes`) and "with indexes" (migrated to latest). This is equivalent in effect, far more robust, and — crucially — runs both states against **identical** seeded data instead of two separately-seeded tables.

Database: a dedicated `geoassets-bench-postgres` container (postgis/postgis:16-3.4, port 5434, db `geoassets_bench`) — never the dev stack (port 5433, db `geoassets`). Compose file: `tools/GeoAssets.Bench.GetPageAsync/docker-compose.bench.yaml`. Harness: `tools/GeoAssets.Bench.GetPageAsync` (a console project — see the comment at the top of `Program.cs` for why this isn't an xUnit test).

**Phase 4 addition:** a third state, "after fix", measures the real fix landed in commit `fbe637c` on this branch — a normalized, trigram-indexed `geo_entity_attribute(EntityId, Key, Value)` table that `GetPageAsync`'s attribute match now queries instead of the unconditional `jsonb_each_text` scan over `geo_entity`. The same bench container and the same 200,000 Phase-1-seeded `geo_entity` rows were reused — only the new migration (`AddGeoEntityAttributeTable`) was applied on top, whose one-time backfill populated `geo_entity_attribute` (1,200,000 rows = 6 attributes × 200,000) from the existing data, so all three states share byte-identical underlying data.

## Dataset

200,000 `geo_entity` rows, deterministic by construction (every field is a pure function of the row index 1..200000, so re-seeding always produces byte-identical data — no PRNG needed to get a fixed seed). Each row: `Name` = `P-NNNNNN`, `Description` = generic sector/feeder text (never mentions material, so the "concreto" search case is isolated to the custom-attributes path), `AssetTypeId` cycles through the 3 real seeded domain types (Poste / Transformador / Línea de baja tensión) on a `(row / 17) % 3` pattern — deliberately decorrelated from the material split below so the asset-type-filter test case isn't numerically identical to the "concreto" test case. Exactly 6 `custom_attributes` per row: `material` (concreto/madera/metal, exactly 1/3 each), `fases`, `altura_m`, `circuito`, `codigo_lote`, `serie` (unique per row).

Fixed `SearchText` for the 5 cases, chosen to isolate one code path each:

| Case | SearchText | Matches | Path probed |
|---|---|---|---|
| rare name | `P-100000` | 1 row | `Name` ILIKE |
| rare attribute value | `SN-100000` | 1 row | jsonb value ILIKE |
| attribute key match | `altura_m` | all 200,000 rows | jsonb **key** ILIKE (worst case) |
| no match | `zzz` | 0 rows | all branches, no hits |
| common term | `concreto` | ~66,666 rows | jsonb value ILIKE |

Matrix: 5 search cases × `SortBy` ∈ {name, createdAt, updatedAt} × page ∈ {1, 200 (Skip 9950)} × AssetTypeId filter ∈ {none, Poste} = 60 `AssetQuery` combinations, each run against the real `PostgresAssetProvider.GetPageAsync` with 1 warm-up + 5 timed calls (median reported).

## Timing — median of 5 runs (ms)

| Search case | Sort | Page | Type filter | Before (ms) | After (ms) | After fix (ms) |
|---|---|---|---|---:|---:|---:|
| rare name | name | page 1 | none | 1026.2 | 762.0 | 6.8 |
| rare name | name | page 1 | Poste | 891.8 | 761.7 | 6.3 |
| rare name | name | page 200 | none | 1085.6 | 748.7 | 5.2 |
| rare name | name | page 200 | Poste | 896.4 | 751.8 | 4.7 |
| rare name | createdAt | page 1 | none | 1082.8 | 752.7 | 5.1 |
| rare name | createdAt | page 1 | Poste | 891.4 | 759.1 | 5.2 |
| rare name | createdAt | page 200 | none | 1079.2 | 745.3 | 4.8 |
| rare name | createdAt | page 200 | Poste | 887.3 | 763.5 | 5.5 |
| rare name | updatedAt | page 1 | none | 1078.3 | 745.9 | 6.8 |
| rare name | updatedAt | page 1 | Poste | 887.9 | 746.1 | 5.6 |
| rare name | updatedAt | page 200 | none | 1082.7 | 750.1 | 5.4 |
| rare name | updatedAt | page 200 | Poste | 901.2 | 753.8 | 5.0 |
| rare attribute value | name | page 1 | none | 1097.8 | 885.8 | 5.1 |
| rare attribute value | name | page 1 | Poste | 919.6 | 885.8 | 4.9 |
| rare attribute value | name | page 200 | none | 1200.4 | 782.3 | 4.9 |
| rare attribute value | name | page 200 | Poste | 906.8 | 755.5 | 4.8 |
| rare attribute value | createdAt | page 1 | none | 1101.3 | 755.4 | 6.6 |
| rare attribute value | createdAt | page 1 | Poste | 904.0 | 763.1 | 5.8 |
| rare attribute value | createdAt | page 200 | none | 1090.6 | 861.2 | 6.3 |
| rare attribute value | createdAt | page 200 | Poste | 1002.0 | 874.2 | 5.4 |
| rare attribute value | updatedAt | page 1 | none | 1099.3 | 751.6 | 5.1 |
| rare attribute value | updatedAt | page 1 | Poste | 907.6 | 875.8 | 4.5 |
| rare attribute value | updatedAt | page 200 | none | 1213.6 | 871.4 | 4.6 |
| rare attribute value | updatedAt | page 200 | Poste | 914.7 | 753.5 | 4.9 |
| attribute key match | name | page 1 | none | 1959.6 | 1577.5 | 840.0 |
| attribute key match | name | page 1 | Poste | 1571.4 | 1273.1 | 571.7 |
| attribute key match | name | page 200 | none | 2029.5 | 1588.3 | 825.6 |
| attribute key match | name | page 200 | Poste | 1581.9 | 1283.5 | 561.3 |
| attribute key match | createdAt | page 1 | none | 1946.8 | 1569.8 | 792.6 |
| attribute key match | createdAt | page 1 | Poste | 1565.2 | 1330.4 | 531.2 |
| attribute key match | createdAt | page 200 | none | 1976.9 | 1638.9 | 780.0 |
| attribute key match | createdAt | page 200 | Poste | 1582.7 | 1303.9 | 546.5 |
| attribute key match | updatedAt | page 1 | none | 1935.0 | 1636.7 | 788.4 |
| attribute key match | updatedAt | page 1 | Poste | 1588.1 | 1286.0 | 534.5 |
| attribute key match | updatedAt | page 200 | none | 1998.2 | 1589.8 | 830.0 |
| attribute key match | updatedAt | page 200 | Poste | 1569.4 | 1360.3 | 570.9 |
| no match | name | page 1 | none | 1039.4 | 730.9 | 1.8 |
| no match | name | page 1 | Poste | 854.4 | 718.3 | 2.0 |
| no match | name | page 200 | none | 1028.3 | 716.9 | 1.9 |
| no match | name | page 200 | Poste | 847.4 | 714.6 | 1.9 |
| no match | createdAt | page 1 | none | 1034.7 | 710.8 | 1.8 |
| no match | createdAt | page 1 | Poste | 858.9 | 716.8 | 1.9 |
| no match | createdAt | page 200 | none | 1030.2 | 708.9 | 1.8 |
| no match | createdAt | page 200 | Poste | 845.8 | 720.9 | 1.8 |
| no match | updatedAt | page 1 | none | 1044.8 | 711.6 | 1.9 |
| no match | updatedAt | page 1 | Poste | 851.4 | 725.3 | 1.9 |
| no match | updatedAt | page 200 | none | 1036.9 | 716.0 | 1.9 |
| no match | updatedAt | page 200 | Poste | 849.5 | 717.2 | 1.9 |
| common term | name | page 1 | none | 1587.0 | 1404.0 | 397.8 |
| common term | name | page 1 | Poste | 1388.9 | 1240.7 | 278.6 |
| common term | name | page 200 | none | 1615.5 | 1432.5 | 422.7 |
| common term | name | page 200 | Poste | 1309.3 | 1290.7 | 351.7 |
| common term | createdAt | page 1 | none | 1612.5 | 1381.6 | 389.2 |
| common term | createdAt | page 1 | Poste | 1385.0 | 1230.3 | 277.9 |
| common term | createdAt | page 200 | none | 1601.0 | 1425.0 | 421.7 |
| common term | createdAt | page 200 | Poste | 1262.5 | 1280.1 | 343.2 |
| common term | updatedAt | page 1 | none | 1590.6 | 1392.7 | 386.2 |
| common term | updatedAt | page 1 | Poste | 1370.7 | 1233.1 | 274.4 |
| common term | updatedAt | page 200 | none | 1602.7 | 1441.4 | 430.2 |
| common term | updatedAt | page 200 | Poste | 1266.1 | 1274.1 | 336.9 |

Raw captured SQL text, bound parameters (array parameters truncated to 10 entries + a total count in the human-readable dump — the SQL text itself is never truncated) and full `EXPLAIN (ANALYZE, BUFFERS)` plan text for every one of these statements (60 combinations × 3 statements × 3 states) are saved verbatim in `tools/GeoAssets.Bench.GetPageAsync/before.json` / `after.json` / `after-fix.json`. The tables below summarize each statement's SQL shape, its Seq-Scan verdict against the table it actually queries, and its share of that combination's total SQL time.

## Per-combination detail

<details>
<summary>rare name | sort=name | page 1 | type filter=none — before 1026.2 ms, after 762.0 ms, after fix 6.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | yes (geo_entity) | 523.8 | 51.9% |
| COUNT | yes (geo_entity) | 239.0 | 23.7% |
| paged SELECT | yes (geo_entity) | 246.3 | 24.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 954.1 | 99.4% |
| COUNT | no (geo_entity) | 5.1 | 0.5% |
| paged SELECT | no (geo_entity) | 0.8 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 5.2 | 61.6% |
| COUNT | no (geo_entity) | 2.4 | 28.4% |
| paged SELECT | no (geo_entity) | 0.8 | 10.0% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

</details>

<details>
<summary>rare name | sort=name | page 1 | type filter=Poste — before 891.8 ms, after 761.7 ms, after fix 6.3 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 954.2 | 84.7% |
| COUNT | no (geo_entity) | 86.6 | 7.7% |
| paged SELECT | no (geo_entity) | 86.0 | 7.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 780.3 | 99.8% |
| COUNT | no (geo_entity) | 0.7 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.7 | 46.1% |
| COUNT | no (geo_entity) | 0.7 | 19.0% |
| paged SELECT | no (geo_entity) | 1.3 | 34.9% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

</details>

<details>
<summary>rare name | sort=name | page 200 | type filter=none — before 1085.6 ms, after 748.7 ms, after fix 5.2 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 763.8 | 69.3% |
| COUNT | yes (geo_entity) | 171.0 | 15.5% |
| paged SELECT | yes (geo_entity) | 168.0 | 15.2% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 755.1 | 99.8% |
| COUNT | no (geo_entity) | 0.6 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.4 | 52.4% |
| COUNT | no (geo_entity) | 0.7 | 25.4% |
| paged SELECT | no (geo_entity) | 0.6 | 22.3% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>rare name | sort=name | page 200 | type filter=Poste — before 896.4 ms, after 751.8 ms, after fix 4.7 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 747.8 | 81.6% |
| COUNT | no (geo_entity) | 84.2 | 9.2% |
| paged SELECT | no (geo_entity) | 84.9 | 9.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 754.6 | 99.8% |
| COUNT | no (geo_entity) | 0.7 | 0.1% |
| paged SELECT | no (geo_entity) | 0.7 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.4 | 56.0% |
| COUNT | no (geo_entity) | 0.5 | 21.8% |
| paged SELECT | no (geo_entity) | 0.6 | 22.2% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>rare name | sort=createdAt | page 1 | type filter=none — before 1082.8 ms, after 752.7 ms, after fix 5.1 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 765.1 | 69.6% |
| COUNT | yes (geo_entity) | 169.1 | 15.4% |
| paged SELECT | yes (geo_entity) | 165.8 | 15.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 751.2 | 99.8% |
| COUNT | no (geo_entity) | 0.7 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.5 | 56.0% |
| COUNT | no (geo_entity) | 0.6 | 21.0% |
| paged SELECT | no (geo_entity) | 0.6 | 23.0% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

</details>

<details>
<summary>rare name | sort=createdAt | page 1 | type filter=Poste — before 891.4 ms, after 759.1 ms, after fix 5.2 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 767.4 | 80.0% |
| COUNT | no (geo_entity) | 99.0 | 10.3% |
| paged SELECT | no (geo_entity) | 92.9 | 9.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 783.7 | 99.9% |
| COUNT | no (geo_entity) | 0.6 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.4 | 56.2% |
| COUNT | no (geo_entity) | 0.6 | 22.4% |
| paged SELECT | no (geo_entity) | 0.5 | 21.4% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

</details>

<details>
<summary>rare name | sort=createdAt | page 200 | type filter=none — before 1079.2 ms, after 745.3 ms, after fix 4.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 786.9 | 69.3% |
| COUNT | yes (geo_entity) | 176.5 | 15.5% |
| paged SELECT | yes (geo_entity) | 171.7 | 15.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 749.5 | 99.8% |
| COUNT | no (geo_entity) | 0.6 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.4 | 56.3% |
| COUNT | no (geo_entity) | 0.6 | 22.2% |
| paged SELECT | no (geo_entity) | 0.5 | 21.5% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>rare name | sort=createdAt | page 200 | type filter=Poste — before 887.3 ms, after 763.5 ms, after fix 5.5 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 752.8 | 81.4% |
| COUNT | no (geo_entity) | 84.5 | 9.1% |
| paged SELECT | no (geo_entity) | 87.7 | 9.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 777.6 | 99.8% |
| COUNT | no (geo_entity) | 0.7 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.5 | 56.7% |
| COUNT | no (geo_entity) | 0.6 | 22.7% |
| paged SELECT | no (geo_entity) | 0.5 | 20.6% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>rare name | sort=updatedAt | page 1 | type filter=none — before 1078.3 ms, after 745.9 ms, after fix 6.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 760.7 | 69.3% |
| COUNT | yes (geo_entity) | 169.9 | 15.5% |
| paged SELECT | yes (geo_entity) | 166.6 | 15.2% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 747.9 | 99.8% |
| COUNT | no (geo_entity) | 0.7 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.5 | 54.8% |
| COUNT | no (geo_entity) | 0.6 | 23.3% |
| paged SELECT | no (geo_entity) | 0.6 | 21.9% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

</details>

<details>
<summary>rare name | sort=updatedAt | page 1 | type filter=Poste — before 887.9 ms, after 746.1 ms, after fix 5.6 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 744.3 | 81.2% |
| COUNT | no (geo_entity) | 86.0 | 9.4% |
| paged SELECT | no (geo_entity) | 86.1 | 9.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 759.0 | 99.8% |
| COUNT | no (geo_entity) | 0.6 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.5 | 56.7% |
| COUNT | no (geo_entity) | 0.6 | 22.7% |
| paged SELECT | no (geo_entity) | 0.6 | 20.6% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

</details>

<details>
<summary>rare name | sort=updatedAt | page 200 | type filter=none — before 1082.7 ms, after 750.1 ms, after fix 5.4 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 749.6 | 69.1% |
| COUNT | yes (geo_entity) | 168.3 | 15.5% |
| paged SELECT | yes (geo_entity) | 166.4 | 15.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 754.1 | 99.8% |
| COUNT | no (geo_entity) | 0.7 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.5 | 55.1% |
| COUNT | no (geo_entity) | 0.6 | 23.3% |
| paged SELECT | no (geo_entity) | 0.6 | 21.7% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>rare name | sort=updatedAt | page 200 | type filter=Poste — before 901.2 ms, after 753.8 ms, after fix 5.0 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 770.9 | 81.5% |
| COUNT | no (geo_entity) | 86.2 | 9.1% |
| paged SELECT | no (geo_entity) | 89.2 | 9.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 779.9 | 99.8% |
| COUNT | no (geo_entity) | 0.7 | 0.1% |
| paged SELECT | no (geo_entity) | 0.6 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%P-100000%; p1=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 1.4 | 53.7% |
| COUNT | no (geo_entity) | 0.6 | 23.4% |
| paged SELECT | no (geo_entity) | 0.6 | 22.9% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%P-100000%; @pattern0=%P-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%P-100000%; @pattern0=%P-100000%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>rare attribute value | sort=name | page 1 | type filter=none — before 1097.8 ms, after 885.8 ms, after fix 5.1 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 887.5 | 71.9% |
| COUNT | yes (geo_entity) | 172.6 | 14.0% |
| paged SELECT | yes (geo_entity) | 173.8 | 14.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 890.7 | 99.9% |
| COUNT | no (geo_entity) | 0.9 | 0.1% |
| paged SELECT | no (geo_entity) | 0.2 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.4 | 75.2% |
| COUNT | no (geo_entity) | 0.6 | 20.2% |
| paged SELECT | no (geo_entity) | 0.1 | 4.6% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

</details>

<details>
<summary>rare attribute value | sort=name | page 1 | type filter=Poste — before 919.6 ms, after 885.8 ms, after fix 4.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 884.9 | 83.1% |
| COUNT | no (geo_entity) | 89.3 | 8.4% |
| paged SELECT | no (geo_entity) | 90.5 | 8.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 759.7 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.4 | 89.3% |
| COUNT | no (geo_entity) | 0.2 | 6.1% |
| paged SELECT | no (geo_entity) | 0.1 | 4.6% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

</details>

<details>
<summary>rare attribute value | sort=name | page 200 | type filter=none — before 1200.4 ms, after 782.3 ms, after fix 4.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 761.2 | 69.1% |
| COUNT | yes (geo_entity) | 172.6 | 15.7% |
| paged SELECT | yes (geo_entity) | 168.2 | 15.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 892.1 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.2 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.0 | 90.6% |
| COUNT | no (geo_entity) | 0.1 | 5.1% |
| paged SELECT | no (geo_entity) | 0.1 | 4.3% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>rare attribute value | sort=name | page 200 | type filter=Poste — before 906.8 ms, after 755.5 ms, after fix 4.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 753.9 | 80.9% |
| COUNT | no (geo_entity) | 88.8 | 9.5% |
| paged SELECT | no (geo_entity) | 88.9 | 9.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 755.8 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.0 | 89.2% |
| COUNT | no (geo_entity) | 0.1 | 6.1% |
| paged SELECT | no (geo_entity) | 0.1 | 4.7% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>rare attribute value | sort=createdAt | page 1 | type filter=none — before 1101.3 ms, after 755.4 ms, after fix 6.6 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 880.0 | 71.7% |
| COUNT | yes (geo_entity) | 175.4 | 14.3% |
| paged SELECT | yes (geo_entity) | 171.4 | 14.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 770.4 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.8 | 87.5% |
| COUNT | no (geo_entity) | 0.3 | 8.2% |
| paged SELECT | no (geo_entity) | 0.1 | 4.3% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

</details>

<details>
<summary>rare attribute value | sort=createdAt | page 1 | type filter=Poste — before 904.0 ms, after 763.1 ms, after fix 5.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 763.1 | 80.9% |
| COUNT | no (geo_entity) | 89.4 | 9.5% |
| paged SELECT | no (geo_entity) | 90.5 | 9.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 757.8 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.1 | 88.6% |
| COUNT | no (geo_entity) | 0.1 | 5.7% |
| paged SELECT | no (geo_entity) | 0.1 | 5.7% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

</details>

<details>
<summary>rare attribute value | sort=createdAt | page 200 | type filter=none — before 1090.6 ms, after 861.2 ms, after fix 6.3 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 752.3 | 68.6% |
| COUNT | yes (geo_entity) | 173.5 | 15.8% |
| paged SELECT | yes (geo_entity) | 170.2 | 15.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 755.5 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.0 | 87.2% |
| COUNT | no (geo_entity) | 0.2 | 8.1% |
| paged SELECT | no (geo_entity) | 0.1 | 4.8% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>rare attribute value | sort=createdAt | page 200 | type filter=Poste — before 1002.0 ms, after 874.2 ms, after fix 5.4 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 760.6 | 81.0% |
| COUNT | no (geo_entity) | 86.9 | 9.3% |
| paged SELECT | no (geo_entity) | 91.2 | 9.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 876.9 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.1 | 90.5% |
| COUNT | no (geo_entity) | 0.1 | 5.3% |
| paged SELECT | no (geo_entity) | 0.1 | 4.2% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>rare attribute value | sort=updatedAt | page 1 | type filter=none — before 1099.3 ms, after 751.6 ms, after fix 5.1 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 896.6 | 72.0% |
| COUNT | yes (geo_entity) | 179.0 | 14.4% |
| paged SELECT | yes (geo_entity) | 169.5 | 13.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 877.3 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.0 | 88.7% |
| COUNT | no (geo_entity) | 0.1 | 4.9% |
| paged SELECT | no (geo_entity) | 0.1 | 6.4% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=0</sub>

</details>

<details>
<summary>rare attribute value | sort=updatedAt | page 1 | type filter=Poste — before 907.6 ms, after 875.8 ms, after fix 4.5 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 753.5 | 81.0% |
| COUNT | no (geo_entity) | 87.7 | 9.4% |
| paged SELECT | no (geo_entity) | 89.3 | 9.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 751.8 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.2 | 90.6% |
| COUNT | no (geo_entity) | 0.1 | 5.4% |
| paged SELECT | no (geo_entity) | 0.1 | 4.0% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=0</sub>

</details>

<details>
<summary>rare attribute value | sort=updatedAt | page 200 | type filter=none — before 1213.6 ms, after 871.4 ms, after fix 4.6 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 766.7 | 69.0% |
| COUNT | yes (geo_entity) | 172.5 | 15.5% |
| paged SELECT | yes (geo_entity) | 171.4 | 15.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 906.8 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.2 | 90.7% |
| COUNT | no (geo_entity) | 0.1 | 5.1% |
| paged SELECT | no (geo_entity) | 0.1 | 4.2% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>rare attribute value | sort=updatedAt | page 200 | type filter=Poste — before 914.7 ms, after 753.5 ms, after fix 4.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 778.6 | 81.1% |
| COUNT | no (geo_entity) | 89.4 | 9.3% |
| paged SELECT | no (geo_entity) | 91.5 | 9.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 757.3 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%SN-100000%; p1=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 2.1 | 88.7% |
| COUNT | no (geo_entity) | 0.1 | 5.9% |
| paged SELECT | no (geo_entity) | 0.1 | 5.4% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%SN-100000%; @pattern0=%SN-100000%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%SN-100000%; @pattern0=%SN-100000%; @matchingAttributeIds=[bench-100000]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>attribute key match | sort=name | page 1 | type filter=none — before 1959.6 ms, after 1577.5 ms, after fix 840.0 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1005.4 | 54.7% |
| COUNT | yes (geo_entity) | 408.8 | 22.2% |
| paged SELECT | yes (geo_entity) | 423.4 | 23.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1005.6 | 70.0% |
| COUNT | yes (geo_entity) | 420.6 | 29.3% |
| paged SELECT | no (geo_entity) | 10.4 | 0.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 320.6 | 45.7% |
| COUNT | yes (geo_entity) | 370.0 | 52.8% |
| paged SELECT | no (geo_entity) | 10.8 | 1.5% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=0</sub>

</details>

<details>
<summary>attribute key match | sort=name | page 1 | type filter=Poste — before 1571.4 ms, after 1273.1 ms, after fix 571.7 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1019.9 | 69.4% |
| COUNT | no (geo_entity) | 105.6 | 7.2% |
| paged SELECT | no (geo_entity) | 345.2 | 23.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1015.5 | 89.7% |
| COUNT | no (geo_entity) | 107.1 | 9.5% |
| paged SELECT | no (geo_entity) | 10.0 | 0.9% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 293.5 | 70.4% |
| COUNT | no (geo_entity) | 113.7 | 27.3% |
| paged SELECT | no (geo_entity) | 9.8 | 2.4% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=0</sub>

</details>

<details>
<summary>attribute key match | sort=name | page 200 | type filter=none — before 2029.5 ms, after 1588.3 ms, after fix 825.6 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1018.3 | 52.8% |
| COUNT | yes (geo_entity) | 404.5 | 21.0% |
| paged SELECT | yes (geo_entity) | 507.4 | 26.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1008.7 | 70.5% |
| COUNT | yes (geo_entity) | 398.7 | 27.8% |
| paged SELECT | no (geo_entity) | 24.4 | 1.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 241.9 | 39.3% |
| COUNT | yes (geo_entity) | 346.1 | 56.2% |
| paged SELECT | no (geo_entity) | 28.0 | 4.5% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>attribute key match | sort=name | page 200 | type filter=Poste — before 1581.9 ms, after 1283.5 ms, after fix 561.3 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1029.3 | 68.8% |
| COUNT | no (geo_entity) | 120.3 | 8.0% |
| paged SELECT | no (geo_entity) | 346.0 | 23.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1002.5 | 87.8% |
| COUNT | no (geo_entity) | 110.8 | 9.7% |
| paged SELECT | no (geo_entity) | 28.6 | 2.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 242.4 | 61.7% |
| COUNT | no (geo_entity) | 119.8 | 30.5% |
| paged SELECT | no (geo_entity) | 30.9 | 7.9% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>attribute key match | sort=createdAt | page 1 | type filter=none — before 1946.8 ms, after 1569.8 ms, after fix 792.6 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1028.8 | 55.4% |
| COUNT | yes (geo_entity) | 409.6 | 22.0% |
| paged SELECT | yes (geo_entity) | 420.0 | 22.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1011.8 | 71.3% |
| COUNT | yes (geo_entity) | 398.1 | 28.0% |
| paged SELECT | no (geo_entity) | 9.9 | 0.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 240.1 | 39.6% |
| COUNT | yes (geo_entity) | 350.8 | 57.8% |
| paged SELECT | no (geo_entity) | 15.9 | 2.6% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p6=50; @p=0</sub>

</details>

<details>
<summary>attribute key match | sort=createdAt | page 1 | type filter=Poste — before 1565.2 ms, after 1330.4 ms, after fix 531.2 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1014.2 | 69.1% |
| COUNT | no (geo_entity) | 105.1 | 7.2% |
| paged SELECT | no (geo_entity) | 348.5 | 23.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1032.4 | 88.9% |
| COUNT | no (geo_entity) | 117.8 | 10.1% |
| paged SELECT | no (geo_entity) | 10.5 | 0.9% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 233.7 | 63.8% |
| COUNT | no (geo_entity) | 117.8 | 32.1% |
| paged SELECT | no (geo_entity) | 15.0 | 4.1% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p7=50; @p=0</sub>

</details>

<details>
<summary>attribute key match | sort=createdAt | page 200 | type filter=none — before 1976.9 ms, after 1638.9 ms, after fix 780.0 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1016.6 | 54.1% |
| COUNT | yes (geo_entity) | 405.1 | 21.6% |
| paged SELECT | yes (geo_entity) | 457.0 | 24.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1027.7 | 70.8% |
| COUNT | yes (geo_entity) | 399.8 | 27.5% |
| paged SELECT | no (geo_entity) | 24.8 | 1.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 237.8 | 39.3% |
| COUNT | yes (geo_entity) | 339.2 | 56.0% |
| paged SELECT | no (geo_entity) | 28.4 | 4.7% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>attribute key match | sort=createdAt | page 200 | type filter=Poste — before 1582.7 ms, after 1303.9 ms, after fix 546.5 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1005.0 | 68.5% |
| COUNT | no (geo_entity) | 111.4 | 7.6% |
| paged SELECT | no (geo_entity) | 351.6 | 24.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1027.7 | 88.2% |
| COUNT | no (geo_entity) | 108.7 | 9.3% |
| paged SELECT | no (geo_entity) | 28.6 | 2.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 244.6 | 59.4% |
| COUNT | no (geo_entity) | 134.5 | 32.7% |
| paged SELECT | no (geo_entity) | 32.6 | 7.9% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>attribute key match | sort=updatedAt | page 1 | type filter=none — before 1935.0 ms, after 1636.7 ms, after fix 788.4 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1021.1 | 55.6% |
| COUNT | yes (geo_entity) | 401.5 | 21.9% |
| paged SELECT | yes (geo_entity) | 413.0 | 22.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1012.9 | 66.1% |
| COUNT | yes (geo_entity) | 508.3 | 33.2% |
| paged SELECT | no (geo_entity) | 10.1 | 0.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 251.2 | 41.2% |
| COUNT | yes (geo_entity) | 347.5 | 57.0% |
| paged SELECT | no (geo_entity) | 11.3 | 1.9% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p6=50; @p=0</sub>

</details>

<details>
<summary>attribute key match | sort=updatedAt | page 1 | type filter=Poste — before 1588.1 ms, after 1286.0 ms, after fix 534.5 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1040.9 | 68.6% |
| COUNT | no (geo_entity) | 107.1 | 7.1% |
| paged SELECT | no (geo_entity) | 370.1 | 24.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1031.0 | 89.2% |
| COUNT | no (geo_entity) | 113.9 | 9.9% |
| paged SELECT | no (geo_entity) | 10.9 | 0.9% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 235.9 | 64.5% |
| COUNT | no (geo_entity) | 119.5 | 32.7% |
| paged SELECT | no (geo_entity) | 10.3 | 2.8% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p7=50; @p=0</sub>

</details>

<details>
<summary>attribute key match | sort=updatedAt | page 200 | type filter=none — before 1998.2 ms, after 1589.8 ms, after fix 830.0 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1181.0 | 57.0% |
| COUNT | yes (geo_entity) | 420.4 | 20.3% |
| paged SELECT | yes (geo_entity) | 469.9 | 22.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1119.9 | 69.4% |
| COUNT | yes (geo_entity) | 470.4 | 29.1% |
| paged SELECT | no (geo_entity) | 24.6 | 1.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 250.6 | 39.9% |
| COUNT | yes (geo_entity) | 353.8 | 56.3% |
| paged SELECT | no (geo_entity) | 23.8 | 3.8% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>attribute key match | sort=updatedAt | page 200 | type filter=Poste — before 1569.4 ms, after 1360.3 ms, after fix 570.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1037.8 | 69.4% |
| COUNT | no (geo_entity) | 116.2 | 7.8% |
| paged SELECT | no (geo_entity) | 341.8 | 22.9% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1028.8 | 87.8% |
| COUNT | no (geo_entity) | 113.9 | 9.7% |
| paged SELECT | no (geo_entity) | 28.6 | 2.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%altura_m%; p1=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-000001, bench-000002, bench-000003, bench-000004, bench-000005, bench-000006, bench-000007, bench-000008, bench-000009, bench-000010, ... (200000 total)]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 240.5 | 61.2% |
| COUNT | no (geo_entity) | 120.1 | 30.5% |
| paged SELECT | no (geo_entity) | 32.6 | 8.3% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%altura_m%; @pattern0=%altura_m%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%altura_m%; @pattern0=%altura_m%; @matchingAttributeIds=[bench-086347, bench-029240, bench-079477, bench-062316, bench-001455, bench-001375, bench-017751, bench-080361, bench-004073, bench-021155, ... (200000 total)]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>no match | sort=name | page 1 | type filter=none — before 1039.4 ms, after 730.9 ms, after fix 1.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 744.0 | 68.8% |
| COUNT | yes (geo_entity) | 173.5 | 16.0% |
| paged SELECT | yes (geo_entity) | 164.1 | 15.2% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 731.7 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.2 | 67.6% |
| COUNT | no (geo_entity) | 0.1 | 22.1% |
| paged SELECT | no (geo_entity) | 0.0 | 10.3% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

</details>

<details>
<summary>no match | sort=name | page 1 | type filter=Poste — before 854.4 ms, after 718.3 ms, after fix 2.0 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 738.7 | 81.0% |
| COUNT | no (geo_entity) | 86.6 | 9.5% |
| paged SELECT | no (geo_entity) | 86.2 | 9.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 720.6 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 34.8% |
| COUNT | no (geo_entity) | 0.0 | 27.8% |
| paged SELECT | no (geo_entity) | 0.0 | 37.4% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

</details>

<details>
<summary>no match | sort=name | page 200 | type filter=none — before 1028.3 ms, after 716.9 ms, after fix 1.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 723.6 | 68.7% |
| COUNT | yes (geo_entity) | 166.2 | 15.8% |
| paged SELECT | yes (geo_entity) | 164.2 | 15.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 721.4 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 30.5% |
| COUNT | no (geo_entity) | 0.0 | 38.1% |
| paged SELECT | no (geo_entity) | 0.0 | 31.4% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>no match | sort=name | page 200 | type filter=Poste — before 847.4 ms, after 714.6 ms, after fix 1.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 730.1 | 81.0% |
| COUNT | no (geo_entity) | 85.3 | 9.5% |
| paged SELECT | no (geo_entity) | 86.4 | 9.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 728.8 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.0 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 33.1% |
| COUNT | no (geo_entity) | 0.0 | 36.3% |
| paged SELECT | no (geo_entity) | 0.0 | 30.6% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>no match | sort=createdAt | page 1 | type filter=none — before 1034.7 ms, after 710.8 ms, after fix 1.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 742.2 | 68.5% |
| COUNT | yes (geo_entity) | 172.1 | 15.9% |
| paged SELECT | yes (geo_entity) | 168.9 | 15.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 718.1 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.0 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 36.1% |
| COUNT | no (geo_entity) | 0.0 | 33.0% |
| paged SELECT | no (geo_entity) | 0.0 | 30.9% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

</details>

<details>
<summary>no match | sort=createdAt | page 1 | type filter=Poste — before 858.9 ms, after 716.8 ms, after fix 1.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 733.1 | 80.8% |
| COUNT | no (geo_entity) | 86.2 | 9.5% |
| paged SELECT | no (geo_entity) | 87.9 | 9.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 732.4 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.0 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 33.7% |
| COUNT | no (geo_entity) | 0.0 | 26.9% |
| paged SELECT | no (geo_entity) | 0.0 | 39.4% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

</details>

<details>
<summary>no match | sort=createdAt | page 200 | type filter=none — before 1030.2 ms, after 708.9 ms, after fix 1.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 731.4 | 68.8% |
| COUNT | yes (geo_entity) | 167.1 | 15.7% |
| paged SELECT | yes (geo_entity) | 164.7 | 15.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 718.4 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.0 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 33.7% |
| COUNT | no (geo_entity) | 0.0 | 39.4% |
| paged SELECT | no (geo_entity) | 0.0 | 26.9% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>no match | sort=createdAt | page 200 | type filter=Poste — before 845.8 ms, after 720.9 ms, after fix 1.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 740.1 | 81.1% |
| COUNT | no (geo_entity) | 84.6 | 9.3% |
| paged SELECT | no (geo_entity) | 87.4 | 9.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 729.4 | 100.0% |
| COUNT | no (geo_entity) | 0.2 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 42.0% |
| COUNT | no (geo_entity) | 0.0 | 28.0% |
| paged SELECT | no (geo_entity) | 0.0 | 30.0% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>no match | sort=updatedAt | page 1 | type filter=none — before 1044.8 ms, after 711.6 ms, after fix 1.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 726.2 | 68.6% |
| COUNT | yes (geo_entity) | 167.8 | 15.9% |
| paged SELECT | yes (geo_entity) | 164.3 | 15.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 723.4 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.0 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 36.3% |
| COUNT | no (geo_entity) | 0.0 | 25.5% |
| paged SELECT | no (geo_entity) | 0.0 | 38.2% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=0</sub>

</details>

<details>
<summary>no match | sort=updatedAt | page 1 | type filter=Poste — before 851.4 ms, after 725.3 ms, after fix 1.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 741.9 | 81.3% |
| COUNT | no (geo_entity) | 84.8 | 9.3% |
| paged SELECT | no (geo_entity) | 86.0 | 9.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 942.9 | 99.5% |
| COUNT | no (geo_entity) | 3.3 | 0.4% |
| paged SELECT | no (geo_entity) | 1.0 | 0.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 35.8% |
| COUNT | no (geo_entity) | 0.0 | 33.7% |
| paged SELECT | no (geo_entity) | 0.0 | 30.5% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=0</sub>

</details>

<details>
<summary>no match | sort=updatedAt | page 200 | type filter=none — before 1036.9 ms, after 716.0 ms, after fix 1.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 736.4 | 68.6% |
| COUNT | yes (geo_entity) | 168.2 | 15.7% |
| paged SELECT | yes (geo_entity) | 169.1 | 15.8% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 738.9 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.1 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.1 | 44.7% |
| COUNT | no (geo_entity) | 0.0 | 28.9% |
| paged SELECT | no (geo_entity) | 0.0 | 26.3% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>no match | sort=updatedAt | page 200 | type filter=Poste — before 849.5 ms, after 717.2 ms, after fix 1.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 725.7 | 80.9% |
| COUNT | no (geo_entity) | 85.0 | 9.5% |
| paged SELECT | no (geo_entity) | 86.1 | 9.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 738.4 | 100.0% |
| COUNT | no (geo_entity) | 0.1 | 0.0% |
| paged SELECT | no (geo_entity) | 0.0 | 0.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%zzz%; p1=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 0.0 | 32.4% |
| COUNT | no (geo_entity) | 0.0 | 29.5% |
| paged SELECT | no (geo_entity) | 0.0 | 38.1% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%zzz%; @pattern0=%zzz%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%zzz%; @pattern0=%zzz%; @matchingAttributeIds=[]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>common term | sort=name | page 1 | type filter=none — before 1587.0 ms, after 1404.0 ms, after fix 397.8 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1086.6 | 67.3% |
| COUNT | yes (geo_entity) | 259.2 | 16.1% |
| paged SELECT | yes (geo_entity) | 268.9 | 16.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1077.4 | 80.4% |
| COUNT | yes (geo_entity) | 259.2 | 19.3% |
| paged SELECT | no (geo_entity) | 3.6 | 0.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 114.8 | 36.6% |
| COUNT | yes (geo_entity) | 195.4 | 62.4% |
| paged SELECT | no (geo_entity) | 3.1 | 1.0% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p6=50; @p=0</sub>

</details>

<details>
<summary>common term | sort=name | page 1 | type filter=Poste — before 1388.9 ms, after 1240.7 ms, after fix 278.6 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1065.4 | 77.6% |
| COUNT | no (geo_entity) | 106.1 | 7.7% |
| paged SELECT | no (geo_entity) | 200.7 | 14.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1071.9 | 90.5% |
| COUNT | no (geo_entity) | 110.0 | 9.3% |
| paged SELECT | no (geo_entity) | 3.2 | 0.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 110.4 | 51.6% |
| COUNT | no (geo_entity) | 100.5 | 47.0% |
| paged SELECT | no (geo_entity) | 3.1 | 1.4% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p7=50; @p=0</sub>

</details>

<details>
<summary>common term | sort=name | page 200 | type filter=none — before 1615.5 ms, after 1432.5 ms, after fix 422.7 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1158.3 | 67.6% |
| COUNT | yes (geo_entity) | 267.6 | 15.6% |
| paged SELECT | yes (geo_entity) | 287.4 | 16.8% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1084.6 | 78.2% |
| COUNT | yes (geo_entity) | 255.9 | 18.5% |
| paged SELECT | no (geo_entity) | 46.3 | 3.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 110.6 | 31.5% |
| COUNT | yes (geo_entity) | 195.5 | 55.7% |
| paged SELECT | no (geo_entity) | 44.6 | 12.7% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."Name"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>common term | sort=name | page 200 | type filter=Poste — before 1309.3 ms, after 1290.7 ms, after fix 351.7 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1165.0 | 82.9% |
| COUNT | no (geo_entity) | 113.7 | 8.1% |
| paged SELECT | no (geo_entity) | 127.5 | 9.1% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1070.7 | 86.8% |
| COUNT | no (geo_entity) | 104.7 | 8.5% |
| paged SELECT | no (geo_entity) | 57.5 | 4.7% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 145.4 | 48.1% |
| COUNT | no (geo_entity) | 99.9 | 33.1% |
| paged SELECT | no (geo_entity) | 56.8 | 18.8% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."Name"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>common term | sort=createdAt | page 1 | type filter=none — before 1612.5 ms, after 1381.6 ms, after fix 389.2 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1061.2 | 67.4% |
| COUNT | yes (geo_entity) | 251.4 | 16.0% |
| paged SELECT | yes (geo_entity) | 261.6 | 16.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1060.6 | 80.5% |
| COUNT | yes (geo_entity) | 252.1 | 19.1% |
| paged SELECT | no (geo_entity) | 4.9 | 0.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 110.1 | 36.1% |
| COUNT | yes (geo_entity) | 190.9 | 62.6% |
| paged SELECT | no (geo_entity) | 3.7 | 1.2% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p6=50; @p=0</sub>

</details>

<details>
<summary>common term | sort=createdAt | page 1 | type filter=Poste — before 1385.0 ms, after 1230.3 ms, after fix 277.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1084.3 | 78.1% |
| COUNT | no (geo_entity) | 107.0 | 7.7% |
| paged SELECT | no (geo_entity) | 197.2 | 14.2% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1072.4 | 91.1% |
| COUNT | no (geo_entity) | 102.2 | 8.7% |
| paged SELECT | no (geo_entity) | 3.1 | 0.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 109.0 | 50.6% |
| COUNT | no (geo_entity) | 102.7 | 47.6% |
| paged SELECT | no (geo_entity) | 4.0 | 1.8% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p7=50; @p=0</sub>

</details>

<details>
<summary>common term | sort=createdAt | page 200 | type filter=none — before 1601.0 ms, after 1425.0 ms, after fix 421.7 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1074.9 | 66.2% |
| COUNT | yes (geo_entity) | 266.5 | 16.4% |
| paged SELECT | yes (geo_entity) | 281.7 | 17.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1082.9 | 78.3% |
| COUNT | yes (geo_entity) | 252.1 | 18.2% |
| paged SELECT | no (geo_entity) | 47.7 | 3.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 111.0 | 31.3% |
| COUNT | yes (geo_entity) | 198.3 | 55.9% |
| paged SELECT | no (geo_entity) | 45.4 | 12.8% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."CreatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>common term | sort=createdAt | page 200 | type filter=Poste — before 1262.5 ms, after 1280.1 ms, after fix 343.2 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1068.4 | 81.5% |
| COUNT | no (geo_entity) | 110.8 | 8.5% |
| paged SELECT | no (geo_entity) | 131.1 | 10.0% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1063.3 | 86.2% |
| COUNT | no (geo_entity) | 110.1 | 8.9% |
| paged SELECT | no (geo_entity) | 60.8 | 4.9% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 144.7 | 46.2% |
| COUNT | no (geo_entity) | 108.1 | 34.5% |
| paged SELECT | no (geo_entity) | 60.6 | 19.3% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."CreatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p7=50; @p=9950</sub>

</details>

<details>
<summary>common term | sort=updatedAt | page 1 | type filter=none — before 1590.6 ms, after 1392.7 ms, after fix 386.2 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1077.2 | 67.4% |
| COUNT | yes (geo_entity) | 258.4 | 16.2% |
| paged SELECT | yes (geo_entity) | 262.0 | 16.4% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1110.9 | 81.2% |
| COUNT | yes (geo_entity) | 253.7 | 18.5% |
| paged SELECT | no (geo_entity) | 3.1 | 0.2% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 111.9 | 35.6% |
| COUNT | yes (geo_entity) | 198.8 | 63.2% |
| paged SELECT | no (geo_entity) | 3.7 | 1.2% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p6=50; @p=0</sub>

</details>

<details>
<summary>common term | sort=updatedAt | page 1 | type filter=Poste — before 1370.7 ms, after 1233.1 ms, after fix 274.4 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1067.3 | 77.8% |
| COUNT | no (geo_entity) | 104.3 | 7.6% |
| paged SELECT | no (geo_entity) | 199.8 | 14.6% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=0</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1069.9 | 90.9% |
| COUNT | no (geo_entity) | 103.6 | 8.8% |
| paged SELECT | no (geo_entity) | 3.1 | 0.3% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=0</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 115.5 | 50.3% |
| COUNT | no (geo_entity) | 109.8 | 47.8% |
| paged SELECT | no (geo_entity) | 4.2 | 1.8% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p7=50; @p=0</sub>

</details>

<details>
<summary>common term | sort=updatedAt | page 200 | type filter=none — before 1602.7 ms, after 1441.4 ms, after fix 430.2 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1085.0 | 66.8% |
| COUNT | yes (geo_entity) | 260.1 | 16.0% |
| paged SELECT | yes (geo_entity) | 279.8 | 17.2% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1069.3 | 77.6% |
| COUNT | yes (geo_entity) | 260.0 | 18.9% |
| paged SELECT | no (geo_entity) | 48.1 | 3.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p6=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 111.2 | 31.2% |
| COUNT | yes (geo_entity) | 196.1 | 55.1% |
| paged SELECT | no (geo_entity) | 48.5 | 13.6% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds)
ORDER BY g."UpdatedAt"
LIMIT @p6 OFFSET @p
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p6=50; @p=9950</sub>

</details>

<details>
<summary>common term | sort=updatedAt | page 200 | type filter=Poste — before 1266.1 ms, after 1274.1 ms, after fix 336.9 ms</summary>

**Before**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1067.9 | 82.3% |
| COUNT | no (geo_entity) | 106.4 | 8.2% |
| paged SELECT | no (geo_entity) | 123.5 | 9.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=9950</sub>

**After**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | no (geo_entity) | 1080.8 | 87.2% |
| COUNT | no (geo_entity) | 102.1 | 8.2% |
| paged SELECT | no (geo_entity) | 56.0 | 4.5% |

<sub>**attr-match (raw jsonb_each_text on geo_entity) SQL:**</sub>

```sql
SELECT DISTINCT ge."Id"
FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
WHERE attr.key ILIKE @p0 OR attr.value ILIKE @p1

```
<sub>Params: p0=%concreto%; p1=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-000003, bench-000006, bench-000009, bench-000012, bench-000015, bench-000018, bench-000021, bench-000024, bench-000027, bench-000030, ... (66666 total)]; @p7=50; @p=9950</sub>

**After fix**

| Statement | Seq Scan (own table) | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (indexed geo_entity_attribute) | no (geo_entity_attribute) | 122.1 | 43.1% |
| COUNT | no (geo_entity) | 104.6 | 36.9% |
| paged SELECT | no (geo_entity) | 56.9 | 20.1% |

<sub>**attr-match (indexed geo_entity_attribute) SQL:**</sub>

```sql
SELECT DISTINCT g."EntityId"
FROM geo_entity_attribute AS g
WHERE g."Key" ILIKE @pattern ESCAPE '' OR g."Value" ILIKE @pattern0 ESCAPE ''
```
<sub>Params: @pattern=%concreto%; @pattern0=%concreto%</sub>

<sub>**COUNT SQL:**</sub>

```sql
SELECT count(*)::int
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]</sub>

<sub>**paged SELECT SQL:**</sub>

```sql
SELECT g."Id", g."AssetTypeId", g."AssetTypeId1", g."CreatedAt", g.custom_attributes, g."Description", g.geom, g."LayerId", g."Name", g."OrganizationId", g.topology, g."UpdatedAt"
FROM geo_entity AS g
WHERE g."AssetTypeId" = @query_AssetTypeId AND (g."Name" ILIKE @pattern ESCAPE '' OR g."Description" ILIKE @pattern0 ESCAPE '' OR g."Id" = ANY (@matchingAttributeIds))
ORDER BY g."UpdatedAt"
LIMIT @p7 OFFSET @p
```
<sub>Params: @query_AssetTypeId=00000000-0000-0000-0000-000000000004; @pattern=%concreto%; @pattern0=%concreto%; @matchingAttributeIds=[bench-161829, bench-176205, bench-160395, bench-127857, bench-107349, bench-062916, bench-152046, bench-109446, bench-062316, bench-001455, ... (66666 total)]; @p7=50; @p=9950</sub>

</details>

## Where the time goes

Aggregated across all 60 combinations, grouped by statement shape:

| Statement | Before avg (ms) | Before max (ms) | Before Seq Scans | After avg (ms) | After max (ms) | After Seq Scans | After-fix avg (ms) | After-fix max (ms) | After-fix Seq Scans |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| attr-match (raw jsonb_each_text on geo_entity) | 883.7 | 1181.0 | 1/60 | 888.0 | 1119.9 | 0/60 | n/a | n/a | n/a |
| attr-match (indexed geo_entity_attribute) | n/a | n/a | n/a | n/a | n/a | n/a | 75.0 | 320.6 | 0/60 |
| COUNT | 167.5 | 420.4 | 30/60 | 90.9 | 508.3 | 12/60 | 77.4 | 370.0 | 12/60 |
| paged SELECT | 201.8 | 507.4 | 30/60 | 9.5 | 60.8 | 0/60 | 9.9 | 60.6 | 0/60 |

**The always-run `jsonb_each_text` attribute-expansion subquery was the dominant, unconditional cost before the fix — and the 171 indexes alone never touched it.** It ran on *every* `SearchText` query regardless of whether the search was even about custom attributes (it ran for "rare name" too), averaged ~884-888 ms before *and* after the 171 indexes, and was the single biggest line item in every combination.

**After the fix (`fbe637c`), that statement is replaced by an index-backed lookup against `geo_entity_attribute`** (trigram GIN on `Key`/`Value`), and it is no longer the dominant cost: for the narrow-match cases (rare name, rare attribute value, no match) it now runs in low single-digit milliseconds, and total `GetPageAsync` latency for those cases dropped from ~730-1100 ms to **under 10 ms** — the ~884 ms fixed floor is gone. For `SearchText` values that still match a huge fraction of the table ("attribute key match" = `altura_m`, matching all 200,000 rows; "common term" = `concreto`, ~66,666 rows), the attribute-match statement itself now takes 115-320 ms — returning and de-duplicating a 66k-200k row id set is real work even with an index seek — down from the ~884-1120 ms unconditional scan, but not free.

**The COUNT seq-scan-on-huge-array issue Phase 1 flagged is only partially resolved.** It still appears in exactly the same 12/60 cases (attribute key match and common term, both without an AssetTypeId filter): once the `"Id" = ANY(@matchingAttributeIds)` parameter built from the attribute-match statement's result is tens of thousands of entries long, the planner still abandons the Name/Description trigram indexes for that OR-branch and sequentially scans `geo_entity` to evaluate array membership — 370 ms and 195 ms respectively for the two representative no-filter cases measured here, versus near-instant paged SELECTs (1-11 ms) once the array is small or an AssetTypeId filter narrows the candidate set first. This is a known, unaddressed residual cost of the chosen fix (normalizing the attribute match didn't change how its *result* feeds into COUNT/paged SELECT), not a regression — and not something this ticket's fix was aimed at.

**Net effect on end-to-end latency:** the narrow-match cases (rare name, rare attribute value, no match) went from ~730-1100 ms to **1.8-6.8 ms** — a 150-600x improvement, since they no longer pay the ~884 ms unconditional floor at all. The broad-match, no-type-filter cases (attribute key match, common term) improved substantially but did not reach the same floor: ~1.2-2.0 s down to **280-840 ms**, still dominated by the attribute-match statement's own cost on a huge result set plus the residual COUNT seq-scan. Adding an AssetTypeId filter to a broad-match query (which narrows the candidate set via its own B-tree index before the array check) brings even these down further (e.g. "common term" + Poste filter: 1240.7 ms after-indexes-only → 278.6 ms after fix). The ~884 ms unconditional floor that made *every* search slow regardless of what it searched for is gone; what remains scales with how broad the attribute match actually is, which is the expected, acceptable shape for this kind of query.
