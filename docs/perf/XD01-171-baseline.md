# XD01-171 — Real search-path baseline (GetPageAsync)

This measures the **real** SQL `PostgresAssetProvider.GetPageAsync` sends — captured via an EF `DbCommandInterceptor`, not the hand-written "mirror" SQL the existing EXPLAIN tests in `PostgresAssetProviderGetPageAsyncTests.cs` run against. Those tests went green on 2026-10-01 (commit `5f1a65c`) after adding trigram/GIN/B-tree indexes, but they never exercised the method's actual query shape — in particular the always-run `jsonb_each_text` + `ILIKE` subquery used to match `SearchText` against custom-attribute keys/values, and the resulting `"Id" = ANY(@array)` parameter the subquery's matches get folded into.

## Methodology

The ticket asked to run this at commit `5f1a65c^` (before the indexes) and `5f1a65c` (after). `git diff 5f1a65c^ 5f1a65c -- providers/GeoAssets.Provider.PostgreSQL/Repositories/PostgresAssetProvider.cs` shows the **only** change to `GetPageAsync` in that commit is a comment — no behavior difference — and no later migration touches `geo_entity`. So instead of checking out and rebuilding two historical commits, this harness builds **one** binary at HEAD (on branch `xd01-171-real-search-path`) and toggles the *database schema* between "no indexes" (migrated down to `SeedDomainAssetTypesAndLayers`, the migration immediately before `AddGetPageAsyncSearchSortIndexes`) and "with indexes" (migrated to latest). This is equivalent in effect, far more robust, and — crucially — runs both states against **identical** seeded data instead of two separately-seeded tables.

