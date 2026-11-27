# COELBA METROPOLITANA scale gate (XD01-448)

> **Fixed by XD01-449** (`docs/perf/XD01-449-bounded-organization-catalog.md`): an organization of more than 50,000
> features is now served from PostGIS; the same first read answers 200 and the silo stays around 200 MiB.

**Verdict: the gate fails.** The kernel cannot serve the organization that holds METROPOLITANA (783,827 features) in a
6 GiB container. The first read loads the whole organization catalog into memory (`PostgresAssetProvider.WarmCacheAsync`),
and that load throws `OutOfMemoryException`. The bounded/lazy catalog (XD01-449) is therefore a prerequisite for
METROPOLITANA itself, not only for the five other regions (about 9.2 M features).

The import path works at this scale: `import` loaded all 783,827 features, resumed from its checkpoint after the silo was
killed, and the database matches the offline plan exactly. Only **reading** the organization fails.

Measured on 2026-11-26 with `deploy/distributor-silo` (XD01-447), the silo `coelba` (database `geoassets_coelba`).

## Setup

| | |
|---|---|
| Host | Apple Silicon Mac, Docker Desktop VM with 7.65 GiB memory and 8 CPUs, shared with `deploy/server` (PostgresL, kernel, gateway, NeutralOperator) |
| Silo | `geoasset-server-coelba`, image built from `develop` at `ca265ae2`, module `postgres-spatial` only. Capped at 6 GiB (`docker update --memory 6g --memory-swap 6g`) so an OOM kills the silo and never the shared Postgres |
| Data | COELBA `Base Coelba Regional/METROPOLITANA` (Neoenergia GSE, SAD69 TM central meridian −42°), profile `neoenergia-gse`, `--include-geometry-only`, IBGE grid `br_ibge_SAD69_003.tif` |
| CLI | `tools/GeoAssets.Tools.NetworkImport`, Release build, on the host, client-credentials `GeoAssets.ServiceCaller` token |
| Sampling | `docker stats` every ~7 s (silo and PostgresL); kernel timings from the silo's own `WarmCacheAsync` log line |

## Results

### Import

| Run | What | Features | Chunks | Wall | Rate | Silo peak | Postgres peak |
|---|---|---|---|---|---|---|---|
| 1 | Fresh insert, organization never read (the LV layer left out, see below) | 547,168 | 112 | 499 s | ≈ 1,100 features/s | 443 MiB | 576 MiB |
| 2 | Re-import of everything + LV, organization catalog **warm** (547 k cached) | 595,000 sent | 119 | 866 s, then **OOM-killed** | ≈ 690 features/s (upserts) | > 6 GiB (killed) | 1.5 GiB |
| 2′ | Run 2 resumed from its checkpoint after a restart, catalog cold | 783,827 (119 chunks skipped) | 160 | 280 s | — | — | — |

- Upserting existing rows is about 1.6× slower than inserting: each chunk also replaces the rows' `geo_entity_attribute`
  entries.
- Run 2 died while sending POSTE chunks, before any `complete` (the CLI completes every scope at the end). The silo held
  about 4.25 GiB for the warm 547 k-feature catalog and exceeded 6 GiB within one sampling interval, under the per-chunk
  allocations alone. The cap had been applied to the running process, so its GC still sized itself for the 7.65 GiB VM.
  After a restart the GC sees the cap, which is why the next failure is an exception, not a kill.
- The CLI's resume worked as designed: the same `--import-id` and `--checkpoint` skipped the 119 acknowledged chunks.
- The CLI itself peaked at 1.08 GB RSS (1.3 GB for `plan` alone).

### Start-up and reads

| Measurement | Result |
|---|---|
| Silo start to `/healthz` 200 | 3.5 s (105 MiB). The organization's catalog is **not** loaded at boot: the start-up warm sees 0 rows |
| First read, 547,168 features (`GET …/organizations/{org}/features/{id}`) | 9.1 s; the silo grows from 228 MiB to **5.09 GiB** (≈ 9.3 KiB per feature resident) |
| Warm read by id | 3–30 ms |
| `GET …/features/{id}/descendants` (warm) | 2.7 s |
| First read, 783,827 features, 6 GiB cap known to the GC | **500** after 9.5 s: `System.OutOfMemoryException` in `PostgresAssetProvider.WarmCacheAsync`; the silo stays up at 4.6 GiB |
| The next reads | 500 again, each one restarting the whole load (`WarmOnceAsync` retries a faulted load): 98.7 s, 10.8 s |
| `/healthz` meanwhile | 200: the silo looks healthy while the organization is unusable |
| `verify --sample 1` | exit 3: the same 500 |

