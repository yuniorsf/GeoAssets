# ADR-002 Implementation Agent Prompts (English)

Ready-to-use, self-contained English prompts for implementation agents covering every track of [ADR-002 — Provider Plugin Architecture, Hybrid Assembly Discovery & Hybrid LOD GIS Stack](ADR-002-provider-plugins-and-hybrid-lod-gis.md).

> **Usage**: Hand each Track prompt to an implementation agent (or orchestrate them sequentially/in parallel respecting the dependency graph: `Track 0` → `Track 3`, `Track 1` → `Track 2` → `Track 4` → `Track 5` → `Track 6`). Every prompt cites its exact Jira Epic and child tickets.

---

## Global Agent Guardrails (Include in Every Run)

```text
Repository: /Users/yuniorsf/WORK/GeoAssets (.NET 10, LangVersion 14, Nullable=enable, TreatWarningsAsErrors=true).
Authoritative ADR: docs/architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md.
Follow CLAUDE.md and .agents/skills/architecture-conventions/SKILL.md strictly:
- Never violate Invariant 1 (ProviderPostgreSqlReferenceTests): only GeoAssets.Server may reference GeoAssets.Provider.PostgreSQL.
- Every contract crossing plugin/ALC boundaries MUST live in GeoAssets.Core (Default AssemblyLoadContext).
- Never use MEF (System.ComponentModel.Composition) for spatial provider plugins.
- Write comprehensive unit/integration/architecture tests and verify `dotnet build GeoAssets.sln` + relevant `dotnet test` suites + `python3 scripts/verify-solid-contracts.py --target .` exit 0 before finishing.
```

---

## Prompt — Track 0: GIS Render Performance Baseline (`XD01-413` / `XD01-420`)

```text
Implement Jira Epic XD01-413 (child task XD01-420) per docs/architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md (Track 0).

Goal:
Create a deterministic GIS render performance benchmark in `tools/GeoAssets.Bench.Render` modeled after `tools/GeoAssets.Bench.GetPageAsync`.

Requirements:
1. Seed a deterministic 200,000-feature PostGIS dataset containing representative points, dense polylines, and polygons across multiple asset types with MinZoom/MaxZoom metadata.
2. Measure and report p50 and p95 payload bytes, server execution time (ms), and simulated/headless client render time across three standard viewports: zoom 8 (overview), zoom 12 (mid-zoom), and zoom 16 (street/edit zoom).
3. Record the initial baseline measurements in `docs/perf/render-baseline.md` with clear instructions to re-run the benchmark after Tracks 3, 4, and 5.
4. Keep the tool cleanly isolated under `tools/` (updating `GeoAssets.sln` if appropriate) without introducing any production host coupling.

Verification:
- `dotnet build tools/GeoAssets.Bench.Render/GeoAssets.Bench.Render.csproj` passes with zero warnings.
- `docs/perf/render-baseline.md` is committed with structured tables for z8, z12, and z16.
```

---

## Prompt — Track 1: Provider Plugin Foundation & Code-Smell Remediation (`XD01-414` / `XD01-421`–`XD01-424`)

