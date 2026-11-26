# GIS render baseline — z8 / z12 / z16 (XD01-420, ADR-002 Track 0)

The "before" numbers for [ADR-002](../architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md)'s render work: what
the map's **current** viewport path costs over a 200,000-feature PostGIS dataset at an overview (z8), mid (z12) and
street/edit (z16) zoom. Tracks 3, 4 and 5 re-run the same benchmark and add their rows next to these (see
[Re-running after Tracks 3, 4 and 5](#re-running-after-tracks-3-4-and-5)); ADR-002 Part 4's performance gate
(`z8` bytes down ≥ 10×, server p95 per tile < 50 ms) is judged against this page.

Harness: `tools/GeoAssets.Bench.Render` (console app, `seed` / `bench`). Raw results of this run:
`tools/GeoAssets.Bench.Render/baseline.json`.

## What is measured

The Web client's map (`MapContainer` → `RestAssetProvider.GetInBoundsRawJsonChunksAsync`) asks the kernel for
`GET /api/geoassets/features/bounds?minLon=…&maxLat=…` on every pan/zoom — one request per viewport at these zooms (the
2×2 tiling only starts above 50 deg²; a 1580 × 1020 z8 viewport is ~45 deg²). **No zoom reaches the server**: every zoom gets every
intersecting row, and the client drops the asset types whose `MinZoom`/`MaxZoom` exclude the current zoom.

| Column | Meaning |
|---|---|
| Matched rows | Rows the bbox query returns — all of them are read and mapped to `GeoFeature` by the server. |
| Returned features | Features in the response body: the endpoint caps at 10,000 (`MaxBoundsFeatureCount`), **after** mapping every match, in whatever order PostgreSQL returned them. |
| Drawn features | Returned features whose asset type the map draws at that zoom (the rest are parsed and turned into layers, but never shown). |
| Payload | Response body size, uncompressed — what goes over the wire today: neither the kernel nor the gateway compresses, and the body is serialized with `GeoJsonSerializer.Options` (**indented**). The gzip column (ASP.NET Core's default `Fastest` level) is informational, for Track 3's compression work. |
| Server ms | The kernel's pipeline, in-process (`BoundsEndpointPipeline`): the EF-equivalent query (`"OrganizationId" = ∅ AND geom IS NOT NULL AND ST_Intersects(geom, @bbox)`), reading every row, `PostgresAssetProvider.MapToFeature`, the cap, and serialization. `tests/GeoAssets.Bench.Render.Tests` asserts this produces exactly the body `PostgresAssetProvider.GetInBoundsAsync` + the endpoint produce. Excludes Kestrel, authentication/authorization, the gateway hop and EF Core's materializer (so the real endpoint is somewhat slower). |
| ↳ DB query + read ms | The part of Server ms spent executing the query and reading the rows (geometry decoding included). |
| Client render ms (simulated) | A headless stand-in for `geoassets.js`'s `renderFeatureBatch` (SVG renderer): parse the whole body, then for each drawn feature project every vertex to layer pixels (`latLngToLayerPoint`), simplify with Leaflet's `LineUtil.simplify` (1 px) and build the SVG path data. A **lower bound** measured in .NET: no DOM/SVG node creation, styling, layout, rasterization, JS engine, WASM interop or rAF chunking. Not a browser time-to-paint. |

Size/count percentiles are over the zoom's 12 viewports (one value each — the response is deterministic); timing
percentiles are over all 60 measured requests (12 viewports × 5 passes, after 1 warm-up pass). Percentiles are
nearest-rank. Requests run one at a time (per-request cost, not contention).

## Dataset

200,000 `geo_entity` rows generated from seed `420` (SplitMix64; same rows on every machine), COPY-loaded into a schema
migrated with `GeoAssetsDbContext`'s full chain (GiST on `geom`), then `ANALYZE`d. 3,366,132 vertices; `geo_entity` is
225 MB on disk. A power-distribution concession of 2.5° × 2.0° (−47.5…−45.0, −24.0…−22.0): 75 % of features in 12 towns
(one metro core at −46.63/−23.55, σ 0.12°; 11 seeded towns, σ 0.02–0.08°), 25 % rural uniform; types interleaved like a
real table, coordinates at 7 decimals.

| Asset type | Geometry | Features | Vertices each | Size | `MinZoom`…`MaxZoom` |
|---|---|---:|---|---|---|
| Bench — Poste | Point | 120,000 | 1 | — | 15…∞ |
| Bench — Transformador | Point | 20,000 | 1 | — | 13…∞ |
| Bench — Rede MT (feeder) | LineString | 15,000 | 64–160 | 0.8–3 km | 10…∞ |
| Bench — Rede BT (service line) | LineString | 25,000 | 8–32 | 80–400 m | 14…∞ |
| Bench — Lote (parcel) | Polygon | 15,000 | 6–24 (+ closing) | r 10–40 m | 16…∞ |
| Bench — Setor (district) | Polygon | 5,000 | 64–256 (+ closing) | r 0.5–2.5 km | 8…13 |

Each row carries 5 custom attributes, a ~60-character description and empty topology, organization-less
(`Guid.Empty`). Viewports: 1580 × 1020 px (the map area of a 1920×1080 browser, as in
[gateway-rate-limits.md](gateway-rate-limits.md)), Web Mercator, 12 centers per zoom drawn from the dataset's own
density (seeded by the dataset seed and the zoom).

## Environment

| | |
|---|---|
| Measured | 2026-11-26, branch `feat/xd01-420-render-bench` (base `develop` @ `587c72ed`) |
| Machine | Apple M5, 10 cores, 32 GB; macOS 27.2.1 (Arm64) |
| Bench process | .NET 10.0.7, Release build, server GC |
| Database | PostgreSQL 16.4 + PostGIS 3.4.3 (`postgis/postgis:16-3.4`, Docker Desktop 29.8.2, 8 vCPU / 7.7 GB VM). **The image is amd64-only, so PostgreSQL ran under emulation on this arm64 host** — DB times are inflated against native hardware; compare runs on the same machine and image only. |
| Isolation | Run under the machine-wide heavy-run lock (no concurrent test suites); other containers idle. |

## Baseline (2026-11-26)

Each cell is p50 / p95.

| Zoom | Matched rows | Returned features | Drawn features | Payload (uncompressed) | Payload (gzip, informational) | Server ms | ↳ DB query + read ms | Client render ms (simulated) |
|---|---|---|---|---|---|---|---|---|
| z8 | 200,000 / 200,000 | 10,000 / 10,000 | 235 / 235 | 18.15 MB / 18.15 MB | 3.05 MB / 3.05 MB | 1372.6 / 2388.0 | 1033.5 / 1790.9 | 41.3 / 80.7 |
| z12 | 12,882 / 27,589 | 10,000 / 10,000 | 992 / 1,186 | 18.79 MB / 20.31 MB | 3.16 MB / 3.49 MB | 170.6 / 263.2 | 125.6 / 202.1 | 51.0 / 57.8 |
| z16 | 32 / 897 | 32 / 897 | 29 / 818 | 92.5 kB / 2.72 MB | 17.5 kB / 493.4 kB | 1.0 / 11.0 | 0.8 / 5.0 | 0.3 / 7.6 |

### What it shows

- **z8 is the worst case by far.** Every viewport contains the whole concession, so the server reads, decodes and maps
  all 200,000 rows (3.4 M vertices) per pan — ~1.0 s of it in the query/read alone — to send an arbitrary 10,000 of
  them as an 18 MB indented body. Only 235 of those (2.4 %, the `Setor` districts that happened to be in the first
  10,000) are drawn at z8; the poles, transformers, lines and parcels in the body are all hidden by their `MinZoom`.
  What the map shows at z8 is incomplete *and* nearly all of the payload is wasted. Target from ADR-002 Part 4:
  ≤ 1.8 MB (≥ 10×).
- **z12 hits the cap too** in 9 of 12 viewports: ~19 MB bodies of which ~10 % is drawn (feeders and districts), and the
  part of the network that is drawn depends on which 10,000 rows come back first.
- **z16 is cheap** (median 32 features, 92 kB, ~1 ms), up to ~0.9 k features / 2.7 MB in the densest towns.
- **Compact JSON + gzip alone** (Track 3) would cut bytes ~6× (gzip column), but not the server's read of every
  intersecting row, nor the hidden-type waste — that needs the zoom to reach the SQL (`MinZoom`/`MaxZoom` + `LIMIT`) and,
  for z8, tiles/LOD (Tracks 4–5).
- The simulated client cost tracks *drawn* vertices (z12's ~36 k path vertices ≈ 50 ms); the real browser adds DOM/SVG
  work per drawn feature on top, and still pays the transfer and `JSON.parse` of the hidden 90–97 %.

<details>
<summary>Per-viewport results (first measured pass; ms = the viewport's p50)</summary>

#### z8

| # | Center (lon, lat) | Matched | Returned | Drawn | Path vertices | Payload | gzip | Server ms | Client ms |
|---|---|---|---|---|---|---|---|---|---|
| 1 | -46.7468, -23.5309 | 200,000 | 10,000 | 235 | 2,409 | 18.15 MB | 3.05 MB | 1481.4 | 41.2 |
| 2 | -46.7617, -23.4770 | 200,000 | 10,000 | 235 | 2,408 | 18.15 MB | 3.05 MB | 1402.8 | 42.5 |
| 3 | -46.3412, -22.5534 | 200,000 | 10,000 | 235 | 2,403 | 18.15 MB | 3.05 MB | 1338.8 | 40.3 |
| 4 | -45.6628, -23.3672 | 200,000 | 10,000 | 235 | 2,408 | 18.15 MB | 3.05 MB | 1333.8 | 40.4 |
| 5 | -45.5599, -22.1990 | 200,000 | 10,000 | 235 | 2,398 | 18.15 MB | 3.05 MB | 1471.8 | 42.2 |
| 6 | -45.4998, -22.2362 | 200,000 | 10,000 | 235 | 2,403 | 18.15 MB | 3.05 MB | 1154.9 | 42.6 |
| 7 | -46.6450, -23.0692 | 200,000 | 10,000 | 235 | 2,408 | 18.15 MB | 3.05 MB | 1503.0 | 41.0 |
| 8 | -45.0971, -22.5257 | 200,000 | 10,000 | 235 | 2,403 | 18.15 MB | 3.05 MB | 1176.1 | 42.3 |
| 9 | -46.1336, -23.0456 | 200,000 | 10,000 | 235 | 2,404 | 18.15 MB | 3.05 MB | 1453.8 | 42.1 |
| 10 | -46.6977, -23.4957 | 200,000 | 10,000 | 235 | 2,408 | 18.15 MB | 3.05 MB | 1284.6 | 40.3 |
| 11 | -45.3450, -22.2220 | 200,000 | 10,000 | 235 | 2,398 | 18.15 MB | 3.05 MB | 1372.6 | 41.4 |
| 12 | -45.6222, -23.2969 | 200,000 | 10,000 | 235 | 2,408 | 18.15 MB | 3.05 MB | 1243.5 | 41.6 |

#### z12

| # | Center (lon, lat) | Matched | Returned | Drawn | Path vertices | Payload | gzip | Server ms | Client ms |
|---|---|---|---|---|---|---|---|---|---|
| 1 | -46.0546, -22.9418 | 17,942 | 10,000 | 983 | 35,881 | 18.72 MB | 3.15 MB | 227.2 | 51.2 |
| 2 | -45.1899, -23.7373 | 1,430 | 1,430 | 159 | 6,621 | 2.92 MB | 510.2 kB | 111.7 | 8.0 |
| 3 | -46.2892, -23.3176 | 12,927 | 10,000 | 1,042 | 36,903 | 19.10 MB | 3.23 MB | 164.4 | 51.0 |
| 4 | -46.4704, -23.1546 | 12,882 | 10,000 | 1,186 | 44,096 | 20.31 MB | 3.49 MB | 170.8 | 57.4 |
| 5 | -46.0670, -22.2774 | 14,930 | 10,000 | 1,046 | 34,951 | 19.22 MB | 3.25 MB | 167.4 | 53.2 |
| 6 | -47.2049, -22.8610 | 10,025 | 10,000 | 958 | 33,736 | 18.60 MB | 3.10 MB | 163.4 | 50.0 |
| 7 | -46.2796, -23.2883 | 11,920 | 10,000 | 1,046 | 36,989 | 19.10 MB | 3.23 MB | 178.1 | 52.3 |
| 8 | -46.3051, -23.3351 | 14,090 | 10,000 | 1,066 | 37,431 | 19.24 MB | 3.26 MB | 167.0 | 53.4 |
| 9 | -46.6491, -23.4364 | 27,589 | 10,000 | 1,046 | 34,280 | 19.16 MB | 3.24 MB | 262.0 | 51.5 |
| 10 | -46.9668, -22.5645 | 1,827 | 1,827 | 207 | 6,970 | 3.62 MB | 633.2 kB | 115.0 | 10.0 |
| 11 | -45.5262, -22.2781 | 9,873 | 9,873 | 992 | 34,356 | 18.65 MB | 3.14 MB | 173.6 | 49.4 |
| 12 | -46.1278, -22.9329 | 17,913 | 10,000 | 983 | 36,932 | 18.79 MB | 3.16 MB | 221.2 | 50.0 |

#### z16

| # | Center (lon, lat) | Matched | Returned | Drawn | Path vertices | Payload | gzip | Server ms | Client ms |
|---|---|---|---|---|---|---|---|---|---|
| 1 | -46.0856, -22.2488 | 253 | 253 | 222 | 1,665 | 907.5 kB | 167.0 kB | 4.1 | 2.4 |
| 2 | -46.0833, -22.3288 | 897 | 897 | 818 | 5,454 | 2.72 MB | 493.4 kB | 11.6 | 7.6 |
| 3 | -46.6197, -22.8717 | 73 | 73 | 63 | 508 | 269.4 kB | 50.2 kB | 2.1 | 0.7 |
| 4 | -45.9215, -23.3346 | 667 | 667 | 602 | 3,999 | 2.02 MB | 364.9 kB | 8.4 | 5.5 |
| 5 | -46.9435, -23.9813 | 3 | 3 | 3 | 3 | 2.4 kB | 0.8 kB | 0.9 | 0.0 |
| 6 | -46.9950, -23.0116 | 6 | 6 | 5 | 63 | 33.8 kB | 7.0 kB | 0.7 | 0.1 |
| 7 | -46.2734, -22.4417 | 17 | 17 | 14 | 67 | 60.3 kB | 11.4 kB | 0.7 | 0.2 |
| 8 | -46.6655, -22.8303 | 34 | 34 | 29 | 273 | 130.2 kB | 24.6 kB | 1.0 | 0.4 |
| 9 | -46.2093, -23.5036 | 6 | 6 | 5 | 40 | 26.0 kB | 5.2 kB | 0.5 | 0.1 |
| 10 | -46.3676, -23.5558 | 32 | 32 | 29 | 237 | 92.5 kB | 17.5 kB | 0.9 | 0.2 |
| 11 | -45.8866, -23.2974 | 509 | 509 | 451 | 3,218 | 1.63 MB | 300.9 kB | 7.6 | 4.2 |
| 12 | -45.4009, -22.3429 | 6 | 6 | 5 | 36 | 17.9 kB | 3.7 kB | 0.9 | 0.1 |

</details>

## After XD01-430 — Track 3 server quick wins (2026-11-26)

Same dataset (seed 420, 200,000 rows, 3,366,132 vertices), viewports, settings and machine as the baseline (Apple M5,
.NET 10.0.7 Release + server GC, `postgis/postgis:16-3.4` amd64-emulated on Docker Desktop 29.8.2, run under the
heavy-run lock), measured on branch `feat/xd01-430-server-render-quick-wins` (base `develop` @ `fdbc8df8`). Raw results:
`tools/GeoAssets.Bench.Render/after-xd01-430.json`.

What changed in the measured read (`BoundsEndpointPipeline` follows it; `PostgresRenderBenchTests` asserts its body is
byte-for-byte the provider's):

- The zoom the client sends since XD01-429 (`&zoom=N`) reaches the SQL: one small query for the asset types hidden at
  that zoom (`"MinZoom" > @zoom OR "MaxZoom" < @zoom`), then `NOT ("AssetTypeId" = ANY(@hidden))` on the features.
- The cap is the query's `ORDER BY "Id" COLLATE "C" LIMIT 10000`, so only the returned rows are read and mapped, and
  a capped answer is always the same rows.
- The body is compact JSON (`GeoJsonSerializer.CompactOptions`), and the kernel compresses it (Brotli, else gzip, both
  `Fastest`). **Server ms now includes the Brotli step**, which the endpoint pays and the baseline did not.

Columns as in the baseline, except: *Matched rows* = rows the read's filter matches (bbox, scope **and** the zoom's
types) before the `LIMIT`, counted by a separate untimed query; *Payload (Brotli, on the wire)* is what a browser gets;
*Payload (gzip)* is what a client that accepts gzip but not Brotli gets.

Each cell is p50 / p95.

| Zoom | Matched rows | Returned features | Drawn features | Payload (uncompressed) | Payload (Brotli, on the wire) | Payload (gzip) | Server ms | ↳ DB query + read ms | Client render ms (simulated) |
|---|---|---|---|---|---|---|---|---|---|
| z8 | 5,000 / 5,000 | 5,000 / 5,000 | 5,000 / 5,000 | 23.55 MB / 23.55 MB | 7.38 MB / 7.38 MB | 10.74 MB / 10.74 MB | 386.5 / 436.1 | 220.1 / 244.4 | 145.1 / 154.9 |
| z12 | 1,364 / 2,911 | 1,364 / 2,911 | 1,364 / 2,911 | 5.13 MB / 10.90 MB | 1.47 MB / 3.13 MB | 2.16 MB / 4.60 MB | 138.4 / 229.2 | 100.4 / 156.9 | 34.7 / 71.7 |
| z16 | 29 / 818 | 29 / 818 | 29 / 818 | 30.6 kB / 864.0 kB | 7.3 kB / 170.9 kB | 9.3 kB / 248.5 kB | 1.1 / 8.6 | 0.9 / 4.8 | 0.1 / 3.5 |

### Before → after (p50 / p95)

| Zoom | Wire bytes: baseline (uncompressed) → after (Brotli) | Server ms | Returned → drawn, baseline | Returned → drawn, after |
|---|---|---|---|---|
| z8 | 18.15 MB → 7.38 MB (2.5×) | 1372.6 / 2388.0 → 386.5 / 436.1 (3.6× / 5.5×) | 10,000 → 235 | 5,000 → 5,000 |
| z12 | 18.79 / 20.31 MB → 1.47 / 3.13 MB (12.8× / 6.5×) | 170.6 / 263.2 → 138.4 / 229.2 | 10,000 → 992 | 1,364 → 1,364 |
| z16 | 92.5 kB / 2.72 MB → 7.3 / 170.9 kB (12.7× / 15.9×) | 1.0 / 11.0 → 1.1 / 8.6 | 32 → 29 | 29 → 29 |

### What it shows

- **Every returned feature is now drawn**, at every zoom and viewport: the hidden types never leave the database, and
  no viewport reaches the 10,000 cap any more (z12's densest, #9, returns 2,911 of what used to be 27,589 matches).
- **z8 is complete, but not small.** The baseline drew 235 of the 5,000 districts (`Setor`, the only type drawn at
  z8) — whichever happened to be in its arbitrary 10,000; it now sends all 5,000, which are the dataset's largest
  polygons (64–256 vertices each). So the uncompressed body *grew* (18.15 → 23.55 MB) while the bytes on the wire fell
  2.5× (7.38 MB Brotli) and the server time 3.6× / 5.5× (no more reading and mapping 200,000 rows to cut 190,000). The
  simulated client cost rises with it (41 → 145 ms), because it now draws 21× the features. ADR-002 Part 4's z8 gate
  (≥ 10× fewer bytes, ≤ 1.8 MB) is **not** met by Track 3 and was not expected to be: 5,000 full-resolution polygons
  per overview is the case for vector/raster tiles with simplification (Tracks 4–5).
- **z12 and z16 shrink ~13× on the wire** (p50): at z16 it is mostly compact JSON (92.5 → 30.6 kB uncompressed, 32
  → 29 features — the 3 left out are hidden districts), at z12 mostly the zoom filter (~19 MB → ~5 MB uncompressed);
  Brotli then takes another ~3.5–4×.
- **Server time at z12/z16 is flat to slightly lower**; at z12 most of it is still reading the drawn features'
  geometry (the `MinZoom` 10 feeders, 64–160 vertices each), plus the Brotli step the baseline did not pay.

<details>
<summary>Per-viewport results after XD01-430 (first measured pass; ms = the viewport's p50)</summary>

#### z8

| # | Center (lon, lat) | Matched | Returned | Drawn | Path vertices | Payload | Brotli | gzip | Server ms | Client ms |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | -46.7468, -23.5309 | 5,000 | 5,000 | 5,000 | 48,575 | 23.55 MB | 7.38 MB | 10.74 MB | 392.3 | 151.9 |
| 2 | -46.7617, -23.4770 | 5,000 | 5,000 | 5,000 | 48,573 | 23.55 MB | 7.38 MB | 10.74 MB | 379.1 | 149.8 |
| 3 | -46.3412, -22.5534 | 5,000 | 5,000 | 5,000 | 48,534 | 23.55 MB | 7.38 MB | 10.74 MB | 380.2 | 138.5 |
| 4 | -45.6628, -23.3672 | 5,000 | 5,000 | 5,000 | 48,577 | 23.55 MB | 7.38 MB | 10.74 MB | 392.8 | 145.1 |
| 5 | -45.5599, -22.1990 | 5,000 | 5,000 | 5,000 | 48,529 | 23.55 MB | 7.38 MB | 10.74 MB | 392.3 | 147.8 |
| 6 | -45.4998, -22.2362 | 5,000 | 5,000 | 5,000 | 48,538 | 23.55 MB | 7.38 MB | 10.74 MB | 389.2 | 145.5 |
| 7 | -46.6450, -23.0692 | 5,000 | 5,000 | 5,000 | 48,550 | 23.55 MB | 7.38 MB | 10.74 MB | 379.1 | 141.9 |
| 8 | -45.0971, -22.5257 | 5,000 | 5,000 | 5,000 | 48,567 | 23.55 MB | 7.38 MB | 10.74 MB | 389.4 | 149.6 |
| 9 | -46.1336, -23.0456 | 5,000 | 5,000 | 5,000 | 48,546 | 23.55 MB | 7.38 MB | 10.74 MB | 386.5 | 151.2 |
| 10 | -46.6977, -23.4957 | 5,000 | 5,000 | 5,000 | 48,575 | 23.55 MB | 7.38 MB | 10.74 MB | 390.0 | 134.9 |
| 11 | -45.3450, -22.2220 | 5,000 | 5,000 | 5,000 | 48,536 | 23.55 MB | 7.38 MB | 10.74 MB | 375.6 | 144.5 |
| 12 | -45.6222, -23.2969 | 5,000 | 5,000 | 5,000 | 48,569 | 23.55 MB | 7.38 MB | 10.74 MB | 398.1 | 141.5 |

#### z12

| # | Center (lon, lat) | Matched | Returned | Drawn | Path vertices | Payload | Brotli | gzip | Server ms | Client ms |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | -46.0546, -22.9418 | 1,769 | 1,769 | 1,769 | 62,739 | 6.67 MB | 1.94 MB | 2.82 MB | 197.9 | 59.2 |
| 2 | -45.1899, -23.7373 | 159 | 159 | 159 | 6,621 | 627.3 kB | 182.8 kB | 270.2 kB | 97.5 | 8.0 |
| 3 | -46.2892, -23.3176 | 1,364 | 1,364 | 1,364 | 49,436 | 5.13 MB | 1.47 MB | 2.16 MB | 141.5 | 33.3 |
| 4 | -46.4704, -23.1546 | 1,518 | 1,518 | 1,518 | 57,551 | 5.70 MB | 1.65 MB | 2.42 MB | 140.3 | 37.3 |
| 5 | -46.0670, -22.2774 | 1,564 | 1,564 | 1,564 | 53,116 | 5.94 MB | 1.71 MB | 2.49 MB | 147.0 | 37.5 |
| 6 | -47.2049, -22.8610 | 959 | 959 | 959 | 33,742 | 3.63 MB | 1.03 MB | 1.50 MB | 121.2 | 22.5 |
| 7 | -46.2796, -23.2883 | 1,259 | 1,259 | 1,259 | 44,719 | 4.71 MB | 1.35 MB | 1.98 MB | 133.3 | 30.6 |
| 8 | -46.3051, -23.3351 | 1,514 | 1,514 | 1,514 | 55,130 | 5.70 MB | 1.64 MB | 2.40 MB | 144.1 | 36.1 |
| 9 | -46.6491, -23.4364 | 2,911 | 2,911 | 2,911 | 98,955 | 10.90 MB | 3.13 MB | 4.60 MB | 231.8 | 77.4 |
| 10 | -46.9668, -22.5645 | 207 | 207 | 207 | 6,970 | 779.6 kB | 228.3 kB | 334.1 kB | 99.0 | 5.4 |
| 11 | -45.5262, -22.2781 | 992 | 992 | 992 | 34,356 | 3.69 MB | 1.05 MB | 1.53 MB | 131.5 | 27.6 |
| 12 | -46.1278, -22.9329 | 1,765 | 1,765 | 1,765 | 63,249 | 6.67 MB | 1.94 MB | 2.82 MB | 200.7 | 59.6 |

#### z16

| # | Center (lon, lat) | Matched | Returned | Drawn | Path vertices | Payload | Brotli | gzip | Server ms | Client ms |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | -46.0856, -22.2488 | 222 | 222 | 222 | 1,665 | 244.8 kB | 49.8 kB | 72.9 kB | 3.2 | 1.0 |
| 2 | -46.0833, -22.3288 | 818 | 818 | 818 | 5,454 | 864.0 kB | 170.9 kB | 248.5 kB | 8.6 | 3.5 |
| 3 | -46.6197, -22.8717 | 63 | 63 | 63 | 508 | 72.0 kB | 15.0 kB | 22.4 kB | 1.6 | 0.3 |
| 4 | -45.9215, -23.3346 | 602 | 602 | 602 | 3,999 | 635.5 kB | 125.5 kB | 181.8 kB | 7.5 | 2.6 |
| 5 | -46.9435, -23.9813 | 3 | 3 | 3 | 3 | 1.7 kB | 0.6 kB | 0.7 kB | 0.9 | 0.0 |
| 6 | -46.9950, -23.0116 | 5 | 5 | 5 | 63 | 9.5 kB | 3.0 kB | 3.8 kB | 0.9 | 0.1 |
| 7 | -46.2734, -22.4417 | 14 | 14 | 14 | 67 | 12.8 kB | 3.1 kB | 3.9 kB | 0.8 | 0.1 |
| 8 | -46.6655, -22.8303 | 29 | 29 | 29 | 273 | 37.4 kB | 8.0 kB | 12.3 kB | 1.1 | 0.2 |
| 9 | -46.2093, -23.5036 | 5 | 5 | 5 | 40 | 6.8 kB | 2.0 kB | 2.6 kB | 0.7 | 0.0 |
| 10 | -46.3676, -23.5558 | 29 | 29 | 29 | 237 | 30.6 kB | 7.3 kB | 9.3 kB | 1.0 | 0.1 |
| 11 | -45.8866, -23.2974 | 451 | 451 | 451 | 3,218 | 471.3 kB | 94.3 kB | 137.5 kB | 5.2 | 1.9 |
| 12 | -45.4009, -22.3429 | 5 | 5 | 5 | 36 | 5.4 kB | 1.5 kB | 1.9 kB | 0.8 | 0.0 |

</details>

## Re-running

From the repository root, against the benchmark's own container (never the dev stack — `seed` empties `geo_entity`, and
refuses a database whose name lacks `bench`):

```bash
# 1. PostGIS on localhost:5435, database geoassets_render_bench
docker compose -f tools/GeoAssets.Bench.Render/docker-compose.bench.yaml up -d --wait

# 2. The kernel schema (the tool cannot reference GeoAssets.Provider.PostgreSQL — Invariant 1)
ConnectionStrings__GeoAssets="Host=localhost;Port=5435;Database=geoassets_render_bench;Username=postgres;Password=postgres" \
  dotnet ef database update --project providers/GeoAssets.Provider.PostgreSQL \
    --startup-project providers/GeoAssets.Provider.PostgreSQL --context GeoAssetsDbContext

# 3. Seed (≈ 10 s) and measure (≈ 7 min on the machine above); Release build
dotnet build tools/GeoAssets.Bench.Render -c Release
dotnet tools/GeoAssets.Bench.Render/bin/Release/net10.0/GeoAssets.Bench.Render.dll seed
dotnet tools/GeoAssets.Bench.Render/bin/Release/net10.0/GeoAssets.Bench.Render.dll bench --out results.json

docker compose -f tools/GeoAssets.Bench.Render/docker-compose.bench.yaml down
```

`bench` prints the tables above (Markdown) and, with `--out`, the full JSON (environment, settings, per-viewport rows).
Defaults reproduce this baseline: `--seed 420 --zooms 8,12,16 --viewports 12 --iterations 5 --warmup 1 --viewport 1580x1020`
(`seed --features 200000`). Another database: `GEOASSETS_RENDER_BENCH_CONNECTION`. Run nothing else heavy meanwhile,
and record the machine and image with the numbers — timings only compare within one environment.

## Re-running after Tracks 3, 4 and 5

Keep the dataset, seed and viewports unchanged, and add the new run's table under its own dated heading below the
baseline (don't overwrite it):

- **Track 3 (XD01-429 / XD01-430)** — done: see [After XD01-430](#after-xd01-430--track-3-server-quick-wins-2026-11-26).
  `BoundsEndpointPipeline` now reproduces `PostgresAssetProvider.GetInViewportAsync` (zoom, `LIMIT`, compact JSON,
  Brotli); when the read changes again, update the pipeline in the same change —
  `PostgresRenderBenchTests.BoundsRead_AnswersTheProvidersBody_ForTheMeasuredViewports` fails until they agree.
- **Track 4 (XD01-431…XD01-434)** — `ST_AsMVT` vector tiles and Skia raster tiles: add a tile scenario that requests the
  256-px tiles covering each of these viewports (z8: ~40 tiles at 1580 × 1020) and reports bytes per viewport and server
  p95 **per tile** — ADR-002's `< 50 ms` gate is per tile, while the baseline's server ms is per viewport request.
- **Track 5 (XD01-435 / XD01-436)** — MapLibre / hybrid LOD: the simulated SVG client does not model WebGL; measure the
  client side in a real browser and note the method next to the numbers.