Projected from 9.3 KiB per feature: METROPOLITANA needs about 7.1 GiB resident for the catalog alone, and all of COELBA
about 80 GiB.

### Database

| | |
|---|---|
| `geoassets_coelba` | 8.5 GB |
| `geo_entity` | 783,827 rows, 1.9 GB (incl. indexes) |
| `geo_entity_attribute` | 21,964,527 rows (28 per feature), 6.6 GB: 77 % of the database |

At this ratio all of COELBA would be about 100 GB, most of it the attribute table. That is a separate cost from the memory
problem, and worth a look in XD01-449's design (which attributes need to be filterable).

### The import is correct

The kernel could not answer `verify`, so the import was checked against the plan's `features.ndjson` in the database:

| Check | Result |
|---|---|
| Planned ids missing from `geo_entity` | 0 of 783,827 |
| Rows not in the plan | 0 |
| Sample of 2,000 features: first vertex vs the plan | 0.0000 m max offset |
| Sample: topology edge count vs the plan | 0 mismatches |
| SRID / organization | 4326 / every row the COELBA organization |
| Counts per asset class, unresolved references (23,219), issues | Identical to `plan` |

## What XD01-449 has to remove

1. **The whole-organization load.** `PostgresOrganizationAssetCatalogs` → `PostgresAssetProvider.WarmCacheAsync` reads
   every row (`ToListAsync`) and builds a dictionary of `GeoFeature`s. Reads by id, by bounds and by topology must go to
   PostGIS (the primary key, the GiST index, the topology JSON or an edge table) with bounded memory.
2. **The reload on `complete`.** `RefreshAsync` reloads the whole cache after each scope's `complete`, while the old cache
   is still alive. That is two copies at once, for each of the 5 scopes of one region.
3. **The retry storm.** A failed load is retried by the next request, from scratch, as often as requests arrive.
4. **The silent failure.** `/healthz` (and `/readyz`) say nothing about an organization whose catalog cannot load.
5. **`descendants`.** It traverses the in-memory catalog (2.7 s warm), so it needs a bounded query too.

## Findings fixed in this ticket

- **CIAM issuer.** A token asked from `login.microsoftonline.com` (MSAL `WithTenantId`) carries that host as its issuer.
  The kernel accepts only its CIAM instance's issuer (`https://<tenant id>.ciamlogin.com/<tenant id>/v2.0`) and answers
  401. The CLI now requires `GEOASSETS_SERVICEAUTH_INSTANCE` and asks `<instance><tenant id>`. The runbook's `curl` was
  fixed the same way. The vertical hosts' `EntraCiamServiceAuthTokenProvider`s had the same bug, fixed by XD01-452.
- **No LV without `--include-geometry-only`.** METROPOLITANA's `REDE_BT` has no `.dbf` (`geometryOnly`). The runbook's
  `plan`/`import` commands left out the flag, so following it imported no LV network (run 1). They pass it now.
- **The silo's `ConnectionStrings__GeoAssets`** no longer carries the `:?…` error text that GitGuardian took for a password.
- **The kernel API registration had no `GeoAssets.ServiceCaller` app role.** It was added (runbook step 1b), and the
  `GeoAssets Network Import` registration was assigned it. Nothing in the repository changes for this.

## Re-measure

Follow `deploy/distributor-silo/README.md` (provision, organization, credentials, import), then:

```bash
docker update --memory 6g --memory-swap 6g geoasset-server-coelba && docker restart geoasset-server-coelba
docker stats --no-stream geoasset-server-coelba PostgresL
curl -s -o /dev/null -w 'first %{http_code} %{time_total}s\n' -H "Authorization: Bearer $TOKEN" \
  "http://127.0.0.1:8180/api/geoassets/organizations/$ORG_ID/features/$FEATURE_ID"
docker logs geoasset-server-coelba 2>&1 | grep -E 'WarmCacheAsync|OutOfMemory'
```

The gate passes when the first read of the 783,827-feature organization answers 200 in a 6 GiB silo, and when memory
stays bounded independently of the organization's size.