```text
Implement Jira Epic XD01-414 (child tasks XD01-421, XD01-422, XD01-423, XD01-424) per docs/architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md (Track 1).

Requirements:
1. (XD01-421) Plugin Module Contract & Fail-Fast Registry:
   - Add `core/GeoAssets.Core/Interfaces/ProviderHostTargets.cs` (`[Flags] enum ProviderHostTargets { None = 0, Server = 1, Browser = 2, MobileDesktop = 4, All = Server | Browser | MobileDesktop }`).
   - Add `core/GeoAssets.Core/Interfaces/IProviderPluginModule.cs` with `string PluginId { get; }`, `ProviderHostTargets SupportedHosts { get; }`, and `void RegisterServices(IServiceCollection services, IConfiguration configuration)`.
   - Refactor `core/GeoAssets.Core/Services/ProviderPluginRegistry.cs` to back lookups with `Dictionary<string, IProviderPlugin>(StringComparer.OrdinalIgnoreCase)` and throw `InvalidOperationException` (fail-fast) when two registered plugins share the same `Id`.
   - Add unit tests in `tests/GeoAssets.Core.Tests/Plugins/ProviderPluginRegistryTests.cs`.

2. (XD01-422) Eliminate `is IWmsProvider` Type-Switching:
   - Add `core/GeoAssets.Core/Interfaces/ProviderRenderCapabilities.cs` (`[Flags] enum ProviderRenderCapabilities { None, VectorFeatures, RasterTiles, VectorTiles, HybridLod, VectorExport, VectorImport, InteractiveEditing }` and `interface IProviderCapabilities`).
   - Add `GetRenderCapabilities(this IAssetProvider provider)` in `core/GeoAssets.Core/Providers/AssetProviderExtensions.cs` that ALWAYS calls `provider.UnwrapToConcrete()` first, checks `IProviderCapabilities`, and falls back to `IWmsProvider -> RasterTiles` or default vector capabilities.
   - Implement `IProviderCapabilities` on `WmsAssetProvider` returning `RasterTiles`.
   - Introduce `IProviderMapLayerRenderer` strategy chain (`CanRender`, `RenderAsync`, `RemoveAsync`) in `apps/GeoAssets.Shared/Services/Rendering/` with `WmsMapLayerRenderer` and `VectorFeatureMapLayerRenderer`, registered in DI.
   - Replace all 7 `is [not] IWmsProvider` checks in `ProviderConnectionMapRenderer.cs`, `ProviderPoolPanel.razor`, `ProviderPoolPanel.razor.cs`, and `MapContainer.razor.cs`.
   - Gate Export GeoJSON / Shapefile buttons on `VectorExport` and Import GeoJSON on `VectorImport` in `ProviderPoolPanel.razor` so raster-only providers do not expose broken vector actions.
   - Add/update tests in `tests/GeoAssets.Shared.Tests/` including a decorator-wrapped `ValidatingAssetProvider(wmsProvider)` test proving `UnwrapToConcrete()` works.

3. (XD01-423) Fix `IWorkbookReader` DIP Violation & Generalize `INativeFilePicker`:
   - Move `providers/GeoAssets.Provider.Xlsx/IWorkbookReader.cs` to `core/GeoAssets.Core/Interfaces/IWorkbookReader.cs` (namespace `GeoAssets.Core.Interfaces`). Add `GeoAssets.Core` reference to `GeoAssets.Provider.Xlsx.csproj`.
   - Remove all `using GeoAssets.Provider.Xlsx;` directives from `apps/GeoAssets.IpBilling.Server/IpBilling{Fiscalizacao,Levantamento,Oficio}RestApiExtensions.cs`.
   - Generalize `core/GeoAssets.Core/Interfaces/INativeFilePicker.cs` from `PickZipArchiveAsync` to `PickBinaryFileAsync(IReadOnlyList<string>? acceptedExtensions = null, CancellationToken ct = default)` (update MAUI implementation and tests).
   - Update `apps/GeoAssets.Shared/Components/Providers/ProviderConfigForm.razor` and `.razor.cs` to pass `field.Accept` extensions to `PickBinaryFileAsync` and remove the hardcoded `.zip` assumption for `BinaryFile`.

4. (XD01-424) Split Shapefile Registrations & Add Architecture Guardrails:
   - Split `providers/GeoAssets.Provider.Shapefile/ShapefileServiceExtensions.cs` into `AddGeoAssetsShapefilePlugin()` and `AddGeoAssetsShapefileTools()` (keep `AddGeoAssetsShapefile()` calling both for compatibility). Migrate `apps/GeoAssets.FieldService.Server/Program.cs` to `AddGeoAssetsShapefileTools()`.
   - Add architecture tests asserting: (a) zero `using GeoAssets.Provider.*` in `apps/GeoAssets.IpBilling.Server` endpoints, and (b) zero `is IWmsProvider` or `is not IWmsProvider` in `apps/GeoAssets.Shared/Components/`.

Verification:
- Run `dotnet build GeoAssets.sln` (zero warnings) and `dotnet test` across Core, Shared, IpBilling, Server (Architecture), and Provider test projects.
```

---

## Prompt — Track 2: Hybrid Assembly-Discovery Provider Plugin Loader (`XD01-415` / `XD01-425`–`XD01-428`)

