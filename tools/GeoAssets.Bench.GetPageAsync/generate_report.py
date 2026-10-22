import json
import sys

before = json.load(open("before.json"))
after = json.load(open("after.json"))


def key(r):
    return (r["SearchCase"], r["SortBy"], r["Page"], r["TypeFilter"])


bmap = {key(r): r for r in before}
amap = {key(r): r for r in after}
assert bmap.keys() == amap.keys()

SEARCH_CASE_ORDER = ["rare name", "rare attribute value", "attribute key match", "no match", "common term"]
SORT_ORDER = ["name", "createdAt", "updatedAt"]
PAGE_ORDER = ["page 1", "page 200"]
FILTER_ORDER = ["none", "Poste"]


def sort_key(k):
    case, sort_by, page, filt = k
    return (SEARCH_CASE_ORDER.index(case), SORT_ORDER.index(sort_by), PAGE_ORDER.index(page), FILTER_ORDER.index(filt))


keys = sorted(bmap.keys(), key=sort_key)

lines = []
lines.append("# XD01-171 — Real search-path baseline (GetPageAsync)")
lines.append("")
lines.append(
    "This measures the **real** SQL `PostgresAssetProvider.GetPageAsync` sends — captured via an EF "
    "`DbCommandInterceptor`, not the hand-written \"mirror\" SQL the existing EXPLAIN tests in "
    "`PostgresAssetProviderGetPageAsyncTests.cs` run against. Those tests went green on 2026-10-01 "
    "(commit `5f1a65c`) after adding trigram/GIN/B-tree indexes, but they never exercised the method's "
    "actual query shape — in particular the always-run `jsonb_each_text` + `ILIKE` subquery used to "
    "match `SearchText` against custom-attribute keys/values, and the resulting `\"Id\" = ANY(@array)` "
    "parameter the subquery's matches get folded into."
)
lines.append("")
lines.append("## Methodology")
lines.append("")
lines.append(
    "The ticket asked to run this at commit `5f1a65c^` (before the indexes) and `5f1a65c` (after). "
    "`git diff 5f1a65c^ 5f1a65c -- providers/GeoAssets.Provider.PostgreSQL/Repositories/PostgresAssetProvider.cs` "
    "shows the **only** change to `GetPageAsync` in that commit is a comment — no behavior difference — "
    "and no later migration touches `geo_entity`. So instead of checking out and rebuilding two historical "
    "commits, this harness builds **one** binary at HEAD (on branch `xd01-171-real-search-path`) and toggles "
    "the *database schema* between \"no indexes\" (migrated down to `SeedDomainAssetTypesAndLayers`, the "
    "migration immediately before `AddGetPageAsyncSearchSortIndexes`) and \"with indexes\" (migrated to "
    "latest). This is equivalent in effect, far more robust, and — crucially — runs both states against "
    "**identical** seeded data instead of two separately-seeded tables."
)
lines.append("")
lines.append(
    "Database: a dedicated `geoassets-bench-postgres` container (postgis/postgis:16-3.4, port 5434, db "
    "`geoassets_bench`) — never the dev stack (port 5433, db `geoassets`). Compose file: "
    "`tools/GeoAssets.Bench.GetPageAsync/docker-compose.bench.yaml`. Harness: "
    "`tools/GeoAssets.Bench.GetPageAsync` (a console project — see the comment at the top of `Program.cs` "
    "for why this isn't an xUnit test)."
)
lines.append("")
lines.append("## Dataset")
lines.append("")
lines.append(
    "200,000 `geo_entity` rows, deterministic by construction (every field is a pure function of the row "
    "index 1..200000, so re-seeding always produces byte-identical data — no PRNG needed to get a fixed "
    "seed). Each row: `Name` = `P-NNNNNN`, `Description` = generic sector/feeder text (never mentions "
    "material, so the \"concreto\" search case is isolated to the custom-attributes path), `AssetTypeId` "
    "cycles through the 3 real seeded domain types (Poste / Transformador / Línea de baja tensión) on a "
    "`(row / 17) % 3` pattern — deliberately decorrelated from the material split below so the "
    "asset-type-filter test case isn't numerically identical to the \"concreto\" test case. Exactly 6 "
    "`custom_attributes` per row: `material` (concreto/madera/metal, exactly 1/3 each), `fases`, "
    "`altura_m`, `circuito`, `codigo_lote`, `serie` (unique per row)."
)
lines.append("")
lines.append("Fixed `SearchText` for the 5 cases, chosen to isolate one code path each:")
lines.append("")
lines.append("| Case | SearchText | Matches | Path probed |")
lines.append("|---|---|---|---|")
lines.append("| rare name | `P-100000` | 1 row | `Name` ILIKE |")
lines.append("| rare attribute value | `SN-100000` | 1 row | jsonb value ILIKE |")
lines.append("| attribute key match | `altura_m` | all 200,000 rows | jsonb **key** ILIKE (worst case) |")
lines.append("| no match | `zzz` | 0 rows | all branches, no hits |")
lines.append("| common term | `concreto` | ~66,666 rows | jsonb value ILIKE |")
lines.append("")
lines.append(
    "Matrix: 5 search cases × `SortBy` ∈ {name, createdAt, updatedAt} × page ∈ {1, 200 (Skip 9950)} × "
    "AssetTypeId filter ∈ {none, Poste} = 60 `AssetQuery` combinations, each run against the real "
    "`PostgresAssetProvider.GetPageAsync` with 1 warm-up + 5 timed calls (median reported)."
)
lines.append("")
lines.append("## Timing — median of 5 runs (ms)")
lines.append("")
lines.append("| Search case | Sort | Page | Type filter | Before (ms) | After (ms) |")
lines.append("|---|---|---|---|---:|---:|")
for k in keys:
    case, sort_by, page, filt = k
    b = bmap[k]["MedianMs"]
    a = amap[k]["MedianMs"]
    lines.append(f"| {case} | {sort_by} | {page} | {filt} | {b:.1f} | {a:.1f} |")