Database: a dedicated `geoassets-bench-postgres` container (postgis/postgis:16-3.4, port 5434, db `geoassets_bench`) — never the dev stack (port 5433, db `geoassets`). Compose file: `tools/GeoAssets.Bench.GetPageAsync/docker-compose.bench.yaml`. Harness: `tools/GeoAssets.Bench.GetPageAsync` (a console project — see the comment at the top of `Program.cs` for why this isn't an xUnit test).

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

| Search case | Sort | Page | Type filter | Before (ms) | After (ms) |
|---|---|---|---|---:|---:|
| rare name | name | page 1 | none | 1026.2 | 762.0 |
| rare name | name | page 1 | Poste | 891.8 | 761.7 |
| rare name | name | page 200 | none | 1085.6 | 748.7 |
| rare name | name | page 200 | Poste | 896.4 | 751.8 |
| rare name | createdAt | page 1 | none | 1082.8 | 752.7 |
| rare name | createdAt | page 1 | Poste | 891.4 | 759.1 |
| rare name | createdAt | page 200 | none | 1079.2 | 745.3 |
| rare name | createdAt | page 200 | Poste | 887.3 | 763.5 |
| rare name | updatedAt | page 1 | none | 1078.3 | 745.9 |
| rare name | updatedAt | page 1 | Poste | 887.9 | 746.1 |
| rare name | updatedAt | page 200 | none | 1082.7 | 750.1 |
| rare name | updatedAt | page 200 | Poste | 901.2 | 753.8 |
| rare attribute value | name | page 1 | none | 1097.8 | 885.8 |
| rare attribute value | name | page 1 | Poste | 919.6 | 885.8 |
| rare attribute value | name | page 200 | none | 1200.4 | 782.3 |
| rare attribute value | name | page 200 | Poste | 906.8 | 755.5 |
| rare attribute value | createdAt | page 1 | none | 1101.3 | 755.4 |
| rare attribute value | createdAt | page 1 | Poste | 904.0 | 763.1 |
| rare attribute value | createdAt | page 200 | none | 1090.6 | 861.2 |
| rare attribute value | createdAt | page 200 | Poste | 1002.0 | 874.2 |
| rare attribute value | updatedAt | page 1 | none | 1099.3 | 751.6 |
| rare attribute value | updatedAt | page 1 | Poste | 907.6 | 875.8 |
| rare attribute value | updatedAt | page 200 | none | 1213.6 | 871.4 |
| rare attribute value | updatedAt | page 200 | Poste | 914.7 | 753.5 |
| attribute key match | name | page 1 | none | 1959.6 | 1577.5 |
| attribute key match | name | page 1 | Poste | 1571.4 | 1273.1 |
| attribute key match | name | page 200 | none | 2029.5 | 1588.3 |
| attribute key match | name | page 200 | Poste | 1581.9 | 1283.5 |
| attribute key match | createdAt | page 1 | none | 1946.8 | 1569.8 |
| attribute key match | createdAt | page 1 | Poste | 1565.2 | 1330.4 |
| attribute key match | createdAt | page 200 | none | 1976.9 | 1638.9 |
| attribute key match | createdAt | page 200 | Poste | 1582.7 | 1303.9 |
| attribute key match | updatedAt | page 1 | none | 1935.0 | 1636.7 |
| attribute key match | updatedAt | page 1 | Poste | 1588.1 | 1286.0 |
| attribute key match | updatedAt | page 200 | none | 1998.2 | 1589.8 |
| attribute key match | updatedAt | page 200 | Poste | 1569.4 | 1360.3 |
| no match | name | page 1 | none | 1039.4 | 730.9 |
| no match | name | page 1 | Poste | 854.4 | 718.3 |
| no match | name | page 200 | none | 1028.3 | 716.9 |
| no match | name | page 200 | Poste | 847.4 | 714.6 |
| no match | createdAt | page 1 | none | 1034.7 | 710.8 |
| no match | createdAt | page 1 | Poste | 858.9 | 716.8 |
| no match | createdAt | page 200 | none | 1030.2 | 708.9 |
| no match | createdAt | page 200 | Poste | 845.8 | 720.9 |
| no match | updatedAt | page 1 | none | 1044.8 | 711.6 |
| no match | updatedAt | page 1 | Poste | 851.4 | 725.3 |
| no match | updatedAt | page 200 | none | 1036.9 | 716.0 |
| no match | updatedAt | page 200 | Poste | 849.5 | 717.2 |
| common term | name | page 1 | none | 1587.0 | 1404.0 |
| common term | name | page 1 | Poste | 1388.9 | 1240.7 |
| common term | name | page 200 | none | 1615.5 | 1432.5 |
| common term | name | page 200 | Poste | 1309.3 | 1290.7 |
| common term | createdAt | page 1 | none | 1612.5 | 1381.6 |
| common term | createdAt | page 1 | Poste | 1385.0 | 1230.3 |
| common term | createdAt | page 200 | none | 1601.0 | 1425.0 |
| common term | createdAt | page 200 | Poste | 1262.5 | 1280.1 |
| common term | updatedAt | page 1 | none | 1590.6 | 1392.7 |
| common term | updatedAt | page 1 | Poste | 1370.7 | 1233.1 |
| common term | updatedAt | page 200 | none | 1602.7 | 1441.4 |
| common term | updatedAt | page 200 | Poste | 1266.1 | 1274.1 |

Raw captured SQL text, bound parameters (array parameters truncated to 10 entries + a total count in the human-readable dump — the SQL text itself is never truncated) and full `EXPLAIN (ANALYZE, BUFFERS)` plan text for every one of these 360 statements (60 combinations × 3 statements × 2 states) are saved verbatim in `tools/GeoAssets.Bench.GetPageAsync/before.json` / `after.json`. The tables below summarize each statement's SQL shape, its Seq-Scan-on-`geo_entity` verdict, and its share of that combination's total SQL time.

## Per-combination detail

<details>
<summary>rare name | sort=name | page 1 | type filter=none — before 1026.2 ms, after 762.0 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | yes | 523.8 | 51.9% |
| COUNT | yes | 239.0 | 23.7% |
| paged SELECT | yes | 246.3 | 24.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 954.1 | 99.4% |
| COUNT | no | 5.1 | 0.5% |
| paged SELECT | no | 0.8 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=name | page 1 | type filter=Poste — before 891.8 ms, after 761.7 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 954.2 | 84.7% |
| COUNT | no | 86.6 | 7.7% |
| paged SELECT | no | 86.0 | 7.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 780.3 | 99.8% |
| COUNT | no | 0.7 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=name | page 200 | type filter=none — before 1085.6 ms, after 748.7 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 763.8 | 69.3% |
| COUNT | yes | 171.0 | 15.5% |
| paged SELECT | yes | 168.0 | 15.2% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 755.1 | 99.8% |
| COUNT | no | 0.6 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=name | page 200 | type filter=Poste — before 896.4 ms, after 751.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 747.8 | 81.6% |
| COUNT | no | 84.2 | 9.2% |
| paged SELECT | no | 84.9 | 9.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 754.6 | 99.8% |
| COUNT | no | 0.7 | 0.1% |
| paged SELECT | no | 0.7 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=createdAt | page 1 | type filter=none — before 1082.8 ms, after 752.7 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 765.1 | 69.6% |
| COUNT | yes | 169.1 | 15.4% |
| paged SELECT | yes | 165.8 | 15.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 751.2 | 99.8% |
| COUNT | no | 0.7 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=createdAt | page 1 | type filter=Poste — before 891.4 ms, after 759.1 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 767.4 | 80.0% |
| COUNT | no | 99.0 | 10.3% |
| paged SELECT | no | 92.9 | 9.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 783.7 | 99.9% |
| COUNT | no | 0.6 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=createdAt | page 200 | type filter=none — before 1079.2 ms, after 745.3 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 786.9 | 69.3% |
| COUNT | yes | 176.5 | 15.5% |
| paged SELECT | yes | 171.7 | 15.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 749.5 | 99.8% |
| COUNT | no | 0.6 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=createdAt | page 200 | type filter=Poste — before 887.3 ms, after 763.5 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 752.8 | 81.4% |
| COUNT | no | 84.5 | 9.1% |
| paged SELECT | no | 87.7 | 9.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 777.6 | 99.8% |
| COUNT | no | 0.7 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=updatedAt | page 1 | type filter=none — before 1078.3 ms, after 745.9 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 760.7 | 69.3% |
| COUNT | yes | 169.9 | 15.5% |
| paged SELECT | yes | 166.6 | 15.2% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 747.9 | 99.8% |
| COUNT | no | 0.7 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=updatedAt | page 1 | type filter=Poste — before 887.9 ms, after 746.1 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 744.3 | 81.2% |
| COUNT | no | 86.0 | 9.4% |
| paged SELECT | no | 86.1 | 9.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 759.0 | 99.8% |
| COUNT | no | 0.6 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=updatedAt | page 200 | type filter=none — before 1082.7 ms, after 750.1 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 749.6 | 69.1% |
| COUNT | yes | 168.3 | 15.5% |
| paged SELECT | yes | 166.4 | 15.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 754.1 | 99.8% |
| COUNT | no | 0.7 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare name | sort=updatedAt | page 200 | type filter=Poste — before 901.2 ms, after 753.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 770.9 | 81.5% |
| COUNT | no | 86.2 | 9.1% |
| paged SELECT | no | 89.2 | 9.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 779.9 | 99.8% |
| COUNT | no | 0.7 | 0.1% |
| paged SELECT | no | 0.6 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=name | page 1 | type filter=none — before 1097.8 ms, after 885.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 887.5 | 71.9% |
| COUNT | yes | 172.6 | 14.0% |
| paged SELECT | yes | 173.8 | 14.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 890.7 | 99.9% |
| COUNT | no | 0.9 | 0.1% |
| paged SELECT | no | 0.2 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=name | page 1 | type filter=Poste — before 919.6 ms, after 885.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 884.9 | 83.1% |
| COUNT | no | 89.3 | 8.4% |
| paged SELECT | no | 90.5 | 8.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 759.7 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=name | page 200 | type filter=none — before 1200.4 ms, after 782.3 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 761.2 | 69.1% |
| COUNT | yes | 172.6 | 15.7% |
| paged SELECT | yes | 168.2 | 15.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 892.1 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.2 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=name | page 200 | type filter=Poste — before 906.8 ms, after 755.5 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 753.9 | 80.9% |
| COUNT | no | 88.8 | 9.5% |
| paged SELECT | no | 88.9 | 9.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 755.8 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=createdAt | page 1 | type filter=none — before 1101.3 ms, after 755.4 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 880.0 | 71.7% |
| COUNT | yes | 175.4 | 14.3% |
| paged SELECT | yes | 171.4 | 14.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 770.4 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=createdAt | page 1 | type filter=Poste — before 904.0 ms, after 763.1 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 763.1 | 80.9% |
| COUNT | no | 89.4 | 9.5% |
| paged SELECT | no | 90.5 | 9.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 757.8 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=createdAt | page 200 | type filter=none — before 1090.6 ms, after 861.2 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 752.3 | 68.6% |
| COUNT | yes | 173.5 | 15.8% |
| paged SELECT | yes | 170.2 | 15.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 755.5 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=createdAt | page 200 | type filter=Poste — before 1002.0 ms, after 874.2 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 760.6 | 81.0% |
| COUNT | no | 86.9 | 9.3% |
| paged SELECT | no | 91.2 | 9.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 876.9 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=updatedAt | page 1 | type filter=none — before 1099.3 ms, after 751.6 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 896.6 | 72.0% |
| COUNT | yes | 179.0 | 14.4% |
| paged SELECT | yes | 169.5 | 13.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 877.3 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=updatedAt | page 1 | type filter=Poste — before 907.6 ms, after 875.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 753.5 | 81.0% |
| COUNT | no | 87.7 | 9.4% |
| paged SELECT | no | 89.3 | 9.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 751.8 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=updatedAt | page 200 | type filter=none — before 1213.6 ms, after 871.4 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 766.7 | 69.0% |
| COUNT | yes | 172.5 | 15.5% |
| paged SELECT | yes | 171.4 | 15.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 906.8 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>rare attribute value | sort=updatedAt | page 200 | type filter=Poste — before 914.7 ms, after 753.5 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 778.6 | 81.1% |
| COUNT | no | 89.4 | 9.3% |
| paged SELECT | no | 91.5 | 9.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 757.3 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=name | page 1 | type filter=none — before 1959.6 ms, after 1577.5 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1005.4 | 54.7% |
| COUNT | yes | 408.8 | 22.2% |
| paged SELECT | yes | 423.4 | 23.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1005.6 | 70.0% |
| COUNT | yes | 420.6 | 29.3% |
| paged SELECT | no | 10.4 | 0.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=name | page 1 | type filter=Poste — before 1571.4 ms, after 1273.1 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1019.9 | 69.4% |
| COUNT | no | 105.6 | 7.2% |
| paged SELECT | no | 345.2 | 23.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1015.5 | 89.7% |
| COUNT | no | 107.1 | 9.5% |
| paged SELECT | no | 10.0 | 0.9% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=name | page 200 | type filter=none — before 2029.5 ms, after 1588.3 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1018.3 | 52.8% |
| COUNT | yes | 404.5 | 21.0% |
| paged SELECT | yes | 507.4 | 26.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1008.7 | 70.5% |
| COUNT | yes | 398.7 | 27.8% |
| paged SELECT | no | 24.4 | 1.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=name | page 200 | type filter=Poste — before 1581.9 ms, after 1283.5 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1029.3 | 68.8% |
| COUNT | no | 120.3 | 8.0% |
| paged SELECT | no | 346.0 | 23.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1002.5 | 87.8% |
| COUNT | no | 110.8 | 9.7% |
| paged SELECT | no | 28.6 | 2.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=createdAt | page 1 | type filter=none — before 1946.8 ms, after 1569.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1028.8 | 55.4% |
| COUNT | yes | 409.6 | 22.0% |
| paged SELECT | yes | 420.0 | 22.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1011.8 | 71.3% |
| COUNT | yes | 398.1 | 28.0% |
| paged SELECT | no | 9.9 | 0.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=createdAt | page 1 | type filter=Poste — before 1565.2 ms, after 1330.4 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1014.2 | 69.1% |
| COUNT | no | 105.1 | 7.2% |
| paged SELECT | no | 348.5 | 23.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1032.4 | 88.9% |
| COUNT | no | 117.8 | 10.1% |
| paged SELECT | no | 10.5 | 0.9% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=createdAt | page 200 | type filter=none — before 1976.9 ms, after 1638.9 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1016.6 | 54.1% |
| COUNT | yes | 405.1 | 21.6% |
| paged SELECT | yes | 457.0 | 24.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1027.7 | 70.8% |
| COUNT | yes | 399.8 | 27.5% |
| paged SELECT | no | 24.8 | 1.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=createdAt | page 200 | type filter=Poste — before 1582.7 ms, after 1303.9 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1005.0 | 68.5% |
| COUNT | no | 111.4 | 7.6% |
| paged SELECT | no | 351.6 | 24.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1027.7 | 88.2% |
| COUNT | no | 108.7 | 9.3% |
| paged SELECT | no | 28.6 | 2.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=updatedAt | page 1 | type filter=none — before 1935.0 ms, after 1636.7 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1021.1 | 55.6% |
| COUNT | yes | 401.5 | 21.9% |
| paged SELECT | yes | 413.0 | 22.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1012.9 | 66.1% |
| COUNT | yes | 508.3 | 33.2% |
| paged SELECT | no | 10.1 | 0.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=updatedAt | page 1 | type filter=Poste — before 1588.1 ms, after 1286.0 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1040.9 | 68.6% |
| COUNT | no | 107.1 | 7.1% |
| paged SELECT | no | 370.1 | 24.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1031.0 | 89.2% |
| COUNT | no | 113.9 | 9.9% |
| paged SELECT | no | 10.9 | 0.9% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=updatedAt | page 200 | type filter=none — before 1998.2 ms, after 1589.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1181.0 | 57.0% |
| COUNT | yes | 420.4 | 20.3% |
| paged SELECT | yes | 469.9 | 22.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1119.9 | 69.4% |
| COUNT | yes | 470.4 | 29.1% |
| paged SELECT | no | 24.6 | 1.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>attribute key match | sort=updatedAt | page 200 | type filter=Poste — before 1569.4 ms, after 1360.3 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1037.8 | 69.4% |
| COUNT | no | 116.2 | 7.8% |
| paged SELECT | no | 341.8 | 22.9% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1028.8 | 87.8% |
| COUNT | no | 113.9 | 9.7% |
| paged SELECT | no | 28.6 | 2.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=name | page 1 | type filter=none — before 1039.4 ms, after 730.9 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 744.0 | 68.8% |
| COUNT | yes | 173.5 | 16.0% |
| paged SELECT | yes | 164.1 | 15.2% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 731.7 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=name | page 1 | type filter=Poste — before 854.4 ms, after 718.3 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 738.7 | 81.0% |
| COUNT | no | 86.6 | 9.5% |
| paged SELECT | no | 86.2 | 9.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 720.6 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=name | page 200 | type filter=none — before 1028.3 ms, after 716.9 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 723.6 | 68.7% |
| COUNT | yes | 166.2 | 15.8% |
| paged SELECT | yes | 164.2 | 15.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 721.4 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=name | page 200 | type filter=Poste — before 847.4 ms, after 714.6 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 730.1 | 81.0% |
| COUNT | no | 85.3 | 9.5% |
| paged SELECT | no | 86.4 | 9.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 728.8 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.0 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=createdAt | page 1 | type filter=none — before 1034.7 ms, after 710.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 742.2 | 68.5% |
| COUNT | yes | 172.1 | 15.9% |
| paged SELECT | yes | 168.9 | 15.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 718.1 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.0 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=createdAt | page 1 | type filter=Poste — before 858.9 ms, after 716.8 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 733.1 | 80.8% |
| COUNT | no | 86.2 | 9.5% |
| paged SELECT | no | 87.9 | 9.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 732.4 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.0 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=createdAt | page 200 | type filter=none — before 1030.2 ms, after 708.9 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 731.4 | 68.8% |
| COUNT | yes | 167.1 | 15.7% |
| paged SELECT | yes | 164.7 | 15.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 718.4 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.0 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=createdAt | page 200 | type filter=Poste — before 845.8 ms, after 720.9 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 740.1 | 81.1% |
| COUNT | no | 84.6 | 9.3% |
| paged SELECT | no | 87.4 | 9.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 729.4 | 100.0% |
| COUNT | no | 0.2 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=updatedAt | page 1 | type filter=none — before 1044.8 ms, after 711.6 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 726.2 | 68.6% |
| COUNT | yes | 167.8 | 15.9% |
| paged SELECT | yes | 164.3 | 15.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 723.4 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.0 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=updatedAt | page 1 | type filter=Poste — before 851.4 ms, after 725.3 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 741.9 | 81.3% |
| COUNT | no | 84.8 | 9.3% |
| paged SELECT | no | 86.0 | 9.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 942.9 | 99.5% |
| COUNT | no | 3.3 | 0.4% |
| paged SELECT | no | 1.0 | 0.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=updatedAt | page 200 | type filter=none — before 1036.9 ms, after 716.0 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 736.4 | 68.6% |
| COUNT | yes | 168.2 | 15.7% |
| paged SELECT | yes | 169.1 | 15.8% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 738.9 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.1 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>no match | sort=updatedAt | page 200 | type filter=Poste — before 849.5 ms, after 717.2 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 725.7 | 80.9% |
| COUNT | no | 85.0 | 9.5% |
| paged SELECT | no | 86.1 | 9.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 738.4 | 100.0% |
| COUNT | no | 0.1 | 0.0% |
| paged SELECT | no | 0.0 | 0.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=name | page 1 | type filter=none — before 1587.0 ms, after 1404.0 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1086.6 | 67.3% |
| COUNT | yes | 259.2 | 16.1% |
| paged SELECT | yes | 268.9 | 16.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1077.4 | 80.4% |
| COUNT | yes | 259.2 | 19.3% |
| paged SELECT | no | 3.6 | 0.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=name | page 1 | type filter=Poste — before 1388.9 ms, after 1240.7 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1065.4 | 77.6% |
| COUNT | no | 106.1 | 7.7% |
| paged SELECT | no | 200.7 | 14.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1071.9 | 90.5% |
| COUNT | no | 110.0 | 9.3% |
| paged SELECT | no | 3.2 | 0.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=name | page 200 | type filter=none — before 1615.5 ms, after 1432.5 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1158.3 | 67.6% |
| COUNT | yes | 267.6 | 15.6% |
| paged SELECT | yes | 287.4 | 16.8% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1084.6 | 78.2% |
| COUNT | yes | 255.9 | 18.5% |
| paged SELECT | no | 46.3 | 3.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=name | page 200 | type filter=Poste — before 1309.3 ms, after 1290.7 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1165.0 | 82.9% |
| COUNT | no | 113.7 | 8.1% |
| paged SELECT | no | 127.5 | 9.1% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1070.7 | 86.8% |
| COUNT | no | 104.7 | 8.5% |
| paged SELECT | no | 57.5 | 4.7% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=createdAt | page 1 | type filter=none — before 1612.5 ms, after 1381.6 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1061.2 | 67.4% |
| COUNT | yes | 251.4 | 16.0% |
| paged SELECT | yes | 261.6 | 16.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1060.6 | 80.5% |
| COUNT | yes | 252.1 | 19.1% |
| paged SELECT | no | 4.9 | 0.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=createdAt | page 1 | type filter=Poste — before 1385.0 ms, after 1230.3 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1084.3 | 78.1% |
| COUNT | no | 107.0 | 7.7% |
| paged SELECT | no | 197.2 | 14.2% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1072.4 | 91.1% |
| COUNT | no | 102.2 | 8.7% |
| paged SELECT | no | 3.1 | 0.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=createdAt | page 200 | type filter=none — before 1601.0 ms, after 1425.0 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1074.9 | 66.2% |
| COUNT | yes | 266.5 | 16.4% |
| paged SELECT | yes | 281.7 | 17.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1082.9 | 78.3% |
| COUNT | yes | 252.1 | 18.2% |
| paged SELECT | no | 47.7 | 3.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=createdAt | page 200 | type filter=Poste — before 1262.5 ms, after 1280.1 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1068.4 | 81.5% |
| COUNT | no | 110.8 | 8.5% |
| paged SELECT | no | 131.1 | 10.0% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1063.3 | 86.2% |
| COUNT | no | 110.1 | 8.9% |
| paged SELECT | no | 60.8 | 4.9% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=updatedAt | page 1 | type filter=none — before 1590.6 ms, after 1392.7 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1077.2 | 67.4% |
| COUNT | yes | 258.4 | 16.2% |
| paged SELECT | yes | 262.0 | 16.4% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1110.9 | 81.2% |
| COUNT | yes | 253.7 | 18.5% |
| paged SELECT | no | 3.1 | 0.2% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=updatedAt | page 1 | type filter=Poste — before 1370.7 ms, after 1233.1 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1067.3 | 77.8% |
| COUNT | no | 104.3 | 7.6% |
| paged SELECT | no | 199.8 | 14.6% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1069.9 | 90.9% |
| COUNT | no | 103.6 | 8.8% |
| paged SELECT | no | 3.1 | 0.3% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=updatedAt | page 200 | type filter=none — before 1602.7 ms, after 1441.4 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1085.0 | 66.8% |
| COUNT | yes | 260.1 | 16.0% |
| paged SELECT | yes | 279.8 | 17.2% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1069.3 | 77.6% |
| COUNT | yes | 260.0 | 18.9% |
| paged SELECT | no | 48.1 | 3.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

<details>
<summary>common term | sort=updatedAt | page 200 | type filter=Poste — before 1266.1 ms, after 1274.1 ms</summary>

**Before**

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1067.9 | 82.3% |
| COUNT | no | 106.4 | 8.2% |
| paged SELECT | no | 123.5 | 9.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |
|---|---|---:|---:|
| attr-match (raw) | no | 1080.8 | 87.2% |
| COUNT | no | 102.1 | 8.2% |
| paged SELECT | no | 56.0 | 4.5% |

<sub>**attr-match (raw) SQL:**</sub>

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

</details>

## Where the time goes

Aggregated across all 60 combinations, grouped by statement shape (n=60 each, before vs. after):

| Statement | Before avg (ms) | Before max (ms) | Before Seq Scans | After avg (ms) | After max (ms) | After Seq Scans |
|---|---:|---:|---:|---:|---:|---:|
| attr-match (raw, jsonb_each_text) | 883.7 | 1181.0 | 1/60 | 888.0 | 1119.9 | 0/60 |
| COUNT | 167.5 | 420.4 | 30/60 | 90.9 | 508.3 | 12/60 |
| paged SELECT | 201.8 | 507.4 | 30/60 | 9.5 | 60.9 | 0/60 |

**The always-run `jsonb_each_text` attribute-expansion subquery is the dominant, unconditional cost — and the 171 indexes don't touch it.** It runs on *every* `SearchText` query regardless of whether the search is even about custom attributes (it runs for "rare name" too), averages ~884-888 ms before *and* after, and is the single biggest line item in every combination: typically 60-90% of total SQL time for the narrow-match cases, and still the single largest statement even in the two heavy cases below. This matches the existing test suite's own `GetPageAsync_SearchTextOnCustomAttributes_StillSequentiallyScansDespiteGinIndex`, which already documents that the GIN index on `custom_attributes` serves containment operators, not this per-row-ILIKE-after-jsonb_each_text query shape — so it was never going to help here, and it didn't.

**The 171 indexes did help a lot — but only for the COUNT and paged SELECT statements, and only when the attribute-match subquery's result set is small.** For "rare name", "rare attribute value" and "no match" (0-1 matching ids), the paged SELECT's seq-scan rate went from 30/60 to 0/60 and its max time dropped from 507 ms to 61 ms — the trigram/B-tree indexes are doing exactly what they were built for. But for "attribute key match" (`altura_m`, matching all 200,000 rows) and "common term" (`concreto`, ~66,666 rows) **without** an AssetTypeId filter, the COUNT statement still sequentially scans `geo_entity` after the indexes (12/60 cases, all exactly these two search cases × no filter, 252-508 ms each) — once the `"Id" = ANY(@array)` parameter is tens of thousands of entries long, the planner abandons the Name/Description trigram indexes for that OR-branch and falls back to a sequential scan to evaluate array membership row-by-row. Adding the AssetTypeId filter (which narrows the candidate set via its own B-tree index *before* the array check) makes the seq scan disappear even for these two cases — the planner has a cheaper way in.

**Net effect on end-to-end latency:** even after the 171 indexes, `GetPageAsync` ranges from ~710-760 ms (no match / rare cases) up to ~1.2-2.0 s (attribute key match / common term, no type filter) on this 200k-row table — dominated by the ~880 ms fixed floor from the attribute-match subquery that runs unconditionally on every text search, which none of the existing indexes address. The suspicion that prompted this ticket is confirmed: the EXPLAIN tests went green, but the real search path is still slow, and the reason is a query shape the mirror-SQL tests never captured.