```text
Implement Jira Epic XD01-415 (child tasks XD01-425, XD01-426, XD01-427, XD01-428) per docs/architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md (Track 2). Prerequisite: Track 1 (XD01-414) is merged.

Requirements:
1. (XD01-425) Implement `IProviderPluginModule` in Spatial Providers:
   - `RestProviderPluginModule` (`PluginId = "rest"`, `SupportedHosts = All`) in `GeoAssets.Provider.Rest`.
   - `WfsProviderPluginModule` (`PluginId = "wfs"`, `SupportedHosts = All`) in `GeoAssets.Provider.WFS`.
   - `WmsProviderPluginModule` (`PluginId = "wms"`, `SupportedHosts = All`) in `GeoAssets.Provider.WMS`.
   - `ShapefileProviderPluginModule` (`PluginId = "shapefile"`, `SupportedHosts = All`) in `GeoAssets.Provider.Shapefile`.
   - `PostgresProviderPluginModule` (`PluginId = "postgres"`, `SupportedHosts = Server`) in `GeoAssets.Provider.PostgreSQL` (registers `AddGeoAssetsPostgres()` only; server pushdowns remain in `PostgresSpatialModule`).

2. (XD01-426) Core Trimming-Safe Discovery Bootstrapper (`core/GeoAssets.Core/Plugins/`):
   - Add `IPluginAssemblySource`, `StaticAssemblyPluginSource`, `ProviderPluginDiscoveryOptions` (`HostTarget`, `EnabledPluginIds`, `DisabledPluginIds`), and `ProviderPluginRegistrationExtensions.AddDiscoveredProviderPlugins(...)`.
   - Scan concrete public `IProviderPluginModule` types with `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]`.
   - Fail fast on `ReflectionTypeLoadException` or duplicate `PluginId`; cleanly skip modules whose `SupportedHosts` does not match `HostTarget` or excluded by allowlist/denylist.
   - Register `ProviderPluginRegistry` via `TryAddSingleton`.
   - Add comprehensive unit tests in `tests/GeoAssets.Core.Tests/Plugins/ProviderPluginDiscoveryTests.cs`.

3. (XD01-427) Isolated Dynamic Directory Loader (`core/GeoAssets.Hosting.Abstractions/Plugins/`):
   - Implement `PluginLoadContext : AssemblyLoadContext` using `AssemblyDependencyResolver(pluginMainAssemblyPath)`.
   - Delegate shared host contracts (`GeoAssets.Core`, `GeoAssets.Identity`, `GeoAssets.Hosting.Abstractions`, `Microsoft.Extensions.*`) to `AssemblyLoadContext.Default`.
   - Override `LoadUnmanagedDll(string unmanagedDllName)` using `ResolveUnmanagedDllToPath` + `LoadUnmanagedDllFromPath` (enables native GDAL plugins in Track 6).
   - Implement `DirectoryPluginAssemblySource : IPluginAssemblySource` (scanning `GeoAssets:ProviderPlugins:Directory`, default `plugins/providers/`, supporting isolated subfolders `<PluginName>/<PluginName>.dll` and flat `GeoAssets.Provider.*.dll`), `IPluginLoadContextRegistry`, and `AddProviderPluginsFromDirectory(...)`.
   - Add fixture DLL integration tests in `tests/GeoAssets.Hosting.Abstractions.Tests/Plugins/DirectoryPluginAssemblySourceTests.cs` verifying shared `IProviderPlugin` type identity across ALC boundaries and fail-fast on malformed DLLs.

4. (XD01-428) Host Migrations & Packaging:
   - Migrate `apps/GeoAssets.Web/Program.cs` to `AddDiscoveredProviderPlugins` with `StaticAssemblyPluginSource` (`HostTarget = Browser`).
   - Migrate `apps/GeoAssets.MAUI/MauiProgram.cs` to `AddDiscoveredProviderPlugins` (`HostTarget = MobileDesktop`).
   - Wire `AddProviderPluginsFromDirectory` in `apps/GeoAssets.Server/Program.cs` while keeping `PostgresSpatialModule` and `ProviderPostgreSqlReferenceTests` (Invariant 1) green without duplicate `IProviderPlugin` registrations.
   - Add an opt-in MSBuild `<EnableDynamicLoading>true</EnableDynamicLoading>` packaging target for drop-in server plugins.

Verification:
- `dotnet build GeoAssets.sln` (zero warnings) and `dotnet test` for Core, Hosting.Abstractions, Server (`Category=Architecture`, `Catalog`), Shared, and Provider suites.
```

---

## Prompt — Track 3: Existing-Path GIS Performance Quick Wins (`XD01-416` / `XD01-429`–`XD01-430`)

