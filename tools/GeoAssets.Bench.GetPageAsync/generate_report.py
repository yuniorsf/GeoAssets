import json
import re
import sys

before = json.load(open("before.json"))
after = json.load(open("after.json"))
fix = json.load(open("after-fix.json"))


def key(r):
    return (r["SearchCase"], r["SortBy"], r["Page"], r["TypeFilter"])


bmap = {key(r): r for r in before}
amap = {key(r): r for r in after}
fmap = {key(r): r for r in fix}
assert bmap.keys() == amap.keys() == fmap.keys()

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
lines.append(
    "**Phase 4 addition:** a third state, \"after fix\", measures the real fix landed in commit `fbe637c` on "
    "this branch — a normalized, trigram-indexed `geo_entity_attribute(EntityId, Key, Value)` table that "
    "`GetPageAsync`'s attribute match now queries instead of the unconditional `jsonb_each_text` scan over "
    "`geo_entity`. The same bench container and the same 200,000 Phase-1-seeded `geo_entity` rows were reused "
    "— only the new migration (`AddGeoEntityAttributeTable`) was applied on top, whose one-time backfill "
    "populated `geo_entity_attribute` (1,200,000 rows = 6 attributes × 200,000) from the existing data, so "
    "all three states share byte-identical underlying data."
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
lines.append("| Search case | Sort | Page | Type filter | Before (ms) | After (ms) | After fix (ms) |")
lines.append("|---|---|---|---|---:|---:|---:|")
for k in keys:
    case, sort_by, page, filt = k
    b = bmap[k]["MedianMs"]
    a = amap[k]["MedianMs"]
    x = fmap[k]["MedianMs"]
    lines.append(f"| {case} | {sort_by} | {page} | {filt} | {b:.1f} | {a:.1f} | {x:.1f} |")
lines.append("")
lines.append(
    "Raw captured SQL text, bound parameters (array parameters truncated to 10 entries + a total count in "
    "the human-readable dump — the SQL text itself is never truncated) and full `EXPLAIN (ANALYZE, BUFFERS)` "
    "plan text for every one of these statements (60 combinations × 3 statements × 3 states) are saved "
    "verbatim in `tools/GeoAssets.Bench.GetPageAsync/before.json` / `after.json` / `after-fix.json`. The "
    "tables below summarize each statement's SQL shape, its Seq-Scan verdict against the table it actually "
    "queries, and its share of that combination's total SQL time."
)
lines.append("")


def classify(sql):
    if "jsonb_each_text" in sql:
        return "attr-match (raw jsonb_each_text on geo_entity)"
    if re.search(r"FROM\s+geo_entity_attribute", sql):
        return "attr-match (indexed geo_entity_attribute)"
    if sql.strip().lower().startswith("select count"):
        return "COUNT"
    return "paged SELECT"


def target_table(kind):
    if kind == "attr-match (indexed geo_entity_attribute)":
        return "geo_entity_attribute"
    return "geo_entity"


def seq_scan_on(plan, table):
    return bool(re.search(rf"Seq Scan on {re.escape(table)}\b", plan))


lines.append("## Per-combination detail")
lines.append("")
for k in keys:
    case, sort_by, page, filt = k
    b = bmap[k]
    a = amap[k]
    x = fmap[k]
    lines.append(
        f"<details>\n<summary>{case} | sort={sort_by} | {page} | type filter={filt} "
        f"— before {b['MedianMs']:.1f} ms, after {a['MedianMs']:.1f} ms, after fix {x['MedianMs']:.1f} ms"
        f"</summary>\n"
    )
    for label, r in [("Before", b), ("After", a), ("After fix", x)]:
        lines.append(f"**{label}**\n")
        lines.append("| Statement | Seq Scan (own table) | Exec time (ms) | % of total |")
        lines.append("|---|---|---:|---:|")
        for s in r["Statements"]:
            kind = classify(s["Sql"])
            table = target_table(kind)
            seq = "yes" if seq_scan_on(s["Plan"], table) else "no"
            exec_ms = s["ExecutionTimeMs"]
            exec_str = f"{exec_ms:.1f}" if exec_ms is not None else "n/a"
            lines.append(f"| {kind} | {seq} ({table}) | {exec_str} | {s['PercentOfTotal']:.1f}% |")
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

SHAPES = [
    "attr-match (raw jsonb_each_text on geo_entity)",
    "attr-match (indexed geo_entity_attribute)",
    "COUNT",
    "paged SELECT",
]


def aggregate(data_map):
    buckets = {s: [] for s in SHAPES}
    for r in data_map.values():
        for s in r["Statements"]:
            kind = classify(s["Sql"])
            table = target_table(kind)
            buckets[kind].append((s["ExecutionTimeMs"], seq_scan_on(s["Plan"], table)))
    out = {}
    for shape, vals in buckets.items():
        if not vals:
            continue
        times = [v[0] for v in vals if v[0] is not None]
        scans = sum(1 for v in vals if v[1])
        out[shape] = (sum(times) / len(times), max(times), scans, len(vals))
    return out


bagg = aggregate(bmap)
aagg = aggregate(amap)
xagg = aggregate(fmap)

lines.append("Aggregated across all 60 combinations, grouped by statement shape:\n")
lines.append(
    "| Statement | Before avg (ms) | Before max (ms) | Before Seq Scans | After avg (ms) | After max (ms) | "
    "After Seq Scans | After-fix avg (ms) | After-fix max (ms) | After-fix Seq Scans |"
)
lines.append("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|")


def cell_avg(v):
    return f"{v[0]:.1f}" if v else "n/a"


def cell_max(v):
    return f"{v[1]:.1f}" if v else "n/a"


def cell_scans(v):
    return f"{v[2]}/{v[3]}" if v else "n/a"


for shape in SHAPES:
    b = bagg.get(shape)
    a = aagg.get(shape)
    x = xagg.get(shape)
    if b is None and a is None and x is None:
        continue
    lines.append(
        f"| {shape} | {cell_avg(b)} | {cell_max(b)} | {cell_scans(b)} | "
        f"{cell_avg(a)} | {cell_max(a)} | {cell_scans(a)} | "
        f"{cell_avg(x)} | {cell_max(x)} | {cell_scans(x)} |"
    )
lines.append("")

lines.append(
    "**The always-run `jsonb_each_text` attribute-expansion subquery was the dominant, unconditional cost "
    "before the fix — and the 171 indexes alone never touched it.** It ran on *every* `SearchText` query "
    "regardless of whether the search was even about custom attributes (it ran for \"rare name\" too), "
    "averaged ~884-888 ms before *and* after the 171 indexes, and was the single biggest line item in every "
    "combination."
)
lines.append("")
lines.append(
    "**After the fix (`fbe637c`), that statement is replaced by an index-backed lookup against "
    "`geo_entity_attribute`** (trigram GIN on `Key`/`Value`), and it is no longer the dominant cost: for the "
    "narrow-match cases (rare name, rare attribute value, no match) it now runs in low single-digit "
    "milliseconds, and total `GetPageAsync` latency for those cases dropped from ~730-1100 ms to **under "
    "10 ms** — the ~884 ms fixed floor is gone. For `SearchText` values that still match a huge fraction of "
    "the table (\"attribute key match\" = `altura_m`, matching all 200,000 rows; \"common term\" = "
    "`concreto`, ~66,666 rows), the attribute-match statement itself now takes 115-320 ms — returning and "
    "de-duplicating a 66k-200k row id set is real work even with an index seek — down from the ~884-1120 ms "
    "unconditional scan, but not free."
)
lines.append("")
lines.append(
    "**The COUNT seq-scan-on-huge-array issue Phase 1 flagged is only partially resolved.** It still appears "
    "in exactly the same 12/60 cases (attribute key match and common term, both without an AssetTypeId "
    "filter): once the `\"Id\" = ANY(@matchingAttributeIds)` parameter built from the attribute-match "
    "statement's result is tens of thousands of entries long, the planner still abandons the Name/Description "
    "trigram indexes for that OR-branch and sequentially scans `geo_entity` to evaluate array membership — "
    "370 ms and 195 ms respectively for the two representative no-filter cases measured here, versus near-"
    "instant paged SELECTs (1-11 ms) once the array is small or an AssetTypeId filter narrows the candidate "
    "set first. This is a known, unaddressed residual cost of the chosen fix (normalizing the attribute match "
    "didn't change how its *result* feeds into COUNT/paged SELECT), not a regression — and not something "
    "this ticket's fix was aimed at."
)
lines.append("")
lines.append(
    "**Net effect on end-to-end latency:** the narrow-match cases (rare name, rare attribute value, no "
    "match) went from ~730-1100 ms to **1.8-6.8 ms** — a 150-600x improvement, since they no longer pay the "
    "~884 ms unconditional floor at all. The broad-match, no-type-filter cases (attribute key match, common "
    "term) improved substantially but did not reach the same floor: ~1.2-2.0 s down to **280-840 ms**, still "
    "dominated by the attribute-match statement's own cost on a huge result set plus the residual COUNT "
    "seq-scan. Adding an AssetTypeId filter to a broad-match query (which narrows the candidate set via its "
    "own B-tree index before the array check) brings even these down further (e.g. \"common term\" + Poste "
    "filter: 1240.7 ms after-indexes-only → 278.6 ms after fix). The ~884 ms unconditional floor that made "
    "*every* search slow regardless of what it searched for is gone; what remains scales with how broad the "
    "attribute match actually is, which is the expected, acceptable shape for this kind of query."
)
lines.append("")

with open("../../docs/perf/XD01-171-baseline.md", "w") as f:
    f.write("\n".join(lines))

print("wrote docs/perf/XD01-171-baseline.md,", len(lines), "lines")