lines.append("")
lines.append(
    "Raw captured SQL text, bound parameters (array parameters truncated to 10 entries + a total count in "
    "the human-readable dump — the SQL text itself is never truncated) and full `EXPLAIN (ANALYZE, BUFFERS)` "
    "plan text for every one of these 360 statements (60 combinations × 3 statements × 2 states) are saved "
    "verbatim in `tools/GeoAssets.Bench.GetPageAsync/before.json` / `after.json`. The tables below summarize "
    "each statement's SQL shape, its Seq-Scan-on-`geo_entity` verdict, and its share of that combination's "
    "total SQL time."
)
lines.append("")


def classify(sql):
    if "jsonb_each_text" in sql:
        return "attr-match (raw)"
    if sql.strip().startswith("SELECT count"):
        return "COUNT"
    return "paged SELECT"


lines.append("## Per-combination detail")
lines.append("")
for k in keys:
    case, sort_by, page, filt = k
    b = bmap[k]
    a = amap[k]
    lines.append(f"<details>\n<summary>{case} | sort={sort_by} | {page} | type filter={filt} "
                 f"— before {b['MedianMs']:.1f} ms, after {a['MedianMs']:.1f} ms</summary>\n")
    for label, r in [("Before", b), ("After", a)]:
        lines.append(f"**{label}**\n")
        lines.append("| Statement | Seq Scan on geo_entity | Exec time (ms) | % of total |")
        lines.append("|---|---|---:|---:|")
        for s in r["Statements"]:
            kind = classify(s["Sql"])
            seq = "yes" if s["SeqScanOnGeoEntity"] else "no"
            exec_ms = s["ExecutionTimeMs"]
            exec_str = f"{exec_ms:.1f}" if exec_ms is not None else "n/a"
            lines.append(f"| {kind} | {seq} | {exec_str} | {s['PercentOfTotal']:.1f}% |")
        lines.append("")
        for s in r["Statements"]:
            kind = classify(s["Sql"])
            lines.append(f"<sub>**{kind} SQL:**</sub>\n")
            lines.append("```sql")
            lines.append(s["Sql"])
            lines.append("```")
            if s["Params"]:
                lines.append(f"<sub>Params: {'; '.join(s['Params'])}</sub>\n")
    lines.append("</details>\n")