```text
Implement Jira Epic XD01-416 (child tasks XD01-429, XD01-430) per docs/architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md (Track 3).

Requirements:
1. (XD01-429) Client Viewport Quick Wins & Bug Fixes:
   - Debounce `moveend` and `zoomend` into a single 150ms event in `apps/GeoAssets.Shared/wwwroot/js/geoassets.js` and `MapContainer.razor.cs`.
   - Propagate `CancellationToken` into `HttpClient.GetAsync` in `providers/GeoAssets.Provider.Rest/RestAssetProvider.cs` so superseded viewport requests cancel cleanly; drop stale `requestAnimationFrame` batches.
   - Diff features on pan/zoom instead of clearing all map layers; request a padded bounding box; pass current `zoom` with viewport queries.
   - Fix `styleMap` dropped on the raw-JSON pan path (`MapInteropService.cs:117-123`), fix culture-sensitive bbox URL formatting (`CultureInfo.InvariantCulture` in `RestAssetProvider.cs:103,114,189`), and stop re-sending the full `styleMap` per chunk.

2. (XD01-430) Server Render Query Quick Wins:
   - Add `Zoom` to `RenderQueryOptions` (`core/GeoAssets.Core/Rendering/`) and filter `AssetType.MinZoom`/`MaxZoom` in SQL inside `PostgresAssetRenderQuery`.
   - Push deterministic `ORDER BY` + `LIMIT` into SQL and project only render columns.
   - Enable compact JSON serialization and `AddResponseCompression` (Brotli/Gzip) on `GeoAssets.Server`.
   - Eliminate the `Multi*` GeoJSON round-trip in `GeoGeometry.FromNts` (`core/GeoAssets.Core/Models/Geometry/GeoGeometry.cs`) and cache `JsonSerializerOptions` in `GeoGeometryConverter`.
   - Re-run `tools/GeoAssets.Bench.Render` and append Track 3 results to `docs/perf/render-baseline.md`.

Verification:
- Unit tests for cancellation, `InvariantCulture` bbox URLs, `GeoGeometry.FromNts`, and SQL `MinZoom`/`MaxZoom` filtering.
- `dotnet build GeoAssets.sln` and relevant `dotnet test` suites pass.
```

---

## Prompt — Track 4: Server Tile Ports & PostGIS MVT / SkiaSharp Raster Sources (`XD01-417` / `XD01-431`–`XD01-434`)

```text
Implement Jira Epic XD01-417 (child tasks XD01-431, XD01-432, XD01-433, XD01-434) per docs/architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md (Track 4). Prerequisites: Tracks 1, 2, and 3.

Requirements:
1. (XD01-431) Shared Tile & LOD Contracts (`core/GeoAssets.Core/Rendering/`):
   - Add `TileAddress(int Z, int X, int Y)` value object with validated bounds and `EPSG:3857` / `EPSG:4326` envelope math.
   - Add `TileLayerSet`, `TileRenderTier` (`Aggregate`, `Raster`, `Vector`), `TilePayload`, `IVectorTileSource`, `IRasterTileSource`, pure `ILodPolicy.Decide(string layerId, int zoom)`, `ITileCache`, and `ITileInvalidation`.
   - Add unit tests for `TileAddress` envelope math and `LodPolicy.Decide` truth table.

2. (XD01-432) PostGIS Migration & `PostgresVectorTileSource` (`providers/GeoAssets.Provider.PostgreSQL`):
   - Add EF Core migration enforcing `geometry(Geometry, 4326)` SRID/type constraint and a stored generated `geom_3857` column with GiST index (strip `#pragma warning disable 612, 618` per CLAUDE.md).
   - Implement `PostgresVectorTileSource : IVectorTileSource` executing a single-round-trip `ST_AsMVT` query (`ST_TileEnvelope`, `ST_AsMVTGeom` extent 4096 / buffer 64, per-zoom `ST_SimplifyPreserveTopology`, SQL min-zoom filtering, low-z point grid aggregation, per-tile feature budget, strict RLS/org scoping).
   - Wire after-commit dirty-tile invalidation from repository writes / `PostgresAssetMutationBatchApplier` into `ITileInvalidation`.
   - Add `PostgresFact` integration tests decoding MVT payloads and verifying org isolation, min-zoom, and budget.

3. (XD01-433) `SkiaRasterTileSource`:
   - Refactor the SkiaSharp painter out of `WmsEndpointExtensions.cs` into `SkiaRasterTileSource : IRasterTileSource` supporting `EPSG:3857`, symbol buffer (no clipped points), and 2x2 metatile rendering, driven by `LayerResolver`.

4. (XD01-434) Authenticated Tile Endpoints & Immutable Caching (`PostgresSpatialModule`):
   - Map authenticated `GET /tiles/{layer}/{v}/{z}/{x}/{y}.mvt`, `GET /tiles/{layer}/{v}/{z}/{x}/{y}.png`, and `GET /tiles/style.json` (TileJSON + `LayerResolver` vector style + `LodPolicy` `minzoom`/`maxzoom`).
   - Wire OutputCache / HybridCache keyed by `(orgScope, grantScopeHash, layerVersion)` returning `ETag` (`304 Not Modified`) and `Cache-Control: immutable`, behind the gateway `tiles` rate-limit policy.
   - Ensure tile requests never trigger full-catalog `WarmCacheAsync` middleware.
   - Re-run `tools/GeoAssets.Bench.Render` and record results in `docs/perf/render-baseline.md` (targets: z8 bytes >=10x smaller, server p95 <50ms).