lines.append("## Where the time goes")
lines.append("")
lines.append(
    "Aggregated across all 60 combinations, grouped by statement shape (n=60 each, before vs. after):\n\n"
    "| Statement | Before avg (ms) | Before max (ms) | Before Seq Scans | After avg (ms) | After max (ms) | After Seq Scans |\n"
    "|---|---:|---:|---:|---:|---:|---:|\n"
    "| attr-match (raw, jsonb_each_text) | 883.7 | 1181.0 | 1/60 | 888.0 | 1119.9 | 0/60 |\n"
    "| COUNT | 167.5 | 420.4 | 30/60 | 90.9 | 508.3 | 12/60 |\n"
    "| paged SELECT | 201.8 | 507.4 | 30/60 | 9.5 | 60.9 | 0/60 |\n"
)
lines.append(
    "**The always-run `jsonb_each_text` attribute-expansion subquery is the dominant, unconditional cost — "
    "and the 171 indexes don't touch it.** It runs on *every* `SearchText` query regardless of whether the "
    "search is even about custom attributes (it runs for \"rare name\" too), averages ~884-888 ms "
    "before *and* after, and is the single biggest line item in every combination: typically 60-90% of "
    "total SQL time for the narrow-match cases, and still the single largest statement even in the two "
    "heavy cases below. This matches the existing test suite's own "
    "`GetPageAsync_SearchTextOnCustomAttributes_StillSequentiallyScansDespiteGinIndex`, which already "
    "documents that the GIN index on `custom_attributes` serves containment operators, not this "
    "per-row-ILIKE-after-jsonb_each_text query shape — so it was never going to help here, and it didn't."
)
lines.append("")
lines.append(
    "**The 171 indexes did help a lot — but only for the COUNT and paged SELECT statements, and only when "
    "the attribute-match subquery's result set is small.** For \"rare name\", \"rare attribute value\" and "
    "\"no match\" (0-1 matching ids), the paged SELECT's seq-scan rate went from 30/60 to 0/60 and its max "
    "time dropped from 507 ms to 61 ms — the trigram/B-tree indexes are doing exactly what they were built "
    "for. But for \"attribute key match\" (`altura_m`, matching all 200,000 rows) and \"common term\" "
    "(`concreto`, ~66,666 rows) **without** an AssetTypeId filter, the COUNT statement still sequentially "
    "scans `geo_entity` after the indexes (12/60 cases, all exactly these two search cases × no filter, "
    "252-508 ms each) — once the `\"Id\" = ANY(@array)` parameter is tens of thousands of entries long, "
    "the planner abandons the Name/Description trigram indexes for that OR-branch and falls back to a "
    "sequential scan to evaluate array membership row-by-row. Adding the AssetTypeId filter (which narrows "
    "the candidate set via its own B-tree index *before* the array check) makes the seq scan disappear even "
    "for these two cases — the planner has a cheaper way in."
)
lines.append("")
lines.append(
    "**Net effect on end-to-end latency:** even after the 171 indexes, `GetPageAsync` ranges from "
    "~710-760 ms (no match / rare cases) up to ~1.2-2.0 s (attribute key match / common term, no type "
    "filter) on this 200k-row table — dominated by the ~880 ms fixed floor from the attribute-match "
    "subquery that runs unconditionally on every text search, which none of the existing indexes address. "
    "The suspicion that prompted this ticket is confirmed: the EXPLAIN tests went green, but the real "
    "search path is still slow, and the reason is a query shape the mirror-SQL tests never captured."
)
lines.append("")

with open("../../docs/perf/XD01-171-baseline.md", "w") as f:
    f.write("\n".join(lines))

print("wrote docs/perf/XD01-171-baseline.md,", len(lines), "lines")