Verification:
- Unit, cache/ETag (304), dirty-tile invalidation, and `PostgresFact` MVT decode tests pass.
```

---

## Prompt — Track 5: Client Hybrid LOD Renderer (`XD01-418` / `XD01-435`–`XD01-436`)

```text
Implement Jira Epic XD01-418 (child tasks XD01-435, XD01-436) per docs/architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md (Track 5). Prerequisite: Track 4 (XD01-417).

Requirements:
1. (XD01-435) `MapRenderMode.MapLibre`:
   - Add `MapRenderMode.MapLibre` and implement `apps/GeoAssets.Shared/wwwroot/js/geoassets.maplibre.js` behind the existing `IMapInterop` contract.
   - Load `/tiles/style.json` with per-layer raster + MVT sources and `minzoom`/`maxzoom` thresholds.
   - Authenticate tile requests via MapLibre `transformRequest` (bearer header).
   - Use GPU feature queries for click/hover instead of per-feature DOM/SVG layers, and integrate `terra-draw` for draw/edit/snap.

2. (XD01-436) Hybrid LOD Strategy & Editing Coexistence:
   - Implement and register `HybridLodMapLayerRenderer : IProviderMapLayerRenderer` (plugging into Track 1B's strategy chain without modifying `ProviderPoolPanel.razor` or `MapContainer.razor.cs`).
   - Editing coexistence: selecting/editing a feature fetches its GeoJSON overlay via `IAssetProvider` and hides the underlying MVT tile copy by ID filter / `feature-state` until the dirty tile re-serves; snapping candidates come from MVT features at interaction zoom.
   - Keep Leaflet as the default `MapRenderMode` until parity (protecting MAUI), adding an interim Leaflet XYZ raster + vector tile layer split below/above threshold.

Verification:
- Headless browser check of `MapRenderMode.MapLibre` across zoom thresholds (raster -> vector swap, edit overlay hiding tile copy, snap, zero full-map clear on pan).
- `GeoAssets.Shared.Tests` and `dotnet build GeoAssets.sln` pass with zero warnings.
```

---

## Prompt — Track 6: Unified GDAL Cloud-Native Plugin, MAUI Offline & LOD Calibration (`XD01-419` / `XD01-437`–`XD01-439`)

```text
Implement Jira Epic XD01-419 (child tasks XD01-437, XD01-438, XD01-439) per docs/architecture/ADR-002-provider-plugins-and-hybrid-lod-gis.md (Track 6). Prerequisites: Tracks 2, 4, and 5.

Requirements:
1. (XD01-437) Unified GDAL Cloud-Native Plugin (`providers/GeoAssets.Provider.Gdal`):
   - Create `GeoAssets.Provider.Gdal` implementing `IProviderPluginModule` (`PluginId = "gdal-cloud-native"`, `SupportedHosts = ProviderHostTargets.Server | ProviderHostTargets.MobileDesktop`).
   - Consolidate `FileGeodatabase` export with `PMTiles`, `FlatGeobuf`, `GeoParquet`, and `COG` (XD01-168) readers/exporters under a single process-safe `GdalNativeInitializer` (`GdalBase.ConfigureAll()`) so native GDAL initializes once inside one `PluginLoadContext` via `LoadUnmanagedDll`.
   - Register `PMTilesVectorTileSource : IVectorTileSource` and `CogRasterTileSource : IRasterTileSource`.

2. (XD01-438) MAUI Offline PMTiles Packs:
   - Enable MAUI offline `.pmtiles` packs reading through the existing `SqliteLocalVectorTileCache`, picked via Track 1C's generalized `INativeFilePicker.PickBinaryFileAsync([".pmtiles"])` and discovered via `StaticAssemblyPluginSource` (`ProviderHostTargets.MobileDesktop`).

3. (XD01-439) Density-Based LOD Calibration Job & Fail-Fast Validation:
   - Implement a calibration job computing per-`(org, assetType)` tile density percentiles (features x vertices per tile) to emit recommended `LodPolicy` thresholds.
   - Enforce strict startup validation for `LodPolicy` configuration so invalid thresholds (inverted bounds, out-of-range zooms) fail fast before serving traffic.
   - Add unit tests proving threshold changes alter `LodPolicy.Decide` and invalid config fails startup.

Verification:
- GDAL plugin ALC tests, MAUI offline PMTiles tests, `LodPolicy` unit & startup failure tests, `dotnet build GeoAssets.sln`, `python3 scripts/verify-solid-contracts.py --target .`, and `./scripts/verify-agent-worktree.sh .` exit 0.
```
